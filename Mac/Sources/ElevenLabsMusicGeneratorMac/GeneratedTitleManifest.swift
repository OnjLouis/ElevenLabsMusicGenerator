import CryptoKit
import Foundation

enum GeneratedTitleManifest {
    private struct Record: Codable {
        var sourceHash: String
        var model: String
        var format: String
        var outputs: [String: String]
    }

    private static func url(_ request: GenerationRequest) -> URL {
        request.outputFolder.appendingPathComponent("Lyrics", isDirectory: true)
            .appendingPathComponent(GenerationRequest.suggestedStem(request.prompt) + ".\(request.model.rawValue).\(request.format.rawValue).titles.json")
    }

    private static func hash(_ request: GenerationRequest) -> String {
        let source = String(data: request.sourceData, encoding: .utf8)?.trimmingCharacters(in: .newlines) ?? ""
        return Data(SHA256.hash(data: Data(source.utf8))).base64EncodedString()
    }

    private static func read(_ request: GenerationRequest) throws -> Record {
        let path = url(request)
        guard FileManager.default.fileExists(atPath: path.path) else {
            return Record(sourceHash: hash(request), model: request.model.rawValue, format: request.format.rawValue, outputs: [:])
        }
        let record = try JSONDecoder().decode(Record.self, from: Data(contentsOf: path))
        guard record.sourceHash == hash(request), record.model == request.model.rawValue, record.format == request.format.rawValue else {
            throw MusicError.validation("The generated-title record belongs to another request. Choose a base filename or output folder.")
        }
        for (key, name) in record.outputs {
            guard let index = Int(key), (1...10).contains(index), !name.isEmpty,
                  URL(fileURLWithPath: name).lastPathComponent == name,
                  name.lowercased().hasSuffix(".\(request.format.fileExtension)") else {
                throw MusicError.validation("The generated-title record contains an unsafe output path.")
            }
        }
        return record
    }

    static func resolvedOutputURL(_ request: GenerationRequest, index: Int) throws -> URL {
        let record = try read(request)
        guard let name = record.outputs[String(index)] else {
            let suffix = request.variations == 1 ? "" : "_v\(index)"
            return request.outputFolder.appendingPathComponent("Lyrics", isDirectory: true)
                .appendingPathComponent(GenerationRequest.suggestedStem(request.prompt) + ".pending" + suffix + ".\(request.format.fileExtension)")
        }
        return request.outputFolder.appendingPathComponent(name)
    }

    static func chooseOutputURL(_ request: GenerationRequest, title: String, index: Int) throws -> URL {
        let record = try read(request)
        let stem = GenerationRequest.safeStem(title.isEmpty ? request.prompt : title)
        let prefix = index < 10 ? "0\(index) - " : "\(index) - "
        for attempt in 1...1000 {
            let collision = attempt == 1 ? "" : " (\(attempt))"
            let name = "\(prefix)\(stem)\(collision).\(request.format.fileExtension)"
            let path = request.outputFolder.appendingPathComponent(name)
            if !FileManager.default.fileExists(atPath: path.path) &&
                !record.outputs.contains(where: { $0.key != String(index) && $0.value.caseInsensitiveCompare(name) == .orderedSame }) {
                return path
            }
        }
        throw MusicError.validation("Could not find a free filename for the returned song title.")
    }

    static func reserve(_ request: GenerationRequest, index: Int, output: URL) throws {
        guard output.deletingLastPathComponent().standardizedFileURL == request.outputFolder.standardizedFileURL else {
            throw MusicError.validation("The generated title is outside the output folder.")
        }
        var record = try read(request)
        record.outputs[String(index)] = output.lastPathComponent
        let path = url(request)
        try FileManager.default.createDirectory(at: path.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONEncoder().encode(record).write(to: path, options: .atomic)
    }
}
