import Foundation
import os

enum SoundtrackStage: Int, Sendable { case preparing, uploading, waiting, downloading, saving }

struct SoundtrackProgress: Sendable {
    let stage: SoundtrackStage
    var bytes: Int64 = 0
    var totalBytes: Int64 = 0
    var sequence = 0
    var message: String {
        switch stage {
        case .preparing: return "Preparing video upload."
        case .uploading: return "Uploading videos."
        case .waiting: return "Upload complete. Waiting for ElevenLabs to generate the soundtrack. This can take several minutes, even for a short video. No generation percentage or time remaining is available."
        case .downloading: return "Receiving soundtrack from ElevenLabs."
        case .saving: return "Saving soundtrack recordings."
        }
    }
    var percent: Int? {
        guard totalBytes > 0, stage == .uploading || stage == .downloading else { return nil }
        return Int(min(100, max(0, 100 * Double(bytes) / Double(totalBytes))))
    }
    func describe(elapsed: TimeInterval) -> String {
        var result = message + "\nElapsed: " + Self.elapsedText(elapsed) + "."
        if stage == .uploading || stage == .downloading {
            result += "\n" + (stage == .uploading ? "Sent: " : "Received: ") + max(0, bytes).formatted() + " bytes"
            if totalBytes > 0 { result += " of " + totalBytes.formatted() + " (\(percent ?? 0) percent)" }
            result += "."
        }
        return result
    }
    static func elapsedText(_ elapsed: TimeInterval) -> String {
        let total = elapsed.isFinite ? max(0, Int(elapsed.rounded(.down))) : 0
        let minutes = total / 60, seconds = total % 60
        return (minutes > 0 ? "\(minutes) \(minutes == 1 ? "minute" : "minutes"), " : "") + "\(seconds) \(seconds == 1 ? "second" : "seconds")"
    }
}

// Delegate events may race cancellation or completion. Sequence numbers let the UI discard late events.
final class SoundtrackProgressReporter: @unchecked Sendable {
    private let lock = NSLock()
    private var lastStage: SoundtrackStage?
    private var lastUpdate: TimeInterval = 0
    private var sequence = 0
    private let started = ProcessInfo.processInfo.systemUptime
    private let callback: @Sendable (SoundtrackProgress) -> Void
    private static let log = Logger(subsystem: "me.onj.ElevenLabsMusicGeneratorMac", category: "VideoSoundtrack")
    init(_ callback: @escaping @Sendable (SoundtrackProgress) -> Void) { self.callback = callback }
    func report(_ stage: SoundtrackStage, bytes: Int64 = 0, totalBytes: Int64 = 0, force: Bool = false) {
        lock.lock()
        if let lastStage, stage.rawValue < lastStage.rawValue { lock.unlock(); return }
        let now = ProcessInfo.processInfo.systemUptime
        let changed = lastStage != stage
        guard changed || force || now - lastUpdate >= 1 else { lock.unlock(); return }
        lastStage = stage; lastUpdate = now; sequence += 1
        let value = SoundtrackProgress(stage: stage, bytes: bytes, totalBytes: totalBytes, sequence: sequence)
        if changed { Self.log.info("Soundtrack stage=\(String(describing: stage), privacy: .public); elapsed=\(now - self.started, privacy: .public)s") }
        lock.unlock()
        callback(value)
    }
}
