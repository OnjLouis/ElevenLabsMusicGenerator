import Foundation

struct MusicService {
    var session: URLSession = .shared
    var root = URL(string: "https://api.elevenlabs.io")!

    func testKey(_ key: String) async throws -> String {
        let body: [String: Any] = ["prompt": "A short instrumental piano phrase", "music_length_ms": 3000,
            "model_id": MusicModel.v25.rawValue]
        _ = try await request(path: "/v1/music/plan", key: key, body: body)
        return "Music API key accepted. No music was generated."
    }

    func subscriptionBalance(_ key: String) async throws -> SubscriptionBalance {
        let (data, _) = try await request(path: "/v1/user/subscription", key: key, method: "GET", timeout: 15)
        return try SubscriptionBalance.decode(data)
    }

    func suggestPlan(prompt: String, seconds: Int, model: MusicModel, key: String) async throws -> CompositionPlan {
        guard model == .v2 || model == .v25 else { throw MusicError.validation("Plans require Music v2 or v2.5.") }
        let body: [String: Any] = ["prompt": prompt, "music_length_ms": seconds * 1000, "model_id": model.rawValue]
        let (data, _) = try await request(path: "/v1/music/plan", key: key, body: body)
        return try CompositionPlan.decodePayload(data)
    }

    func generate(_ input: GenerationRequest, index: Int, key: String) async throws -> GeneratedTrack {
        try input.validate()
        let destination = try input.resolvedOutputURL(index)
        let fm = FileManager.default
        guard !fm.fileExists(atPath: destination.path) else {
            throw MusicError.validation("The existing track will not be overwritten: \(destination.lastPathComponent)")
        }
        try fm.createDirectory(at: input.outputFolder, withIntermediateDirectories: true)
        try fm.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
        let body = input.payload
        let path = input.endpoint
        var components = URLComponents(url: root.appendingPathComponent(path), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "output_format", value: input.format.rawValue)]
        var request = URLRequest(url: components.url!)
        request.httpMethod = "POST"
        request.timeoutInterval = 20 * 60
        request.setValue(key, forHTTPHeaderField: "xi-api-key")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("ElevenLabs Music Generator Mac/1.3.1", forHTTPHeaderField: "User-Agent")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let start = Date()
        let (download, response) = try await session.download(for: request)
        guard let http = response as? HTTPURLResponse else { throw MusicError.response("No HTTP response was received.") }
        guard (200...299).contains(http.statusCode) else {
            throw Self.apiError(status: http.statusCode, data: (try? Data(contentsOf: download)) ?? Data())
        }
        let rawPart = destination.appendingPathExtension("download.part")
        let audioPart = destination.appendingPathExtension("part")
        defer {
            try? fm.removeItem(at: rawPart)
            try? fm.removeItem(at: audioPart)
        }
        try? fm.removeItem(at: rawPart)
        try? fm.removeItem(at: audioPart)
        try fm.moveItem(at: download, to: rawPart)
        let details: String?
        if (input.includeDetails || input.useGeneratedTitle) && !input.isSoundEffect {
            let parsed = try MultipartMusicResponse.parse(file: rawPart, contentType: http.value(forHTTPHeaderField: "Content-Type") ?? "")
            details = parsed.metadata
            try parsed.audio.write(to: audioPart, options: .atomic)
        } else {
            details = nil
            try fm.moveItem(at: rawPart, to: audioPart)
        }
        if input.format == .wav {
            let wrapped = destination.appendingPathExtension("wav.part")
            defer { try? fm.removeItem(at: wrapped) }
            try WaveWriter.wrap(input: audioPart, output: wrapped, sampleRate: 44_100)
            try fm.removeItem(at: audioPart)
            try fm.moveItem(at: wrapped, to: audioPart)
        }
        guard (try? audioPart.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0) ?? 0 > 0 else {
            throw MusicError.response("ElevenLabs returned an empty audio file.")
        }
        var finalDestination = destination
        if input.useGeneratedTitle {
            let title = details.flatMap { Self.extractTitle($0) } ?? ""
            finalDestination = try GeneratedTitleManifest.chooseOutputURL(input, title: title, index: index)
        }
        let sidecarFolder = input.outputFolder.appendingPathComponent("Lyrics", isDirectory: true)
        var detailsURL: URL?
        var lyricsURL: URL?
        if let details, input.includeDetails {
            try fm.createDirectory(at: sidecarFolder, withIntermediateDirectories: true)
            let base = finalDestination.deletingPathExtension().lastPathComponent
            let alternate = finalDestination.deletingPathExtension().appendingPathExtension(input.format == .wav ? "mp3" : "wav")
            let sidecarStem = base + (fm.fileExists(atPath: alternate.path) ? ".\(input.format.fileExtension)" : "")
            let jsonURL = sidecarFolder.appendingPathComponent(sidecarStem + ".details.json")
            let readableDetails: String
            if let data = details.data(using: .utf8),
               let object = try? JSONSerialization.jsonObject(with: data),
               let formatted = try? JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted, .sortedKeys]),
               let text = String(data: formatted, encoding: .utf8) {
                readableDetails = text
            } else {
                readableDetails = details
            }
            try Data((readableDetails + "\n").utf8).write(to: jsonURL, options: .atomic)
            detailsURL = jsonURL
            if let lyrics = Self.extractLyrics(details), !lyrics.isEmpty {
                let textURL = sidecarFolder.appendingPathComponent(sidecarStem + ".txt")
                try Data((lyrics + "\n").utf8).write(to: textURL, options: .atomic)
                lyricsURL = textURL
            }
        }
        // Publish audio only after the response and any requested metadata are valid.
        guard !fm.fileExists(atPath: finalDestination.path) else {
            throw MusicError.validation("Another file appeared at \(finalDestination.lastPathComponent); it was not overwritten.")
        }
        if input.useGeneratedTitle {
            if index == 1 { try input.sourceData.write(to: input.promptURL, options: .atomic) }
            try GeneratedTitleManifest.reserve(input, index: index, output: finalDestination)
        }
        try fm.moveItem(at: audioPart, to: finalDestination)
        if index == 1 && !input.useGeneratedTitle {
            try input.sourceData.write(to: input.promptURL, options: .atomic)
        }
        return GeneratedTrack(url: finalDestination, detailsURL: detailsURL, lyricsURL: lyricsURL,
            seconds: Date().timeIntervalSince(start), songID: http.value(forHTTPHeaderField: "song-id"))
    }

    private static func extractTitle(_ json: String) -> String? {
        guard let data = json.data(using: .utf8),
              let result = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let metadata = result["song_metadata"] as? [String: Any] else { return nil }
        return (metadata["title"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func request(path: String, key: String, method: String = "POST", body: [String: Any]? = nil,
                         timeout: TimeInterval = 20 * 60) async throws -> (Data, HTTPURLResponse) {
        var request = URLRequest(url: root.appendingPathComponent(path))
        request.httpMethod = method
        request.timeoutInterval = timeout
        request.setValue(key, forHTTPHeaderField: "xi-api-key")
        request.setValue("ElevenLabs Music Generator Mac/1.3.1", forHTTPHeaderField: "User-Agent")
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
        }
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw MusicError.response("No HTTP response was received.") }
        guard (200...299).contains(http.statusCode) else { throw Self.apiError(status: http.statusCode, data: data) }
        return (data, http)
    }

    private static func apiError(status: Int, data: Data) -> MusicError {
        let object = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
        let detail = object?["detail"] ?? object?["error"] ?? object?["message"]
        let text: String
        if let detail = detail as? String { text = detail }
        else if let detail = detail as? [String: Any] { text = detail["message"] as? String ?? String(describing: detail) }
        else { text = String(data: data.prefix(500), encoding: .utf8) ?? "Request failed." }
        return .service(status, text)
    }

    private static func extractLyrics(_ metadata: String) -> String? {
        guard let root = (try? JSONSerialization.jsonObject(with: Data(metadata.utf8))) as? [String: Any],
              let plan = root["composition_plan"] as? [String: Any] else { return nil }
        if let chunks = plan["chunks"] as? [[String: Any]] {
            let lines = chunks.compactMap { $0["text"] as? String }.filter { $0.contains("\n") }
            return lines.isEmpty ? nil : lines.joined(separator: "\n\n")
        }
        if let sections = plan["sections"] as? [[String: Any]] {
            let lines = sections.compactMap { section -> String? in
                guard let words = section["lines"] as? [String], !words.isEmpty else { return nil }
                return "[\(section["section_name"] as? String ?? "Section")]\n" + words.joined(separator: "\n")
            }
            return lines.isEmpty ? nil : lines.joined(separator: "\n\n")
        }
        return nil
    }
}

