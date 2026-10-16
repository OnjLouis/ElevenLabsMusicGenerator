import Foundation
import XCTest
import ZIPFoundation
@testable import ElevenLabsMusicGeneratorMac

final class VideoSoundtrackTests: XCTestCase {
    func testProgressHasOnlyRealTransferPercentages() {
        XCTAssertEqual(SoundtrackProgress.elapsedText(65.9), "1 minute, 5 seconds")
        XCTAssertEqual(SoundtrackProgress.elapsedText(-1), "0 seconds")
        XCTAssertEqual(SoundtrackProgress.elapsedText(.infinity), "0 seconds")
        let waiting = SoundtrackProgress(stage: .waiting)
        XCTAssertNil(waiting.percent)
        XCTAssertTrue(waiting.describe(elapsed: 300).contains("5 minutes, 0 seconds"))
        XCTAssertTrue(waiting.message.contains("several minutes"))
        XCTAssertNil(SoundtrackProgress(stage: .downloading, bytes: 5, totalBytes: -1).percent)
        XCTAssertEqual(SoundtrackProgress(stage: .uploading, bytes: .max, totalBytes: 1).percent, 100)
        XCTAssertEqual(SoundtrackProgress(stage: .uploading, bytes: -1, totalBytes: 10).percent, 0)
    }

    func testProgressReporterThrottlesAndDoesNotRegress() {
        let collected = ProgressCollection()
        let reporter = SoundtrackProgressReporter { collected.append($0) }
        reporter.report(.preparing)
        reporter.report(.uploading, bytes: 0, totalBytes: 10)
        for count in 1...9 { reporter.report(.uploading, bytes: Int64(count), totalBytes: 10) }
        reporter.report(.uploading, bytes: 10, totalBytes: 10, force: true)
        reporter.report(.waiting)
        reporter.report(.uploading, bytes: 10, totalBytes: 10, force: true)
        reporter.report(.downloading, bytes: 12, totalBytes: 12)
        reporter.report(.saving)
        XCTAssertEqual(collected.values.map(\.stage), [.preparing, .uploading, .uploading, .waiting, .downloading, .saving])
        XCTAssertEqual(collected.values.map(\.sequence), [1, 2, 3, 4, 5, 6])
    }
    func testMultipartAndLimits() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let video = folder.appendingPathComponent("input.mp4")
        try Data([1, 2, 3, 4]).write(to: video)
        let values = try video.resourceValues(forKeys: [.contentModificationDateKey])
        var input = VideoSoundtrackRequest(outputFolder: folder)
        input.videos = [SoundtrackVideo(url: video, bytes: 4, modified: try XCTUnwrap(values.contentModificationDate), seconds: 4)]
        input.description = "Calm piano"; input.tags = "cinematic\nquiet"
        let body = folder.appendingPathComponent("multipart")
        try input.writeMultipart(to: body, boundary: "test")
        let text = String(decoding: try Data(contentsOf: body), as: UTF8.self)
        XCTAssertTrue(text.contains("name=\"videos\"; filename=\"video0.mp4\""))
        XCTAssertTrue(text.contains("name=\"model_id\"\r\n\r\nmusic_v2_5"))
        XCTAssertEqual(input.styleTags, ["cinematic", "quiet"])
        input.videos = Array(repeating: input.videos[0], count: 11)
        XCTAssertThrowsError(try input.validate())
        input.videos = [SoundtrackVideo(url: video, bytes: 4, modified: try XCTUnwrap(values.contentModificationDate), seconds: 601)]
        XCTAssertThrowsError(try input.validate())
        input.videos = [SoundtrackVideo(url: video, bytes: 3, modified: try XCTUnwrap(values.contentModificationDate), seconds: 4)]
        XCTAssertThrowsError(try input.validate())
    }

    func testSafeSeparateTracksAndUnsafePaths() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let source = folder.appendingPathComponent("source.mp3")
        try Data("ID3test-audio".utf8).write(to: source)
        let archiveURL = folder.appendingPathComponent("tracks.zip")
        let archive = try Archive(url: archiveURL, accessMode: .create)
        try archive.addEntry(with: "music.mp3", fileURL: source)
        try archive.addEntry(with: "sfx.mp3", fileURL: source)
        let output = folder.appendingPathComponent("tracks")
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: false)
        XCTAssertEqual(try MusicService.saveSoundtrack(archiveURL, to: output, format: .mp3_44100_192, separate: true).sorted(), ["music.mp3", "sfx.mp3"])
        let unsafeURL = folder.appendingPathComponent("unsafe.zip")
        let unsafe = try Archive(url: unsafeURL, accessMode: .create)
        try unsafe.addEntry(with: "../escape.mp3", fileURL: source)
        XCTAssertThrowsError(try MusicService.saveSoundtrack(unsafeURL, to: output, format: .mp3_44100_192, separate: true))
        XCTAssertFalse(FileManager.default.fileExists(atPath: folder.appendingPathComponent("escape.mp3").path))
    }

    func testPCMResponseAndEmptyAudio() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let raw = folder.appendingPathComponent("raw")
        try Data(repeating: 0, count: 16).write(to: raw)
        XCTAssertEqual(try MusicService.saveSoundtrack(raw, to: folder, format: .wav, separate: false), ["Soundtrack.wav"])
        XCTAssertEqual(try Data(contentsOf: folder.appendingPathComponent("Soundtrack.wav")).prefix(4), Data("RIFF".utf8))
        try Data().write(to: raw)
        XCTAssertThrowsError(try MusicService.saveSoundtrack(raw, to: folder, format: .mp3_44100_192, separate: false))
    }
}

private final class ProgressCollection: @unchecked Sendable {
    private let lock = NSLock()
    private var items: [SoundtrackProgress] = []
    var values: [SoundtrackProgress] { lock.lock(); defer { lock.unlock() }; return items }
    func append(_ value: SoundtrackProgress) { lock.lock(); defer { lock.unlock() }; items.append(value) }
}
