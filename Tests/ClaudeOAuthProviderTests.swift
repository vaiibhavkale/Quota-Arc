import XCTest
@testable import QuotaArc

/// The token path of `ClaudeOAuthProvider`.
///
/// It had no tests at all — only the pure helpers (`backoff`, `retryAfter`) were
/// covered — which is how a back-off that re-stamped itself on every failed tick
/// shipped and locked the provider until the app was restarted.
///
/// Every assertion here is about one question: **after a failure, does the next
/// tick actually go and ask again?** Hence the counters. Asserting on the returned
/// error is not enough — the broken version returned exactly the right error while
/// never touching the keychain or the network.
final class ClaudeOAuthProviderTests: XCTestCase {

    override func tearDown() {
        StubEndpoint.reset([])
        super.tearDown()
    }

    /// A 401 must not stop the next tick from trying.
    ///
    /// The endpoint rejects the token and then starts answering again — a token
    /// rotated behind the app's back. This is the manual repro (a local server
    /// switched from 401 to 200) reduced to a test.
    func testA401DoesNotStopTheNextTickFromTrying() async throws {
        StubEndpoint.reset([
            .init(status: 401),                       // the tick's first attempt
            .init(status: 401),                       // its one retry on unauthorized
            .init(status: 200, body: Self.usagePayload)
        ])
        let source = CredentialSource(readable: true)
        let provider = makeProvider(source: source)

        await assertNeedsAuth(from: provider)
        XCTAssertEqual(StubEndpoint.requestCount, 2, "the retry on 401 did not happen")

        let snapshot = try await provider.fetchSnapshot()

        XCTAssertEqual(StubEndpoint.requestCount, 3,
                       "the next tick never reached the endpoint")
        XCTAssertEqual(snapshot.status, .ok)
        XCTAssertEqual(snapshot.windows.first?.id, "session")
    }

    /// A keychain read that failed must not stop the next tick from reading again.
    ///
    /// This is what happened in the field: the Mac was in dark wake, the keychain
    /// answered `-25320` ("no UI possible"), and that fell through to `needsAuth`.
    /// The credential was readable again seconds later; the provider never looked.
    func testAKeychainFailureDoesNotStopTheNextTickFromReading() async throws {
        StubEndpoint.reset([.init(status: 200, body: Self.usagePayload)])
        let source = CredentialSource(readable: false)
        let provider = makeProvider(source: source)

        await assertNeedsAuth(from: provider)
        XCTAssertEqual(source.reads, 1)
        XCTAssertEqual(StubEndpoint.requestCount, 0,
                       "it went to the network without a token")

        source.makeReadable()   // the machine woke up

        let snapshot = try await provider.fetchSnapshot()

        XCTAssertEqual(source.reads, 2, "the next tick never went back to the keychain")
        XCTAssertEqual(snapshot.status, .ok)
    }

    /// Failing repeatedly must not become failing silently.
    ///
    /// The bug's signature was a request count frozen at two while the poll kept
    /// firing every 60 seconds. Three ticks against a rejecting endpoint have to
    /// produce three attempts, not one.
    func testItKeepsAskingWhileTheEndpointKeepsRejecting() async {
        StubEndpoint.reset(Array(repeating: .init(status: 401), count: 6))
        let provider = makeProvider(source: CredentialSource(readable: true))

        for _ in 0..<3 { await assertNeedsAuth(from: provider) }

        XCTAssertEqual(StubEndpoint.requestCount, 6,
                       "the provider stopped asking after the first failure")
    }

    // MARK: - Helpers

    private static let usagePayload = Data("""
    {"limits":[{"kind":"session","percent":42,"resets_at":"2099-01-01T00:00:00Z"}]}
    """.utf8)

    private func makeProvider(source: CredentialSource,
                              cli: ClaudeUsageCLI? = nil,
                              cliRefreshInterval: TimeInterval = 5 * 60,
                              desktop: ProviderSnapshot? = nil) -> ClaudeOAuthProvider {
        // A private defaults suite per test: the archive persists the 429 back-off
        // deadline, and a leaked one would silently skip fetches in the next test.
        let name = "ClaudeOAuthProviderTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: name)!
        defaults.removePersistentDomain(forName: name)

        // No CLI, because these are the token path's tests. Left to find one,
        // the provider would answer off `claude "/usage"` on a machine that has
        // Claude Code installed and off the endpoint on one that does not, and
        // every assertion below about retries and back-off would depend on the
        // developer's own setup rather than on the code.
        return ClaudeOAuthProvider(session: StubEndpoint.session(),
                                   archive: UsageArchive(defaults: defaults),
                                   loadCredentials: { try source.read() },
                                   cli: cli,
                                   cliRefreshInterval: cliRefreshInterval,
                                   loadDesktopSnapshot: { desktop })
    }

