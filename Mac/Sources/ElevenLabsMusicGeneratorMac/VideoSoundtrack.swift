import AVFoundation
import Foundation
import ZIPFoundation

struct SoundtrackVideo: Identifiable {
    let id = UUID()
    let url: URL
    let bytes: Int
    let modified: Date
    let seconds: Double

    static func read(_ url: URL) async throws -> SoundtrackVideo {
        let values = try url.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey, .isRegularFileKey])
        let asset = AVURLAsset(url: url)
        let duration = try await asset.load(.duration).seconds
        guard values.isRegularFile == true, duration.isFinite, duration > 0,
              !(try await asset.loadTracks(withMediaType: .video)).isEmpty else {
            throw MusicError.validation("Choose a readable video with a positive duration.")
        }
        return SoundtrackVideo(url: url, bytes: values.fileSize ?? 0,
            modified: values.contentModificationDate ?? .distantPast, seconds: duration)
    }
}

struct VideoSoundtrackRequest {
    static let maximumUploadBytes = 200_000_000
    static let maximumResponseBytes = 512 * 1024 * 1024
    var videos: [SoundtrackVideo] = []
    var description = ""
    var tags = ""
    var name = ""
    var model: MusicModel = .v25
    var format: AudioFormat = .mp3_44100_192
    var addSoundEffects = false
    var separateTracks = false
    var useAudioAsReference = false
    var outputFolder: URL
    var styleTags: [String] { tags.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty } }

    func validate() throws {
        guard (1...10).contains(videos.count) else { throw MusicError.validation("Choose from 1 to 10 videos.") }
        guard description.utf16.count <= 4100, styleTags.count <= 10 else { throw MusicError.validation("Use at most 4100 description characters and 10 style tags, one per line.") }
        guard model != .soundEffects else { throw MusicError.validation("Choose a Music model for the soundtrack.") }
        var bytes = 0
        var seconds = 0.0
        for video in videos {
            let values = try video.url.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey])
            guard values.fileSize == video.bytes, values.contentModificationDate == video.modified,
                  video.bytes > 0, video.seconds.isFinite, video.seconds > 0 else {
                throw MusicError.validation("A selected video changed or is unavailable. Remove it and add it again.")
            }
            guard video.bytes <= Self.maximumUploadBytes - bytes else { throw MusicError.validation("The videos together must not exceed 200 MB.") }
            bytes += video.bytes; seconds += video.seconds
        }
        guard seconds <= 600 else { throw MusicError.validation("The videos together must not exceed 10 minutes.") }
    }

    var fields: [(String, String)] {
        var result: [(String, String)] = []
        if !description.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { result.append(("description", description)) }
        result += styleTags.map { ("tags", $0) }
        result += [("model_id", model.rawValue), ("add_sfx", String(addSoundEffects)),
            ("separate_tracks", String(separateTracks)), ("use_audio_as_reference", String(useAudioAsReference))]
        return result
    }

    func writeMultipart(to url: URL, boundary: String) throws {
        try validate()
        FileManager.default.createFile(atPath: url.path, contents: nil)
        let output = try FileHandle(forWritingTo: url)
        defer { try? output.close() }
        func text(_ value: String) throws { try output.write(contentsOf: Data(value.utf8)) }
        for (key, value) in fields { try text("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(key)\"\r\n\r\n\(value)\r\n") }
        for (index, video) in videos.enumerated() {
            try Task.checkCancellation()
            let suffix = video.url.pathExtension.lowercased()
            let ext = suffix.range(of: "^[a-z0-9]{1,8}$", options: .regularExpression) != nil ? suffix : "mp4"
            // The live endpoint requires repeated 'videos' fields, unlike its curl example.
            try text("--\(boundary)\r\nContent-Disposition: form-data; name=\"videos\"; filename=\"video\(index).\(ext)\"\r\nContent-Type: application/octet-stream\r\n\r\n")
            let input = try FileHandle(forReadingFrom: video.url)
            defer { try? input.close() }
            var copied = 0
            while let data = try input.read(upToCount: 65_536), !data.isEmpty {
                try Task.checkCancellation(); copied += data.count
                guard copied <= video.bytes else { throw MusicError.validation("A video changed while preparing the upload.") }
                try output.write(contentsOf: data)
            }
            guard copied == video.bytes else { throw MusicError.validation("A video changed while preparing the upload.") }
            try text("\r\n")
        }
        try text("--\(boundary)--\r\n")
    }
}