struct GeneratedTrack {
    var url: URL
    var detailsURL: URL?
    var lyricsURL: URL?
    var seconds: TimeInterval
    var songID: String?
}

enum MultipartMusicResponse {
    static func parse(file: URL, contentType: String) throws -> (audio: Data, metadata: String) {
        guard contentType.lowercased().contains("multipart/mixed"),
              let boundaryField = contentType.components(separatedBy: ";").map({ $0.trimmingCharacters(in: .whitespaces) }).first(where: { $0.lowercased().hasPrefix("boundary=") }) else {
            throw MusicError.response("The detailed response was not multipart/mixed. No incomplete output was kept.")
        }
        let boundary = String(boundaryField.dropFirst("boundary=".count)).trimmingCharacters(in: CharacterSet(charactersIn: "\""))
        guard !boundary.isEmpty, boundary.utf8.count <= 200 else { throw MusicError.response("Invalid multipart boundary.") }
        let data = try Data(contentsOf: file, options: .mappedIfSafe)
        let opening = Data(("--" + boundary + "\r\n").utf8)
        guard data.starts(with: opening) else { throw MusicError.response("Invalid opening response boundary.") }
        let divider = Data("\r\n\r\n".utf8)
        let marker = Data(("\r\n--" + boundary).utf8)
        var position = opening.count
        var audio: Data?
        var metadata: String?
        while position < data.count {
            guard let headerRange = data.range(of: divider, in: position..<data.count) else { throw MusicError.response("Incomplete response headers.") }
            let headers = String(data: data[position..<headerRange.lowerBound], encoding: .utf8) ?? ""
            let start = headerRange.upperBound
            guard let end = data.range(of: marker, in: start..<data.count) else { throw MusicError.response("Incomplete response part.") }
            let part = Data(data[start..<end.lowerBound])
            if headers.lowercased().contains("json") {
                guard metadata == nil, part.count <= 10 * 1024 * 1024, let text = String(data: part, encoding: .utf8) else {
                    throw MusicError.response("Invalid or duplicate response metadata.")
                }
                metadata = text
            } else {
                guard audio == nil, part.count <= 1024 * 1024 * 1024 else { throw MusicError.response("Invalid or duplicate audio part.") }
                audio = part
            }
            position = end.upperBound
            if data[position...].starts(with: Data("--".utf8)) { break }
            guard data[position...].starts(with: Data("\r\n".utf8)) else { throw MusicError.response("Invalid response boundary suffix.") }
            position += 2
        }
        guard let audio, !audio.isEmpty, let metadata else {
            throw MusicError.response("The detailed response did not contain both audio and metadata.")
        }
        return (audio, metadata)
    }
}

