// swift-tools-version: 6.0
import PackageDescription
import Foundation

let package = Package(
    name: "AgentSpeed",
    defaultLocalization: "en",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "AgentSpeed", targets: ["AgentSpeed"]),
        .executable(name: "AgentSpeedCodexMeter", targets: ["AgentSpeedCodexMeter"]),
    ],
    targets: [
        .target(
            name: "CZstd",
            path: "Sources/CZstd",
            exclude: ["LICENSE", "VENDOR.md"],
            publicHeadersPath: ".",
            cSettings: [
                .define("ZSTD_DISABLE_ASM", to: "1"),
                .define("ZSTD_LEGACY_SUPPORT", to: "0"),
                .define("ZSTD_TRACE", to: "0"),
            ]
        ),
        .target(
            name: "AgentSpeedCore",
            dependencies: ["CZstd"],
            path: "Sources/AgentSpeedCore"
        ),
        .target(
            name: "AgentSpeedHero",
            path: "Sources/AgentSpeedHero"
        ),
        .target(
            name: "AgentSpeedOrbModel",
            dependencies: ["AgentSpeedCore"],
            path: "Sources/AgentSpeedOrbModel"
        ),
        .executableTarget(
            name: "AgentSpeed",
            dependencies: ["AgentSpeedCore", "AgentSpeedHero", "AgentSpeedOrbModel"],
            path: "Sources/AgentSpeed"
        ),
        .executableTarget(
            name: "AgentSpeedProbe",
            dependencies: ["AgentSpeedCore"],
            path: "Sources/AgentSpeedProbe"
        ),
        .executableTarget(
            name: "AgentSpeedCodexMeter",
            dependencies: ["AgentSpeedCore"],
            path: "Sources/AgentSpeedCodexMeter"
        ),
        .testTarget(
            name: "AgentSpeedCoreTests",
            dependencies: ["AgentSpeedCore"],
            path: "Tests/AgentSpeedCoreTests"
        ),
        .testTarget(
            name: "AgentSpeedHeroTests",
            dependencies: ["AgentSpeedHero"],
            path: "Tests/AgentSpeedHeroTests"
        ),
        .testTarget(
            name: "AgentSpeedOrbModelTests",
            dependencies: ["AgentSpeedOrbModel", "AgentSpeedCore"],
            path: "Tests/AgentSpeedOrbModelTests"
        ),
    ]
)

// The standalone meter and Core tests also build with Command Line Tools, which do not ship SwiftUI macros.
// Keep the GUI app's normal package unchanged unless this explicit standalone build mode is requested.
if ProcessInfo.processInfo.environment["AGENT_SPEED_CODEX_METER_ONLY"] == "1" {
    let targets: Set<String> = ["CZstd", "AgentSpeedCore", "AgentSpeedCodexMeter", "AgentSpeedCoreTests"]
    package.products.removeAll { $0.name != "AgentSpeedCodexMeter" }
    package.targets.removeAll { !targets.contains($0.name) }
}