    // MARK: - The CLI path

    /// The point of the whole thing: when `claude "/usage"` answers, nothing
    /// asks macOS for a credential and nothing calls the endpoint.
    ///
    /// Counting is the only way to know. A provider that read the keychain and
    /// then threw the result away would return exactly the same snapshot, and
    /// the keychain prompt this exists to avoid would still have appeared.
    func testAWorkingCLIMeansNoKeychainReadAndNoRequest() async throws {
        StubEndpoint.reset([.init(status: 200, body: Self.usagePayload)])
        let source = CredentialSource(readable: true)
        let provider = makeProvider(source: source, cli: Self.cli(answering: Self.cliUsage))

        let snapshot = try await provider.fetchSnapshot()

        XCTAssertEqual(snapshot.windows.map(\.id), ["session", "weekly_all"])
        XCTAssertEqual(source.reads, 0, "the keychain was read even though the CLI answered")
        XCTAssertEqual(StubEndpoint.requestCount, 0, "the endpoint was called even though the CLI answered")
    }

    /// A CLI that cannot answer is a reason to ask the endpoint, never a reason
    /// to fail the refresh — otherwise installing Claude Code and signing out
    /// of it would take the ring down on a machine whose token is fine.
    func testAFailingCLIFallsBackToTheToken() async throws {
        StubEndpoint.reset([.init(status: 200, body: Self.usagePayload)])
        let source = CredentialSource(readable: true)
        let provider = makeProvider(source: source,
                                    cli: Self.cli(answering: "Please run /login first"))

        let snapshot = try await provider.fetchSnapshot()

        XCTAssertEqual(snapshot.windows.first?.id, "session")
        XCTAssertEqual(source.reads, 1, "the token path was not reached")
        XCTAssertEqual(StubEndpoint.requestCount, 1)
    }

    /// `UsageStore` polls every 60s while a session is busy, and each ask is a
    /// subprocess. The windows do not move enough in a minute to be worth one.
    func testTheCLIIsNotSpawnedOnEveryTick() async throws {
        let spawns = Counter()
        let provider = makeProvider(source: CredentialSource(readable: true),
                                    cli: Self.cli { spawns.increment(); return Self.cliUsage })

        _ = try await provider.fetchSnapshot()
        _ = try await provider.fetchSnapshot()
        _ = try await provider.fetchSnapshot()

        XCTAssertEqual(spawns.value, 1, "the CLI was spawned again inside its own interval")
    }

    /// And it is asked again once the interval has passed, or the ring would
    /// show one reading for the rest of the session.
    func testTheCLIIsAskedAgainOnceTheIntervalPasses() async throws {
        let spawns = Counter()
        let provider = makeProvider(source: CredentialSource(readable: true),
                                    cli: Self.cli { spawns.increment(); return Self.cliUsage },
                                    cliRefreshInterval: 0)

        _ = try await provider.fetchSnapshot()
        _ = try await provider.fetchSnapshot()

        XCTAssertEqual(spawns.value, 2)
    }

    /// Desktop's history file is what Windows already reads. An expired token
    /// must not leave the ring empty when those numbers are sitting on disk.
    func testDesktopHistoryFillsTheRingWhenTheTokenCannot() async throws {
        StubEndpoint.reset([.init(status: 401)])
        let source = CredentialSource(readable: true)
        let desktop = Self.desktopSnapshot(session: 0.23, weekly: 0.14)
        let provider = makeProvider(
            source: source,
            cli: Self.cli(answering: "Please run /login first"),
            desktop: desktop
        )

        let snapshot = try await provider.fetchSnapshot()

        XCTAssertEqual(snapshot.status, .ok)
        XCTAssertEqual(snapshot.windows.map(\.id), ["session", "weekly_all"])
        XCTAssertEqual(snapshot.windows[0].usedFraction!, 0.23, accuracy: 0.0001)
        XCTAssertEqual(source.reads, 0, "the keychain was asked even though Desktop had a reading")
        XCTAssertEqual(StubEndpoint.requestCount, 0, "the endpoint was called even though Desktop had a reading")
    }

