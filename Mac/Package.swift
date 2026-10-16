// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ElevenLabsMusicGeneratorMac",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "ElevenLabsMusicGeneratorMac", targets: ["ElevenLabsMusicGeneratorMac"])],
    dependencies: [.package(url: "https://github.com/weichsel/ZIPFoundation.git", exact: "0.9.20")],
    targets: [
        .executableTarget(name: "ElevenLabsMusicGeneratorMac", dependencies: ["ZIPFoundation"]),
        .testTarget(name: "ElevenLabsMusicGeneratorMacTests", dependencies: ["ElevenLabsMusicGeneratorMac"])
    ]
)