private final class SoundtrackDownloadDelegate: NSObject, URLSessionDownloadDelegate, @unchecked Sendable {
    private let reporter: SoundtrackProgressReporter
    private let bodyBytes: Int64
    private let destination: URL
    private let lock = NSLock()
    private var limitExceeded = false
    private var cancelled = false
    private var task: URLSessionDownloadTask?
    private var continuation: CheckedContinuation<(URL, URLResponse), Error>?
    private var result: Result<(URL, URLResponse), Error>?
    init(reporter: SoundtrackProgressReporter, bodyBytes: Int64, destination: URL) {
        self.reporter = reporter; self.bodyBytes = bodyBytes; self.destination = destination
    }
    func download(session: URLSession, request: URLRequest) async throws -> (URL, URLResponse) {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                let task = session.downloadTask(with: request)
                task.delegate = self
                lock.lock()
                self.task = task; self.continuation = continuation
                let cancelled = self.cancelled
                lock.unlock()
                if cancelled { task.cancel() }
                task.resume()
            }
        } onCancel: { self.cancel() }
    }
    private func cancel() {
        lock.lock(); cancelled = true; let task = task; lock.unlock()
        task?.cancel()
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64,
                    totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        reporter.report(.uploading, bytes: totalBytesSent, totalBytes: bodyBytes,
            force: totalBytesSent >= bodyBytes)
        if bodyBytes > 0, totalBytesSent >= bodyBytes { reporter.report(.waiting) }
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        let value: Result<(URL, URLResponse), Error>
        do {
            guard let response = downloadTask.response else { throw MusicError.response("No HTTP response was received.") }
            try FileManager.default.moveItem(at: location, to: destination)
            value = .success((destination, response))
        } catch { value = .failure(error) }
        lock.lock(); result = value; lock.unlock()
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        lock.lock()
        let continuation = self.continuation
        let result: Result<(URL, URLResponse), Error> = limitExceeded
            ? .failure(MusicError.response("The soundtrack response exceeded its size limit."))
            : cancelled ? .failure(CancellationError()) : error.map { .failure($0) }
                ?? self.result ?? .failure(MusicError.response("No soundtrack was received."))
        self.continuation = nil; self.task = nil
        lock.unlock()
        continuation?.resume(with: result)
    }
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        if totalBytesWritten > VideoSoundtrackRequest.maximumResponseBytes || totalBytesExpectedToWrite > VideoSoundtrackRequest.maximumResponseBytes {
            lock.lock(); limitExceeded = true; lock.unlock(); downloadTask.cancel(); return
        }
        reporter.report(.downloading, bytes: totalBytesWritten, totalBytes: totalBytesExpectedToWrite,
            force: totalBytesExpectedToWrite > 0 && totalBytesWritten >= totalBytesExpectedToWrite)
    }
}

extension MusicService {
    func generateSoundtrack(_ input: VideoSoundtrackRequest, key: String, progress: @escaping @Sendable (SoundtrackProgress) -> Void) async throws -> [URL] {
        try input.validate()
        let reporter = SoundtrackProgressReporter(progress)
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("ElevenLabsSoundtrack-" + UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: work, withIntermediateDirectories: false)
        defer { try? fm.removeItem(at: work) }
        let body = work.appendingPathComponent("upload.multipart")
        let boundary = "Soundtrack" + UUID().uuidString
        reporter.report(.preparing)
        try input.writeMultipart(to: body, boundary: boundary)
        var components = URLComponents(url: root.appendingPathComponent("/v1/music/video-to-music"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "output_format", value: input.format.rawValue)]
        var request = URLRequest(url: components.url!)
        request.httpMethod = "POST"; request.timeoutInterval = 20 * 60
        request.setValue(key, forHTTPHeaderField: "xi-api-key")
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        let bodyBytes = Int64(try body.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0)
        request.setValue(String(bodyBytes), forHTTPHeaderField: "Content-Length")
        request.httpBodyStream = InputStream(url: body)
        reporter.report(.uploading, totalBytes: bodyBytes)
        // Async download(for:) suppresses download-delegate callbacks. Use a task delegate
        // so transfer progress and the streaming response-size guard remain effective.
        let delegate = SoundtrackDownloadDelegate(reporter: reporter, bodyBytes: bodyBytes, destination: work.appendingPathComponent("response.download"))
        let (download, response) = try await delegate.download(session: session, request: request)
        defer { try? fm.removeItem(at: download) }
        guard let http = response as? HTTPURLResponse else { throw MusicError.response("No HTTP response was received.") }
        guard (200...299).contains(http.statusCode) else {
            let file = try FileHandle(forReadingFrom: download); defer { try? file.close() }
            throw Self.apiError(status: http.statusCode, data: try file.read(upToCount: 65_536) ?? Data())
        }
        let contentType = http.mimeType ?? ""
        guard !contentType.hasPrefix("text/"), !contentType.contains("json") else { throw MusicError.response("ElevenLabs returned a document instead of soundtrack audio.") }
        guard (try download.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0) <= VideoSoundtrackRequest.maximumResponseBytes else {
            throw MusicError.response("The soundtrack response exceeded its size limit.")
        }
        let stage = work.appendingPathComponent("recordings", isDirectory: true)
        reporter.report(.saving)
        try fm.createDirectory(at: stage, withIntermediateDirectories: false)
        let names = try Self.saveSoundtrack(download, to: stage, format: input.format, separate: input.separateTracks)
        try Task.checkCancellation()
        try fm.createDirectory(at: input.outputFolder, withIntermediateDirectories: true)
        let requested = input.name.isEmpty ? input.videos[0].url.deletingPathExtension().lastPathComponent + " Soundtrack" : input.name
        let destination = input.outputFolder.appendingPathComponent(GenerationRequest.safeStem(requested) + " - " + UUID().uuidString.prefix(8), isDirectory: true)
        try fm.moveItem(at: stage, to: destination)
        return names.map { destination.appendingPathComponent($0) }
    }

