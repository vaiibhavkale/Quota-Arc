import SwiftUI
import XCTest
@testable import QuotaArc

final class UsageBandTests: XCTestCase {
    func testBandsMatchTheDesignFrame() {
        // The three levels the mockup renders, and the colour it renders them in.
        XCTAssertEqual(UsageBand.band(for: 0.21), .ample)
        XCTAssertEqual(UsageBand.band(for: 0.52), .watch)
        XCTAssertEqual(UsageBand.band(for: 0.73), .critical)
    }

    func testBoundaries() {
        XCTAssertEqual(UsageBand.band(for: 0.0), .ample)
        XCTAssertEqual(UsageBand.band(for: 0.4999), .ample)
        XCTAssertEqual(UsageBand.band(for: 0.50), .watch)
        XCTAssertEqual(UsageBand.band(for: 0.6999), .watch)
        XCTAssertEqual(UsageBand.band(for: 0.70), .critical)
        XCTAssertEqual(UsageBand.band(for: 0.9999), .critical)
        XCTAssertEqual(UsageBand.band(for: 1.0), .exhausted)
        XCTAssertEqual(UsageBand.band(for: 1.4), .exhausted)
    }

    /// Only the ample state takes the user's accent choice. The warning bands
    /// exist to interrupt whatever else is on screen, and a customisable
    /// warning colour could be chosen into invisibility — so they stay fixed
    /// regardless of what accent is passed in.
    func testOnlyAmpleFollowsTheChosenAccent() {
        let accent = Color.pink
        XCTAssertEqual(UsageBand.ample.color(accent: accent), accent)
        XCTAssertEqual(UsageBand.watch.color(accent: accent), Palette.watch)
        XCTAssertEqual(UsageBand.critical.color(accent: accent), Palette.critical)
        XCTAssertEqual(UsageBand.exhausted.color(accent: accent), Palette.critical)
    }
}
