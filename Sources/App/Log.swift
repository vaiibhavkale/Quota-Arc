import os

/// An agent app has no window to print into, so anything worth diagnosing has
/// to go somewhere you can read it:
///
///     log stream --predicate 'subsystem == "com.vaibhavkale.quotaarc"' --level debug
enum Log {
    static let usage = Logger(subsystem: "com.vaibhavkale.quotaarc", category: "usage")
    static let sessions = Logger(subsystem: "com.vaibhavkale.quotaarc", category: "sessions")
}