    static func saveSoundtrack(_ source: URL, to folder: URL, format: AudioFormat, separate: Bool) throws -> [String] {
        var names: [String] = []
        if separate {
            let archive = try Archive(url: source, accessMode: .read)
            let entries = Array(archive)
            guard entries.count <= 32, entries.reduce(UInt64(0), { $0 + $1.uncompressedSize }) <= VideoSoundtrackRequest.maximumResponseBytes else {
                throw MusicError.response("The soundtrack archive is too large.")
            }
            for entry in entries {
                try Task.checkCancellation()
                let parts = entry.path.replacingOccurrences(of: "\\", with: "/").components(separatedBy: "/")
                guard !parts.contains(".."), !entry.path.hasPrefix("/"), !entry.path.contains(":"), entry.type != .symlink else {
                    throw MusicError.response("The soundtrack archive contains an unsafe path.")
                }
                if entry.type == .directory { continue }
                let path = URL(fileURLWithPath: entry.path)
                let ext = path.pathExtension.lowercased()
                if ext == "json" || ext == "txt" { continue }
                guard ["wav", "mp3", "pcm"].contains(ext) else { throw MusicError.response("The soundtrack archive contains an unsupported file.") }
                let name = GenerationRequest.safeStem(path.deletingPathExtension().lastPathComponent) + "." + (ext == "pcm" ? "wav" : ext)
                guard !names.contains(where: { $0.caseInsensitiveCompare(name) == .orderedSame }) else { throw MusicError.response("The soundtrack archive contains duplicate track names.") }
                let raw = folder.appendingPathComponent(UUID().uuidString + ".raw")
                FileManager.default.createFile(atPath: raw.path, contents: nil)
                let writer = try FileHandle(forWritingTo: raw)
                defer { try? writer.close(); try? FileManager.default.removeItem(at: raw) }
                var bytes = 0
                let checksum = try archive.extract(entry) { data in
                    try Task.checkCancellation(); bytes += data.count
                    guard bytes <= VideoSoundtrackRequest.maximumResponseBytes else { throw MusicError.response("The soundtrack track exceeded its size limit.") }
                    try writer.write(contentsOf: data)
                }
                guard checksum == entry.checksum else { throw MusicError.response("A soundtrack track failed its integrity check.") }
                try writer.close()
                try saveSoundtrackAudio(raw, to: folder.appendingPathComponent(name), pcm: ext == "pcm")
                names.append(name)
            }
        } else {
            let name = "Soundtrack." + format.fileExtension
            try saveSoundtrackAudio(source, to: folder.appendingPathComponent(name), pcm: format == .wav)
            names.append(name)
        }
        guard !names.isEmpty else { throw MusicError.response("ElevenLabs returned no playable soundtrack tracks.") }
        return names
    }

    private static func saveSoundtrackAudio(_ source: URL, to destination: URL, pcm: Bool) throws {
        let file = try FileHandle(forReadingFrom: source); defer { try? file.close() }
        let head = try file.read(upToCount: 12) ?? Data()
        let wave = head.count == 12 && head.prefix(4) == Data("RIFF".utf8) && head.suffix(4) == Data("WAVE".utf8)
        let mp3 = head.count >= 4 && (head.prefix(3) == Data("ID3".utf8) || head[0] == 255 && head[1] & 224 == 224)
        if pcm && !wave {
            let size = try source.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
            guard size > 0, size % 4 == 0 else { throw MusicError.response("The returned PCM soundtrack is incomplete.") }
            try WaveWriter.wrap(input: source, output: destination, sampleRate: 44_100)
        } else {
            guard wave || mp3 else { throw MusicError.response("ElevenLabs did not return supported audio.") }
            try FileManager.default.copyItem(at: source, to: destination)
        }
    }
}