enum WaveWriter {
    static func wrap(input: URL, output: URL, sampleRate: UInt32) throws {
        let size = try FileManager.default.attributesOfItem(atPath: input.path)[.size] as? UInt64 ?? 0
        guard size > 0, size <= UInt64(UInt32.max - 36), size.isMultiple(of: 4) else {
            throw MusicError.response("The PCM response is empty, too large, or not 16-bit stereo audio.")
        }
        var header = Data()
        func ascii(_ value: String) { header.append(Data(value.utf8)) }
        func u16(_ value: UInt16) { var value = value.littleEndian; withUnsafeBytes(of: &value) { header.append(contentsOf: $0) } }
        func u32(_ value: UInt32) { var value = value.littleEndian; withUnsafeBytes(of: &value) { header.append(contentsOf: $0) } }
        ascii("RIFF"); u32(UInt32(size + 36)); ascii("WAVEfmt "); u32(16); u16(1); u16(2)
        u32(sampleRate); u32(sampleRate * 4); u16(4); u16(16); ascii("data"); u32(UInt32(size))
        FileManager.default.createFile(atPath: output.path, contents: header)
        let reader = try FileHandle(forReadingFrom: input)
        let writer = try FileHandle(forWritingTo: output)
        defer { try? reader.close(); try? writer.close() }
        try writer.seekToEnd()
        while let chunk = try reader.read(upToCount: 64 * 1024), !chunk.isEmpty {
            try writer.write(contentsOf: chunk)
        }
    }
}
