import AppKit
import Combine
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var notchFleet: NotchFleet?
    private var store: UsageStore?
    private var monitors: [String: any AgentActivityMonitor] = [:]
    private var preferences: Preferences?
    private var settings: SettingsWindowController?
    private var whatsNew: WhatsNewWindowController?
    /// Held for the life of the app: releasing it stops the scheduled checks.
    private var updater: Updater?
    private var thresholdNotifier: ThresholdNotifier?
    private var statusItem: StatusItemController?
    private var cancellables = Set<AnyCancellable>()
    /// Turns the monitors' running commentary into the one event worth
    /// interrupting for: an agent that has just stopped working.
    private var completions = SessionCompletionWatcher()
    /// First activation is launch, which already fetched. Later ones are the
    /// user coming back from Cursor or Claude, which is when the cached token
    /// is the one that is wrong.
    private var hasBecomeActive = false

    /// The unit bundle is hosted by this app, so `xcodebuild test` launches it
    /// for real. Without this guard every test run put a live request on the
    /// usage endpoint — which is both wrong on its own terms and, on an endpoint
    /// that rate-limits, actively harmful.
    private var isRunningTests: Bool {
        ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] != nil
            || NSClassFromString("XCTestCase") != nil
    }

    /// Every Claude Code configuration directory on this Mac — `~/.claude` and
    /// any `~/.claude-<slug>` — found once at launch. Each gets a usage
    /// provider and a session monitor of its own, keyed by the same id, so a
    /// work login's sessions spin the work ring and nobody else's.
    private let claudeProfiles = ClaudeProfile.discover()

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Set here, not in the Info.plist: this call is applied at launch and
        // overrides `LSUIElement` either way. Removing the plist key alone left
        // the app registered as a UIElement with no Dock tile, which looked
        // exactly like the icon having failed to install. The user's choice
        // replaces this a moment later, once preferences exist.
        NSApp.setActivationPolicy(.regular)
        guard !isRunningTests else { return }

        // Before Preferences reads anything, or the first launch flag and
        // every choice would be read from an empty domain.
        Preferences.migrateFromPreviousName()
        let preferences = Preferences()
        self.preferences = preferences

        // One notch per display: the fleet owns a controller for each screen
        // the scope asks for and fans every reading out to all of them. The
        // stored edge goes in up front, before any panel is ever put up — the
        // sink below delivers on the next run loop turn, by which time the
        // notch would already have flashed on the default edge.
        let fleet = NotchFleet(scope: preferences.notchScope, edge: preferences.notchEdge)
        self.notchFleet = fleet

        // `QUOTAARC_DEMO=1` puts the design frame's three providers on screen
        // with its numbers, for screenshots and for eyeballing the layout.
        if ProcessInfo.processInfo.environment["QUOTAARC_DEMO"] == "1" {
            fleet.setSnapshots(Fixtures.snapshots())
        } else {
            // Nothing needs a browser session at the moment. `WebSessionProvider`
            // and `Sites.perplexity` are kept: they are the working pattern for a
            // site behind bot management, and re-registering is one line.
            let webProviders: [WebSessionProvider] = []
            fleet.signInItems = webProviders.map { provider in
                (title: "Sign in to \(provider.displayName)…",
                 action: { [weak provider] in provider?.presentSignIn() })
            }

            // Cursor reads the editor's session, or cursor-agent's if the
            // editor is missing — never a browser one: signing into
            // cursor.com separately created a second, empty account.
            //
            // Built *after* preferences and told what is switched off, so the
            // very first list it draws already excludes them. Constructed first,
            // it drew every provider from the archive and only dropped the
            // switched-off ones once the binding below delivered.
            Log.usage.info("claude profiles: \(self.claudeProfiles.map(\.displayPath).joined(separator: ", "), privacy: .public)")
            let store = UsageStore(
                providers: claudeProfiles.map { ClaudeOAuthProvider(profile: $0) }
                    + [CursorLocalProvider(), CodexLocalProvider(), AntigravityProvider(),
                       GLMProvider(), GrokLocalProvider(), OpenCodeProvider(),
                       GitHubCopilotProvider(),
                       // A closure, not the value: the provider is an actor and
                       // re-reads the budget on every fetch, so a ceiling typed
                       // into Settings applies without a restart.
                       GeminiAPIProvider(budget: {
                           Preferences.storedGeminiAPIMonthlyTokenBudget()
                       })]
                    + webProviders,
                disconnected: preferences.disconnectedProviders,
                // Passed at construction, not left to the sink below, for the
                // same reason `disconnected` is: the sink delivers a run loop
                // turn later, so without this every launch draws the built-in
                // order for a frame and then visibly shuffles.
                order: preferences.providerOrder
            )

            let updater = Updater()
            self.updater = updater

            let settings = SettingsWindowController(
                preferences: preferences,
                // A closure so the sheet re-reads accounts each time it comes
                // forward; a snapshot here is what made a switched account keep
                // showing the old address until the app restarted.
                providers: { [weak store] in store?.providerSummaries ?? [] },
                updater: updater,
                signOut: { [weak store] in store?.signOut(providerID: $0) },
                signIn: { [weak store] in store?.signIn(providerID: $0) ?? false },
                switchAccount: { [weak store] in
                    store?.openAccountSource(providerID: $0) ?? false
                },
                retry: { [weak store] in store?.reauthorize(providerID: $0) },
                refreshAll: { [weak store] in store?.refreshAllTokens() }
            )
            fleet.onOpenSettings = { [weak settings] in settings?.show() }
            self.settings = settings

            // What changed, once per version — including on a fresh install,
            // where it is the introduction.
            let whatsNew = WhatsNewWindowController(
                preferences: preferences, version: updater.currentVersion
            )
            self.whatsNew = whatsNew

            // An agent app has no dock icon and no window: installed and
            // launched, it shows four empty rings on a screen edge and no
            // reason to look at them. Once, on the very first run, it opens the
            // one place that explains what to connect.
            //
            // Sequenced behind What's New rather than beside it: two windows
            // arriving together is one to dismiss before you can read either.
            let introduce = { [weak settings] in
                guard preferences.isFirstLaunch else { return }
                settings?.show()
            }
            whatsNew.onDismiss = introduce
            if !whatsNew.showIfNeeded() {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6, execute: introduce)
            }

            let statusItem = StatusItemController { [weak settings] in settings?.show() }
            self.statusItem = statusItem
            statusItem.onRefreshProvider = { [weak store] id in store?.refresh(providerID: id) }
            statusItem.onRefreshAll = { [weak store] in store?.refreshAllTokens() }

            preferences.$appPresence
                .receive(on: RunLoop.main)
                .sink { presence in
                    NSApp.setActivationPolicy(presence.activationPolicy)
                    if presence.wantsStatusItem { statusItem.show() } else { statusItem.hide() }
                }
                .store(in: &cancellables)

            preferences.$notchVisibility
                .receive(on: RunLoop.main)
                .sink { [weak fleet] in fleet?.apply($0) }
                .store(in: &cancellables)

            preferences.$notchEdge
                .receive(on: RunLoop.main)
                .sink { [weak fleet, weak preferences] edge in
                    // Read before `apply(edge:)` moves the panel, so the new
                    // edge's own remembered nudge is what it lands at rather
                    // than the old edge's carried over onto it.
                    fleet?.apply(alongOffset: preferences?.offset(for: edge) ?? 0)
                    fleet?.apply(edge: edge)
                }
                .store(in: &cancellables)

            preferences.$notchScope
                .receive(on: RunLoop.main)
                .sink { [weak fleet] in fleet?.apply(scope: $0) }
                .store(in: &cancellables)

            preferences.$displayPreference
                .receive(on: RunLoop.main)
                .sink { [weak fleet] in fleet?.apply(displayPreference: $0) }
                .store(in: &cancellables)

            fleet.onReposition = { [weak preferences] offset in
                preferences?.setOffset(offset, for: preferences?.notchEdge ?? .right)
            }

            preferences.$resetTimeFormat
                .receive(on: RunLoop.main)
                .sink { [weak fleet] in fleet?.apply(resetTimeFormat: $0) }
                .store(in: &cancellables)

            preferences.$accentColor
                .receive(on: RunLoop.main)
                .sink { [weak fleet] in fleet?.apply(accentColor: $0) }
                .store(in: &cancellables)

            preferences.$disconnectedProviders
                .receive(on: RunLoop.main)
                .sink { [weak store] in store?.disconnected = $0 }
                .store(in: &cancellables)

            preferences.$providerOrder
                .receive(on: RunLoop.main)
                .sink { [weak store] in store?.order = $0 }
                .store(in: &cancellables)

            // Redraw the Gemini API ring against the new ceiling.
            //
            // `dropFirst` because `@Published` publishes the value it is given
            // at init, and a refresh there would race the store's first poll.
            // `receive(on:)` because `@Published` emits in `willSet` — the hop
            // to the next run loop pass is what lets the `didSet` persist the
            // number before the provider's closure goes looking for it.
            preferences.$geminiAPIMonthlyTokenBudget
                .dropFirst()
                .receive(on: RunLoop.main)
                .sink { [weak store] _ in store?.refresh(providerID: "gemini-api") }
                .store(in: &cancellables)

            // Limit crossings become notifications here rather than inside
            // the store: the store fetches, the notifier decides what is
            // worth interrupting someone for, and neither needs to know the
            // other.
            let notifier = ThresholdNotifier(
                isMuted: { [weak preferences] in preferences?.isMutedAlerts(for: $0) ?? false },
                deliver: { ThresholdAlerts.deliver($0) }
            )
            self.thresholdNotifier = notifier

            store.$snapshots
                .receive(on: RunLoop.main)
                .sink { [weak fleet, weak statusItem] snapshots in
                    fleet?.setSnapshots(snapshots)
                    statusItem?.snapshots = snapshots
                    notifier.observe(snapshots)
                }
                .store(in: &cancellables)
            store.start()
            fleet.onRefresh = { [weak store] in store?.refreshNow() }
            fleet.onRefreshProvider = { [weak store] id in store?.refresh(providerID: id) }
            fleet.onUnfold = { [weak store] in store?.refreshIfStale() }
            store.$refreshing
                .receive(on: RunLoop.main)
                .sink { [weak fleet] ids in fleet?.setRefreshing(ids) }
                .store(in: &cancellables)

            // QUOTAARC_DISCOVER=<url> loads that page in the signed-in WebView
            // and logs the API calls it makes — for finding an undocumented
            // endpoint by watching the site rather than guessing at path names.
            if let target = ProcessInfo.processInfo.environment["QUOTAARC_DISCOVER"],
               let url = URL(string: target),
               let provider = webProviders.first(where: { url.host?.contains($0.id) == true })
                   ?? webProviders.first {
                Task {
                    let calls = await provider.recordCalls(on: url)
                    Log.usage.notice("discovered: \(calls.joined(separator: "  "), privacy: .public)")
                }
            }
            self.store = store
        }

        // What each agent is doing right now, so the notch can say whether it is
        // still working without you switching to it.
        var monitors: [String: any AgentActivityMonitor] = [
            "cursor": CursorActivityMonitor(),
            "codex": CodexActivityMonitor(),
            "gemini": AntigravityActivityMonitor(),
            "grok": GrokActivityMonitor(),
            "gemini-api": GeminiCLIActivityMonitor()
        ]
        for profile in claudeProfiles {
            monitors[profile.id] = ClaudeSessionMonitor(directory: profile.sessionsDirectory)
        }
        for (id, monitor) in monitors {
            monitor.sessionsPublisher
                .receive(on: RunLoop.main)
                .sink { [weak self, weak fleet] live in
                    guard let fleet else { return }
                    fleet.setSessions(providerID: id, sessions: live)
                    // The publisher delivers on the main run loop, but the
                    // closure itself is nonisolated — the same assertion the
                    // notch controller's timers make.
                    MainActor.assumeIsolated { self?.announceCompletions(sessions: fleet.sessions) }
                }
                .store(in: &cancellables)
            monitor.start()
        }
        // Poll usage hard only while something is actually running.
        store?.isBusy = { monitors.values.contains { m in m.sessions.contains { $0.state == .busy } } }
        self.monitors = monitors

        // Applied last, right before the panel goes up: every one of these
        // calls a `NotchFleet.apply(...)` that can trigger `reconcile()` on
        // its own — `displayPreference` always does, being how the very
        // first controller gets created — and `reconcile()` copies the
        // fleet's callbacks (`onOpenSettings`, `onRefreshProvider`, ...) into
        // that controller at creation time, not through a live reference.
        // Calling any of these earlier, before those callbacks were set
        // above, silently built the one controller this app ever has with
        // every action wired to nothing: the panel still opened and rings
        // still drew, so there was nothing to notice except every click
        // doing exactly nothing. `fleet.show()`'s own reconcile only ever
        // repositions an existing controller — it does not re-copy them —
        // so this has to be the very last thing that can create one.
        fleet.apply(displayPreference: preferences.displayPreference)
        fleet.apply(alongOffset: preferences.offset(for: preferences.notchEdge))
        fleet.apply(resetTimeFormat: preferences.resetTimeFormat)
        fleet.apply(accentColor: preferences.accentColor)
        fleet.show()
    }

    /// Open the notch, and make a noise, when something has just finished.
    ///
    /// The watcher is fed on every publication whether or not anything is
    /// switched on, because it is a difference engine: skipping a reading would
    /// leave it comparing against a state two changes old, and the *next*
    /// transition it reported would be one that never happened.
    ///
    /// Several sessions can land in the same reading — one turn ending often
    /// unblocks another — and that gets one peek and one chime rather than a
    /// chord. The newest is the one offered, since it is the one whose window
    /// you were most recently in.
    @MainActor
    private func announceCompletions(sessions: [String: [AgentSession]]) {
        let events = completions.absorb(sessions)
        guard let event = events.first, let preferences, let fleet = notchFleet else { return }
        Log.usage.info("session \(event.session.name, privacy: .public) \(String(describing: event.reason), privacy: .public)")

        if preferences.sessionEndSound {
            SessionChime.play(event.reason == .blocked
                              ? preferences.sessionBlockedSoundName
                              : preferences.sessionEndSoundName)
        }
        guard preferences.announceSessionEnd else { return }
        fleet.peek(for: preferences.peekDuration.seconds,
                   focusing: event.session.processID)
    }

    /// Closing the settings window must not take the app with it.
    ///
    /// The default for a Dock app is to quit once its last window closes, which
    /// here would kill the notch — the part that is actually the product —
    /// every time someone shut the settings they had just opened.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    /// The way back in when the notch is hidden.
    ///
    /// With no dock icon, no menu bar item and no notch on screen, there is
    /// otherwise nothing left to click — choosing Hide would be a one-way door.
    /// Opening the app again while it is already running lands here, so
    /// clicking the Dock tile re-reads every connected tool - Cursor, Claude,
    /// Codex, Antigravity - and reopens settings. Switching account happens in
    /// those apps, and a cached token would keep showing the previous one.
    func applicationShouldHandleReopen(_ sender: NSApplication,
                                       hasVisibleWindows: Bool) -> Bool {
        settings?.show()
        return true
    }

    /// Coming back to the app - Dock click or Cmd-Tab - is the moment the
    /// numbers on screen are about to be looked at, and the moment the user
    /// just left Cursor or Claude. Skip the first activation: launch already
    /// fetched.
    func applicationDidBecomeActive(_ notification: Notification) {
        guard !isRunningTests else { return }
        if !hasBecomeActive {
            hasBecomeActive = true
            return
        }
        store?.refreshAllTokens(minSeconds: 5)
    }

    func applicationWillTerminate(_ notification: Notification) {
        store?.stop()
        monitors.values.forEach { $0.stop() }
        notchFleet?.stop()
    }
}
