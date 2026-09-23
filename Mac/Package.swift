// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ElevenLabsMusicGeneratorMac",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "ElevenLabsMusicGeneratorMac", targets: ["ElevenLabsMusicGeneratorMac"])],
    targets: [
        .executableTarget(name: "ElevenLabsMusicGeneratorMac"),
        .testTarget(name: "ElevenLabsMusicGeneratorMacTests", dependencies: ["ElevenLabsMusicGeneratorMac"])
    ]
)