    /// Spawning `claude /usage` takes up to 20s. Windows never waits that long
    /// when the history file is there, and neither should Mac.
    func testDesktopHistoryMeansTheCLIIsNotSpawned() async throws {
        let spawns = Counter()
        let provider = makeProvider(
            source: CredentialSource(readable: true),
            cli: Self.cli { spawns.increment(); return Self.cliUsage },
            desktop: Self.desktopSnapshot(session: 0.09, weekly: 0.11)
        )

        _ = try await provider.fetchSnapshot()

        XCTAssertEqual(spawns.value, 0)
    }

    private static func desktopSnapshot(session: Double, weekly: Double) -> ProviderSnapshot {
        ProviderSnapshot(
            id: "claude", displayName: "Claude", glyph: .claude,
            fidelity: .official, status: .ok,
            windows: [
                LimitWindow(id: "session", label: "Current session", usedFraction: session),
                LimitWindow(id: "weekly_all", label: "All models", usedFraction: weekly)
            ],
            headlineID: "session"
        )
    }

    private static let cliUsage = """
    Current session: 38% used · resets Sep 7 at 2:59pm (Asia/Jakarta)
    Current week (all models): 4% used · resets Sep 14 at 5:59am (Asia/Jakarta)
    """

    private static func cli(answering text: String) -> ClaudeUsageCLI {
        cli { text }
    }

    private static func cli(_ answer: @escaping @Sendable () -> String) -> ClaudeUsageCLI {
        // The path is never run — `output` is what the provider reaches.
        ClaudeUsageCLI(binary: URL(fileURLWithPath: "/nonexistent/claude")) { _ in answer() }
    }

    private func assertNeedsAuth(from provider: ClaudeOAuthProvider,
                                 file: StaticString = #filePath,
                                 line: UInt = #line) async {
        do {
            _ = try await provider.fetchSnapshot()
            XCTFail("expected needsAuth, got a snapshot", file: file, line: line)
        } catch UsageProviderError.needsAuth {
            // expected
        } catch {
            XCTFail("expected needsAuth, got \(error)", file: file, line: line)
        }
    }
}

/// How many times the CLI was actually asked. "Did it spawn again?" is the
/// question the throttle exists to answer, and only a count answers it.
private final class Counter: @unchecked Sendable {
    private let lock = NSLock()
    private var count = 0

    func increment() {
        lock.lock(); count += 1; lock.unlock()
    }

    var value: Int {
        lock.lock(); defer { lock.unlock() }
        return count
    }
}

/// Stands in for the keychain, and counts reads.
///
/// "Did it go back and ask?" is the whole question, and only a counter answers it.
private final class CredentialSource: @unchecked Sendable {
    private let lock = NSLock()
    private var readable: Bool
    private var readCount = 0

    init(readable: Bool) { self.readable = readable }

    var reads: Int {
        lock.lock(); defer { lock.unlock() }
        return readCount
    }

    func makeReadable() {
        lock.lock(); readable = true; lock.unlock()
    }

    func read() throws -> ClaudeCredentials {
        lock.lock()
        readCount += 1
        let allowed = readable
        lock.unlock()

        // The shape a dark-wake or not-found read takes by the time it leaves
        // `ClaudeCredentials.read()`.
        guard allowed else { throw UsageProviderError.needsAuth }
        return ClaudeCredentials(accessToken: "token",
                                 expiresAt: .distantFuture,
                                 subscriptionType: "max")
    }
}

/// Canned answers for the usage endpoint, and a count of how many requests
/// actually arrived. The repo had no URL stubbing, which is why nothing above
/// `retryAfter(from:)` was ever tested.
private final class StubEndpoint: URLProtocol {
    struct Answer {
        let status: Int
        var body: Data = Data()
    }

    private static let lock = NSLock()
    private static var queued: [Answer] = []
    private static var served = 0

    static func reset(_ answers: [Answer]) {
        lock.lock(); queued = answers; served = 0; lock.unlock()
    }

    static var requestCount: Int {
        lock.lock(); defer { lock.unlock() }
        return served
    }

    static func session() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubEndpoint.self]
        return URLSession(configuration: configuration)
    }

    private static func next() -> Answer {
        lock.lock(); defer { lock.unlock() }
        served += 1
        // Running dry is a test bug, and a 500 says so more clearly than a crash
        // inside URLSession's callback would.
        return queued.isEmpty ? Answer(status: 500) : queued.removeFirst()
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        let answer = Self.next()
        let response = HTTPURLResponse(url: request.url!,
                                       statusCode: answer.status,
                                       httpVersion: "HTTP/1.1",
                                       headerFields: ["Content-Type": "application/json"])!
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: answer.body)
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}
}
