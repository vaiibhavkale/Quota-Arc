import Foundation

/// What one release changed, in the app's own words.
struct ReleaseNote: Equatable {
    /// Matched against `CFBundleShortVersionString`, so it has to be exactly
    /// the string `MARKETING_VERSION` is set to.
    let version: String
    /// One line under the title. What this release is *about*.
    let headline: String
    let changes: [Change]

    /// A title carries the change; the detail is optional, so a small fix can
    /// be a single line rather than a line padded out to match its neighbours.
    struct Change: Equatable {
        let title: String
        let detail: String

        init(title: String, detail: String = "") {
            self.title = title
            self.detail = detail
        }
    }
}

/// The release history the app ships with.
///
/// Written here rather than fetched from the appcast: it has to be there on a
/// first launch with no network, and it belongs to the build it describes.
/// Bumping `MARKETING_VERSION` without adding an entry is caught by
/// `testTheCurrentVersionHasANote`.
enum ReleaseNotes {
    static let all: [ReleaseNote] = [
        ReleaseNote(
            version: "1.6.1",
            headline: "Windows, and a Mac disk image on every GitHub Release.",
            changes: [
                ReleaseNote.Change(
                    title: "Quota Arc on Windows",
                    detail: "The same live usage notch as on the Mac, as an "
                          + "installer and a portable build."
                ),
                ReleaseNote.Change(
                    title: "A Mac disk image on GitHub Releases",
                    detail: "Every push to main publishes QuotaArc.dmg beside "
                          + "the Windows builds."
                ),
                ReleaseNote.Change(
                    title: "Settings survive the new name",
                    detail: "Connection choices and notch settings copy across "
                          + "from an earlier install on first launch."
                ),
            ]
        ),
        ReleaseNote(
            version: "1.6.0",
            headline: "Reorder the rings, pick a display, and get told when a limit is close.",
            changes: [
                ReleaseNote.Change(
                    title: "Drag to reorder the rings",
                    detail: "Settings splits into Connected and Not connected; "
                          + "drag a connected row by its handle to change the "
                          + "order the notch draws them in."
                ),
                ReleaseNote.Change(
                    title: "Pin the notch to one display, or show it on every one",
                    detail: "A Displays picker in Appearance offers the main "
                          + "display or all of them; a second picker pins a "
                          + "single notch to a named screen."
                ),
                ReleaseNote.Change(
                    title: "A ring says when it crosses 80% and 100%",
                    detail: "A system notification once per crossing, muted "
                          + "per provider from its own settings row."
                ),
                ReleaseNote.Change(
                    title: "GitHub Copilot is a new ring",
                    detail: "Reads GitHub's Copilot quota endpoint using the "
                          + "GitHub CLI session already on the Mac."
                ),
                ReleaseNote.Change(
                    title: "Say when a session ends",
                    detail: "The notch opens itself for a few seconds and "
                          + "sounds a chime when an agent stops working or "
                          + "starts waiting on you; a click jumps to it."
                ),
                ReleaseNote.Change(
                    title: "⌥-drag the pill along its edge",
                    detail: "Nudge it clear of another menu-bar app anchored "
                          + "to the same spot; remembered per edge."
                ),
                ReleaseNote.Change(
                    title: "Choose an accent colour",
                    detail: "The device accent by default, or a fixed colour "
                          + "for the ring's positive state — the amber and "
                          + "red warning colours stay fixed regardless."
                ),
                ReleaseNote.Change(
                    title: "A countdown instead of a reset date",
                    detail: "Appearance's Reset time picker can show \"Resets "
                          + "in 3h 20m\" instead of a date and time."
                ),
                ReleaseNote.Change(
                    title: "Read Cursor from cursor-agent, and enterprise plans correctly",
                    detail: "A CLI-only Cursor login now gets a ring, and "
                          + "enterprise/team plans read their real usage "
                          + "instead of reporting nothing to meter."
                ),
                ReleaseNote.Change(
                    title: "Fewer keychain prompts for Claude and Antigravity",
                    detail: "Claude reads its own CLI's /usage first, "
                          + "touching the keychain only as a fallback; "
                          + "Antigravity's language server is asked before it."
                ),
                ReleaseNote.Change(
                    title: "Sub-1% usage no longer reads as 0%",
                    detail: "A reading under one percent shows a tenth "
                          + "(\"<0.1%\") instead of rounding to nothing."
                ),
                ReleaseNote.Change(
                    title: "Contributors can build without Xcode signing",
                    detail: "make build and make test sign themselves "
                          + "automatically when the maintainer's certificate "
                          + "isn't present, and CI now runs the suite on "
                          + "every push and pull request."
                )
            ]
        ),
        ReleaseNote(
            version: "1.5.0",
            headline: "Two more providers, and a live account plan that was silently dropped.",
            changes: [
                ReleaseNote.Change(
                    title: "Grok is a new ring",
                    detail: "SuperGrok's weekly Grok Build allowance, read from "
                          + "the same billing endpoint the CLI uses, with the "
                          + "session in ~/.grok/auth.json."
                ),
                ReleaseNote.Change(
                    title: "OpenCode's Go plan is a new ring",
                    detail: "Reads the Go plan's official usage endpoint with "
                          + "the key OpenCode itself stores on sign-in — no "
                          + "second sign-in."
                ),
                ReleaseNote.Change(
                    title: "A real Codex account went unmetered",
                    detail: "Codex's live reading only recognised a 5-hour and "
                          + "a 7-day window. A free-plan account's real limit "
                          + "was a 30-day one, which fell through unnoticed and "
                          + "showed as nothing metered on an account that was "
                          + "genuinely tracked."
                ),
                ReleaseNote.Change(
                    title: "Switching a provider off now really stops it",
                    detail: "Opening Settings could still read a switched-off "
                          + "provider's account, and a reply already in flight "
                          + "could restore a reading you had just asked it to "
                          + "forget."
                ),
                ReleaseNote.Change(
                    title: "Contributors can build without a certificate",
                    detail: "make build and make test now sign themselves "
                          + "automatically when the maintainer's Developer ID "
                          + "isn't present — no Apple account needed to work "
                          + "on this."
                )
            ]
        ),
        ReleaseNote(
            version: "1.4.1",
            headline: "Waking from sleep no longer erases a reading.",
            changes: [
                ReleaseNote.Change(
                    title: "A ring survives waking your Mac",
                    detail: "A brief window right after sleep, where macOS "
                          + "won't allow a keychain prompt yet, was mistaken "
                          + "for being signed out — which erased the reading "
                          + "and left \"waiting for the first reading\" on "
                          + "screen. It now ages the number instead of "
                          + "throwing it away, and picks back up on its own."
                )
            ]
        ),
        ReleaseNote(
            version: "1.4.0",
            headline: "Two more accounts, four community fixes, and honest duplicates.",
            changes: [
                ReleaseNote.Change(
                    title: "Multiple Claude Code accounts",
                    detail: "Keep a work login apart with CLAUDE_CONFIG_DIR? It "
                          + "now gets its own ring, its own limits, and its own "
                          + "row in Settings, beside your personal one."
                ),
                ReleaseNote.Change(
                    title: "GLM added",
                    detail: "Z.ai's Coding Plan reads live now too, with a key "
                          + "borrowed from whichever tool already holds one."
                ),
                ReleaseNote.Change(
                    title: "A stuck Claude ring recovers on its own",
                    detail: "One momentary failure — the Mac waking from sleep, "
                          + "most often — used to lock the ring until the app "
                          + "restarted. It now clears itself on the next check."
                ),
                ReleaseNote.Change(
                    title: "Cursor sessions stop reporting work that already ended",
                    detail: "A crashed or abandoned chat could read as \"still "
                          + "working\" for a day or more. It now notices when "
                          + "the writing has actually stopped."
                ),
                ReleaseNote.Change(
                    title: "A months-old duplicate can no longer win",
                    detail: "Claude Code files a new keychain entry on every "
                          + "token rotation. An account signed in for a while "
                          + "could pick an old, expired one at random and show "
                          + "\"waiting for the first reading\" forever."
                ),
                ReleaseNote.Change(
                    title: "A stray click no longer pins the notch open",
                    detail: "Clicking near the screen edge before the notch had "
                          + "even opened could leave it stuck open with nothing "
                          + "on screen explaining why."
                )
            ]
        ),
        ReleaseNote(
            version: "1.3.0",
            headline: "Codex reads live, and Always show stays on.",
            changes: [
                ReleaseNote.Change(
                    title: "Codex is read live instead of from a log",
                    detail: "The figure came from a file Codex writes during a "
                          + "turn, so it was as old as the last time you used "
                          + "it — three days stale in one case. Quota Arc now "
                          + "asks Codex itself, and matches its own panel."
                ),
                ReleaseNote.Change(
                    title: "The Codex ring notices the desktop app",
                    detail: "It only ever watched the files the CLI and the VS "
                          + "Code extension write, so work done in the desktop "
                          + "app never made it spin."
                ),
                ReleaseNote.Change(
                    title: "Always show no longer turns itself off",
                    detail: "Clicking the notch toggled the same flag the "
                          + "setting used, so a stray click quietly put it back "
                          + "to showing on hover."
                ),
                ReleaseNote.Change(
                    title: "Far fewer keychain prompts",
                    detail: "Once a token expired, every check went back to the "
                          + "keychain — a prompt a minute. It now reads the "
                          + "secret only when the owning app has changed it, and "
                          + "never retries a refusal on a timer."
                ),
                ReleaseNote.Change(
                    title: "A paused limit is shown as paused",
                    detail: "Some limits are reached while the headline still "
                          + "shows room. The ring reads as spent and says when "
                          + "it lifts."
                ),
                ReleaseNote.Change(
                    title: "Long messages are no longer cut off",
                    detail: "A tooltip with something to explain reserved one "
                          + "line for it however much it said."
                )
            ]
        ),
        ReleaseNote(
            version: "1.2.0",
            headline: "Every session, and a tooltip that fits on the screen.",
            changes: [
                ReleaseNote.Change(
                    title: "Tooltips are no longer cut off",
                    detail: "A card is centred on the ring it belongs to, so the "
                          + "first and last providers threw half of it past the "
                          + "end of the panel — and what fell off was the title. "
                          + "The panel now keeps room for it."
                ),
                ReleaseNote.Change(
                    title: "As many sessions as your screen can hold",
                    detail: "The list was capped at four whatever you were "
                          + "running on. It is now solved for the display: ten on "
                          + "a large one, and \"and N more\" only when there is "
                          + "genuinely no room for the rest."
                ),
                ReleaseNote.Change(
                    title: "The ones that need you come first",
                    detail: "Waiting, then busy, then idle — so if anything is "
                          + "summarised away, it is what matters least."
                )
            ]
        ),
        ReleaseNote(
            version: "1.1.0",
            headline: "Antigravity's real numbers, and a switch that stays off.",
            changes: [
                ReleaseNote.Change(
                    title: "Antigravity shows its actual quota",
                    detail: "Google will not answer Quota Arc directly, so it asks "
                          + "Antigravity's own language server instead — the same "
                          + "place Antigravity's usage panel gets its figure."
                ),
                ReleaseNote.Change(
                    title: "Usage reads both ways",
                    detail: "\"12% used · 88% left\", so a reading lines up with "
                          + "whichever end your vendor happens to show."
                ),
                ReleaseNote.Change(
                    title: "A way back from a declined keychain prompt",
                    detail: "Declining no longer looks like being signed out, and "
                          + "Allow access… asks macOS again."
                ),
                ReleaseNote.Change(
                    title: "Switching a provider off now sticks",
                    detail: "It stopped being read but its last reading was kept, "
                          + "so the ring came back at the next launch."
                ),
                ReleaseNote.Change(
                    title: "Distant resets show a date",
                    detail: "A limit renewing in four weeks said \"Mon\", which read "
                          + "as this Monday. It says \"28 Sep\"."
                )
            ]
        ),
        ReleaseNote(
            version: "1.0.0",
            headline: "The first release.",
            changes: [
                ReleaseNote.Change(
                    title: "Put the notch anywhere",
                    detail: "Right, left, top or bottom. It keeps clear of the Dock "
                          + "and the menu bar, and follows when the Dock moves."
                ),
                ReleaseNote.Change(
                    title: "It joins your Mac's own notch",
                    detail: "On the top edge it takes the hardware's shape, so the "
                          + "two read as one rather than as a bar parked underneath."
                ),
                ReleaseNote.Change(
                    title: "Claude, Cursor, Codex and Gemini",
                    detail: "Each read from the tool already signed in on this Mac. "
                          + "Quota Arc never asks for a password."
                ),
                ReleaseNote.Change(
                    title: "Choose where Quota Arc appears",
                    detail: "In the Dock, in the menu bar, or nowhere at all."
                )
            ]
        )
    ]

    static func note(for version: String) -> ReleaseNote? {
        all.first { $0.version == version }
    }

    /// The note worth showing on this launch, if there is one.
    ///
    /// `notes` is a parameter so the rule can be tested against a fixed history
    /// rather than against whatever the app happens to ship this week.
    static func unseen(in version: String,
                       lastSeen: String?,
                       notes: [ReleaseNote] = ReleaseNotes.all) -> ReleaseNote? {
        guard lastSeen != version else { return nil }
        return notes.first { $0.version == version }
    }
}
