import Foundation

/// Claude Code's own `/usage`, asked of the binary rather than of the endpoint
/// behind it.
///
/// The token path works, but it cannot stop asking: Claude Code files a *new*
/// keychain item on every rotation, and the new item's access list does not
/// carry this app. So a grant the user gives is only ever good until the next
/// rotation, and the password dialogue comes back roughly hourly for a reading
/// nobody asked to be interrupted for.
///
/// `claude "/usage"` answers with the same figures, off a credential the CLI
/// already holds, and needs no keychain access from this app at all. It costs a
/// subprocess, so it is throttled by its caller — see
/// `ClaudeOAuthProvider.cliRefreshInterval`.
struct ClaudeUsageCLI: Sendable {
    /// Where the binary was found.
    let binary: URL
    /// How its output is obtained. Injected for the same reason the provider's
    /// credential source is: a test that actually spawned Claude Code would
    /// need a login and a network to be deterministic, and the part worth
    /// testing — what the text means — is downstream of this.
    let output: @Sendable (ClaudeProfile) throws -> String

    init(binary: URL, output: @escaping @Sendable (ClaudeProfile) throws -> String) {
        self.binary = binary
        self.output = output
    }

    /// Long enough for a cold Node start on a busy machine, short enough that a
    /// wedged process cannot hold a refresh open. A timeout kills the process.
    static let timeout: TimeInterval = 20

    // MARK: - Finding the binary

    /// Every path Claude Code installs itself to, newest installer first.
    ///
    /// `which` is no help here: the app is launched from Finder, so it inherits
    /// a `PATH` of `/usr/bin:/bin:/usr/sbin:/sbin` and none of these are on it.
    private static let searchPaths = [
        ".local/bin/claude",     // the native installer
        ".claude/local/claude",  // the migrate-from-npm layout
        ".bun/bin/claude"
    ]

    /// Relative to the filesystem root rather than absolute, so a test can point
    /// the whole search at a temporary directory. Left absolute, `locate` would
    /// find the machine's own Claude Code however carefully a test set its home
    /// up, and would pass or fail depending on what the developer has installed.
    private static let systemPaths = [
        "opt/homebrew/bin/claude",
        "usr/local/bin/claude"
    ]

    /// Nil means Claude Code is not installed in any of the places it installs
    /// itself, and the caller should use the token path instead.
    static func locate(home: URL = ClaudeProfile.homeDirectory,
                       root: URL = URL(fileURLWithPath: "/"),
                       fileManager: FileManager = .default) -> ClaudeUsageCLI? {
        let candidates = searchPaths.map { home.appendingPathComponent($0) }
            + systemPaths.map { root.appendingPathComponent($0) }
        guard let found = candidates.first(where: {
            fileManager.isExecutableFile(atPath: $0.path)
        }) else { return nil }
        return ClaudeUsageCLI(binary: found) { try run(binary: found, profile: $0) }
    }

    // MARK: - Asking it

