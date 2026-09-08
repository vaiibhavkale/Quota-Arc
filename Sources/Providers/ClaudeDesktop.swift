import Foundation

/// Claude Desktop on Mac writes the same plan percentages the settings page
/// shows into `~/Library/Application Support/Claude/plan-usage-history.json`.
///
/// The Windows app already reads this file, which is why a ring there fills
/// the moment the notch opens even when the OAuth token in the credential
/// store has aged out. Mac had only `claude /usage` and the keychain token:
/// a 20s CLI spawn plus an expired token left the ring on "Waiting for the
/// first reading..." while the numbers were sitting in this file.
enum ClaudeDesktop {
    static var supportDirectory: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/Claude")
    }

    static var historyURL: URL {
        supportDirectory.appendingPathComponent("plan-usage-history.json")
    }

    static var isPresent: Bool {
        FileManager.default.fileExists(atPath: historyURL.path)
    }

    static func historyStamp() -> Date? {
        (try? FileManager.default.attributesOfItem(atPath: historyURL.path))?[.modificationDate] as? Date
    }

    static func snapshotFromHistory(displayName: String = "Claude",
                                    json: String? = nil,
                                    now: Date = Date()) -> ProviderSnapshot? {
        guard let windows = windowsFromHistory(json: json, now: now), !windows.isEmpty else {
            return nil
        }
        return ProviderSnapshot(
            id: ClaudeProfile.defaultID,
            displayName: displayName,
            glyph: .claude,
            fidelity: .official,
            status: .ok,
            windows: windows,
            headlineID: "session"
        )
    }

    static func windowsFromHistory(json: String? = nil, now: Date = Date()) -> [LimitWindow]? {
        let text = json ?? (try? String(contentsOf: historyURL, encoding: .utf8))
        guard let text, let data = text.data(using: .utf8) else { return nil }
        do {
            let payload = try JSONDecoder().decode(HistoryFile.self, from: data)
            let samples = payload.samples.compactMap { sample -> Sample? in
                guard let usage = sample.u, let fh = usage.fh, let sd = usage.sd else { return nil }
                let ms = sample.t > 10_000_000_000 ? sample.t : sample.t * 1000
                return Sample(at: Date(timeIntervalSince1970: ms / 1000), fh: fh, sd: sd)
            }
            guard let latest = samples.last else { return nil }
            return [
                LimitWindow(id: "session", label: "Current session",
                            usedFraction: Double(latest.fh) / 100,
                            resetsAt: inferFiveHourReset(samples, now: now)),
                LimitWindow(id: "weekly_all", label: "All models",
                            usedFraction: Double(latest.sd) / 100,
                            resetsAt: inferWeeklyReset(samples, now: now))
            ]
        } catch {
            return nil
        }
    }

    /// The five-hour window starts at the last sample where usage left 0, or
    /// where a previous window had already expired and usage dropped.
    ///
    /// Never stamp `now` as the reset. That is what printed "Resetting..." on
    /// the tooltip with no time next to it: `ResetCopy` treats a reset at or
    /// before now as already happening, and hides the clock. Cursor and Codex
    /// always carry a future date, so they show "Resets Tue 4:50 PM".
    static func inferFiveHourReset(_ samples: [Sample], now: Date) -> Date? {
        let window: TimeInterval = 5 * 60 * 60
        guard let last = samples.last else { return nil }

        var start: Date?
        if samples.count >= 2 {
            for i in 1..<samples.count {
                let prev = samples[i - 1]
                let current = samples[i]
                if prev.fh == 0 && current.fh > 0 {
                    start = current.at
                } else if current.fh == 0 {
                    start = nil
                } else if let begun = start,
                          current.at.timeIntervalSince(begun) >= window,
                          current.fh < prev.fh {
                    start = current.at
                }
            }
        }

        if start == nil {
            if last.fh == 0 { return nil }
            let cutoff = now.addingTimeInterval(-window)
            start = samples.first { $0.at >= cutoff && $0.fh > 0 }?.at ?? last.at
        }

        guard let start else { return nil }
        let reset = start.addingTimeInterval(window)
        if reset > now { return reset }
        if last.fh == 0 { return nil }
        let fromLatest = last.at.addingTimeInterval(window)
        return fromLatest > now ? fromLatest : nil
    }

    /// The weekly window rolls when the all-models percentage drops hard.
    /// Claude Desktop does not write `resets_at`, so this is the same clock
    /// the settings page is on: seven days after that drop.
    static func inferWeeklyReset(_ samples: [Sample], now: Date) -> Date? {
        var drop: Date?
        if samples.count >= 2 {
            for i in 1..<samples.count {
                if samples[i].sd + 15 < samples[i - 1].sd {
                    drop = samples[i].at
                }
            }
        }
        guard let drop else { return nil }
        let reset = drop.addingTimeInterval(7 * 24 * 60 * 60)
        return reset > now ? reset : nil
    }

    struct Sample {
        let at: Date
        let fh: Int
        let sd: Int
    }

    private struct HistoryFile: Decodable {
        let samples: [Row]
    }

    private struct Row: Decodable {
        let t: Double
        let u: Usage?
    }

    private struct Usage: Decodable {
        let fh: Int?
        let sd: Int?
    }
}

/// Re-reads Claude when Desktop writes a new sample, the same file watch the
/// Windows app runs against `%APPDATA%\Claude\plan-usage-history.json`.
@MainActor
final class ClaudeDesktopHistoryMonitor {
    var onChange: (() -> Void)?

    private var source: DispatchSourceFileSystemObject?
    private var descriptor: CInt = -1
    private var debounce: DispatchWorkItem?
    private var lastStamp: Date?

    func start() {
        stop()
        lastStamp = ClaudeDesktop.historyStamp()
        let directory = ClaudeDesktop.supportDirectory
        guard FileManager.default.fileExists(atPath: directory.path) else { return }

        descriptor = open(directory.path, O_EVTONLY)
        guard descriptor >= 0 else { return }

        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: descriptor,
            eventMask: [.write, .extend, .attrib, .delete, .rename],
            queue: .main
        )
        source.setEventHandler { [weak self] in
            MainActor.assumeIsolated { self?.scheduleNotify() }
        }
        source.setCancelHandler { [descriptor] in
            if descriptor >= 0 { close(descriptor) }
        }
        source.resume()
        self.source = source
    }

    func stop() {
        debounce?.cancel()
        debounce = nil
        source?.cancel()
        source = nil
        lastStamp = nil
    }

    private func scheduleNotify() {
        debounce?.cancel()
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated { self?.notifyIfHistoryMoved() }
        }
        debounce = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4, execute: work)
    }

    private func notifyIfHistoryMoved() {
        let stamp = ClaudeDesktop.historyStamp()
        guard stamp != lastStamp else { return }
        lastStamp = stamp
        onChange?()
    }
}
