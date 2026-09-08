import XCTest
@testable import QuotaArc

/// Claude Desktop's `plan-usage-history.json` is what the Windows app already
/// reads. These pin the same percentages the settings page shows.
final class ClaudeDesktopHistoryTests: XCTestCase {
    func testItReadsTheSamePercentsTheSettingsPageShows() {
        let now = date(2026, 9, 8, 14, 48)
        let start = now.addingTimeInterval(-8 * 60)
        let json = """
        {
          "samples": [
            { "t": \(ms(now.addingTimeInterval(-20 * 60))), "u": { "fh": 0, "sd": 10 } },
            { "t": \(ms(start)), "u": { "fh": 9, "sd": 11 } }
          ]
        }
        """

        let windows = ClaudeDesktop.windowsFromHistory(json: json, now: now)
        XCTAssertEqual(windows?.map(\.id), ["session", "weekly_all"])
        XCTAssertEqual(windows?[0].label, "Current session")
        XCTAssertEqual(windows?[0].usedFraction ?? -1, 0.09, accuracy: 0.0001)
        XCTAssertEqual(windows?[1].label, "All models")
        XCTAssertEqual(windows?[1].usedFraction ?? -1, 0.11, accuracy: 0.0001)
        XCTAssertNil(windows?[1].resetsAt)
        XCTAssertEqual(windows?[0].resetsAt, start.addingTimeInterval(5 * 60 * 60))
    }

    /// A five-hour start that has already expired must not be clamped to now.
    /// That is what printed "Resetting..." on Claude with no clock next to it.
    func testAStaleFiveHourStartIsNotClampedToNow() {
        let now = date(2026, 9, 8, 14, 48)
        let oldStart = now.addingTimeInterval(-17 * 60 * 60)
        let newStart = now.addingTimeInterval(-90 * 60)
        let json = """
        {
          "samples": [
            { "t": \(ms(oldStart.addingTimeInterval(-60))), "u": { "fh": 0, "sd": 80 } },
            { "t": \(ms(oldStart)), "u": { "fh": 57, "sd": 80 } },
            { "t": \(ms(oldStart.addingTimeInterval(6 * 60 * 60))), "u": { "fh": 57, "sd": 80 } },
            { "t": \(ms(newStart)), "u": { "fh": 23, "sd": 14 } }
          ]
        }
        """

        let windows = ClaudeDesktop.windowsFromHistory(json: json, now: now)
        let reset = windows?[0].resetsAt
        XCTAssertEqual(reset, newStart.addingTimeInterval(5 * 60 * 60))
        XCTAssertGreaterThan(reset ?? .distantPast, now)
        let copy = ResetCopy.text(for: reset!, now: now)
        XCTAssertFalse(copy.hasPrefix("Resetting"), copy)
        XCTAssertTrue(copy.hasPrefix("Resets"), copy)
    }

    func testWeeklyResetIsSevenDaysAfterTheDrop() {
        let now = date(2026, 9, 8, 14, 48)
        let drop = now.addingTimeInterval(-2 * 24 * 60 * 60)
        let json = """
        {
          "samples": [
            { "t": \(ms(drop.addingTimeInterval(-60))), "u": { "fh": 10, "sd": 100 } },
            { "t": \(ms(drop)), "u": { "fh": 0, "sd": 5 } },
            { "t": \(ms(now.addingTimeInterval(-60))), "u": { "fh": 23, "sd": 14 } }
          ]
        }
        """

        let windows = ClaudeDesktop.windowsFromHistory(json: json, now: now)
        XCTAssertEqual(windows?[1].resetsAt, drop.addingTimeInterval(7 * 24 * 60 * 60))
    }

    func testIdleFiveHourUsageOmitsAResetTime() {
        let now = date(2026, 9, 8, 14, 48)
        let json = """
        {
          "samples": [
            { "t": \(ms(now.addingTimeInterval(-6 * 60 * 60))), "u": { "fh": 40, "sd": 10 } },
            { "t": \(ms(now.addingTimeInterval(-60))), "u": { "fh": 0, "sd": 10 } }
          ]
        }
        """

        let windows = ClaudeDesktop.windowsFromHistory(json: json, now: now)
        XCTAssertNil(windows?[0].resetsAt)
    }

    func testItUsesTheLatestSampleNotAnOlderSpike() {
        let now = date(2026, 9, 8, 14, 48)
        let json = """
        {
          "samples": [
            { "t": \(ms(now.addingTimeInterval(-3 * 60 * 60))), "u": { "fh": 73, "sd": 7 } },
            { "t": \(ms(now.addingTimeInterval(-60))), "u": { "fh": 9, "sd": 11 } }
          ]
        }
        """

        let windows = ClaudeDesktop.windowsFromHistory(json: json, now: now)
        XCTAssertEqual(windows?[0].usedFraction ?? -1, 0.09, accuracy: 0.0001)
        XCTAssertEqual(windows?[1].usedFraction ?? -1, 0.11, accuracy: 0.0001)
    }

    func testItParsesTheOnDiskHistoryFileWhenPresent() {
        guard ClaudeDesktop.isPresent else { return }
        let windows = ClaudeDesktop.windowsFromHistory()
        XCTAssertEqual(windows?.count, 2)
        XCTAssertEqual(windows?[0].id, "session")
        XCTAssertEqual(windows?[1].id, "weekly_all")
        if let reset = windows?[0].resetsAt {
            XCTAssertGreaterThan(reset, Date())
        }
        XCTAssertGreaterThanOrEqual(windows?[0].usedFraction ?? -1, 0)
        XCTAssertLessThanOrEqual(windows?[0].usedFraction ?? -1, 1.5)
    }

    private func date(_ year: Int, _ month: Int, _ day: Int, _ hour: Int, _ minute: Int) -> Date {
        var parts = DateComponents()
        parts.year = year
        parts.month = month
        parts.day = day
        parts.hour = hour
        parts.minute = minute
        return Calendar.current.date(from: parts)!
    }

    private func ms(_ date: Date) -> Int64 {
        Int64(date.timeIntervalSince1970 * 1000)
    }
}