    /// Runs `/usage` for one profile and returns what it reported.
    ///
    /// Runs off the cooperative pool: reading a pipe to exhaustion blocks the
    /// thread it is on, and the caller is an actor whose other work — the
    /// token path this falls back to — would be stuck behind it.
    func read(profile: ClaudeProfile, now: Date = Date()) async throws -> [LimitWindow] {
        let text = try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .utility).async {
                continuation.resume(with: Result { try self.output(profile) })
            }
        }
        return try Self.parse(text, now: now)
    }

    private static func run(binary: URL, profile: ClaudeProfile) throws -> String {
        // A directory of its own, so a session artifact written on the way past
        // lands somewhere disposable rather than in whatever directory the app
        // happened to be launched from.
        let scratch = FileManager.default.temporaryDirectory
            .appendingPathComponent("quotaarc-usage-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: scratch) }

        var environment = ProcessInfo.processInfo.environment
        // Only for a named profile. Pointing the variable at `~/.claude`
        // explicitly is not the same as leaving it unset — Claude Code reads
        // `.claude.json` from beside the home directory when it is unset and
        // from inside the config directory when it is set, so setting it for
        // the default profile would send it looking in the wrong place.
        if profile.slug != nil {
            environment["CLAUDE_CONFIG_DIR"] = profile.configDirectory.path
        }
        environment["PWD"] = scratch.path

        let process = Process()
        process.executableURL = binary
        process.arguments = ["/usage"]
        process.currentDirectoryURL = scratch
        process.environment = environment
        // Never a terminal. Left inheriting the app's stdin, `claude` waits for
        // input that will never come and the timeout is the only thing that
        // ends it.
        process.standardInput = FileHandle.nullDevice
        let output = Pipe()
        process.standardOutput = output
        process.standardError = Pipe()

        try process.run()

        let watchdog = DispatchWorkItem {
            if process.isRunning { process.terminate() }
        }
        DispatchQueue.global(qos: .utility)
            .asyncAfter(deadline: .now() + Self.timeout, execute: watchdog)
        defer { watchdog.cancel() }

        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()

        guard process.terminationStatus == 0 else {
            Log.usage.debug("claude /usage exited \(process.terminationStatus)")
            // A non-zero exit is Claude Code declining to answer, which in
            // practice means it has no login of its own. Not an error worth
            // showing — the caller falls back to the token path, which can say
            // something more precise about why.
            throw UsageProviderError.needsAuth
        }
        guard let text = String(data: data, encoding: .utf8), !text.isEmpty else {
            throw UsageProviderError.badResponse(status: 0)
        }
        return text
    }

    // MARK: - Reading what it said

    /// The lines `/usage` leads with, e.g.
    ///
    ///     Current session: 38% used · resets Sep 7 at 2:59pm (Asia/Jakarta)
    ///     Current week (all models): 4% used · resets Sep 14 at 5:59am (Asia/Jakarta)
    ///
    /// Everything below them is prose about what drove the usage, and is
    /// ignored — it is approximate by its own admission, and none of it is a
    /// limit.
    private static let line = try! NSRegularExpression(
        pattern: #"^Current (?:(session)|week \(([^)]+)\)):\s*(\d+)%\s*used(?:\s*·\s*resets\s*(.+?))?\s*$"#,
        options: [.anchorsMatchLines]
    )

    static func parse(_ text: String, now: Date = Date()) throws -> [LimitWindow] {
        let range = NSRange(text.startIndex..., in: text)
        var windows: [LimitWindow] = []

        for match in line.matches(in: text, range: range) {
            func group(_ index: Int) -> String? {
                guard let r = Range(match.range(at: index), in: text) else { return nil }
                return String(text[r])
            }
            guard let percent = group(3).flatMap(Double.init) else { continue }

            let kind = group(1) != nil ? "session" : Self.kind(forWeek: group(2) ?? "")
            guard !windows.contains(where: { $0.id == kind }) else { continue }

            windows.append(LimitWindow(
                id: kind,
                // The same labels the endpoint path produces, so a reading
                // archived under one source still matches when the other takes
                // over — the archive keys on the window id and the tooltip
                // shows the label, and two spellings would read as two windows.
                label: UsageResponse.label(forKind: kind),
                usedFraction: percent / 100,
                // Kept even when the date is unparseable. `resetsAt` is
                // optional by design, and losing a percentage that parsed
                // perfectly well because the wording of a date changed is the
                // worse failure of the two.
                resetsAt: group(4).flatMap { Self.resetDate(from: $0, now: now) }
            ))
        }

        // Without the session window there is no headline, and the caller asks
        // for one by id. Better to fall back to the token path than to draw a
        // ring with a hole in it.
        guard windows.contains(where: { $0.id == "session" }) else {
            throw UsageProviderError.badResponse(status: 0)
        }
        return windows.sorted(by: UsageResponse.displayOrder)
    }

    /// `all models` → `weekly_all`, `Opus` → `weekly_opus`. The endpoint's own
    /// vocabulary, so `UsageResponse.label(forKind:)` can name both.
    private static func kind(forWeek text: String) -> String {
        let name = text.lowercased() == "all models"
            ? "all"
            : text.lowercased().replacingOccurrences(of: " ", with: "_")
        return "weekly_\(name)"
    }

    /// `Sep 7 at 2:59pm (Asia/Jakarta)` → a `Date`.
    ///
    /// No year is printed, so one is chosen: the candidate nearest `now`, over
    /// last year, this year and next. Anything else gets New Year's Eve wrong
    /// in one direction or the other — a window resetting on Jan 2, read on
    /// Dec 31, is next year's, and `Dec 31` read on `Jan 2` is last year's.
    static func resetDate(from text: String, now: Date) -> Date? {
        var stamp = text.trimmingCharacters(in: .whitespaces)
        var zone = TimeZone.current

        // The zone comes last, in brackets, and has to come off before the
        // am/pm fix below — `America/...` carries an "am" of its own.
        if let open = stamp.lastIndex(of: "("), stamp.hasSuffix(")") {
            let name = String(stamp[stamp.index(after: open)...].dropLast())
            zone = TimeZone(identifier: name) ?? zone
            stamp = String(stamp[..<open]).trimmingCharacters(in: .whitespaces)
        }
        stamp = stamp.replacingOccurrences(of: "am", with: "AM")
            .replacingOccurrences(of: "pm", with: "PM")

        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = zone

        // Two spellings, because the minutes are dropped when they are zero:
        // `Sep 7 at 2:59pm`, but `Sep 7 at 3pm` on the hour. A single
        // `h:mma` pattern reads the first and rejects the second, which is a
        // window that loses its reset time for one hour in sixty — long
        // enough to look like a bug and short enough to miss in a fixture.
        guard let parsed = ["MMM d 'at' h:mma", "MMM d 'at' ha"]
            .lazy
            .compactMap({ format -> Date? in
                formatter.dateFormat = format
                return formatter.date(from: stamp)
            })
            .first
        else { return nil }

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        var parts = calendar.dateComponents([.month, .day, .hour, .minute], from: parsed)
        let thisYear = calendar.component(.year, from: now)

        return [thisYear - 1, thisYear, thisYear + 1].compactMap { year -> Date? in
            parts.year = year
            return calendar.date(from: parts)
        }.min { abs($0.timeIntervalSince(now)) < abs($1.timeIntervalSince(now)) }
    }
}
