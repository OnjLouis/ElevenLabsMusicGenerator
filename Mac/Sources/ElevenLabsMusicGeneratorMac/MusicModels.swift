import Foundation

enum MusicModel: String, CaseIterable, Identifiable {
    case v1 = "music_v1"
    case v2 = "music_v2"
    case v25 = "music_v2_5"

    var id: String { rawValue }
    var title: String {
        switch self {
        case .v1: "Music v1"
        case .v2: "Music v2"
        case .v25: "Music v2.5"
        }
    }
}

enum AudioFormat: String, CaseIterable, Identifiable {
    case wav = "pcm_44100"
    case mp3_44100_128
    case mp3_44100_192
    case mp3_48000_192
    case mp3_48000_240
    case mp3_48000_320

    var id: String { rawValue }
    var fileExtension: String { self == .wav ? "wav" : "mp3" }
    var title: String {
        switch self {
        case .wav: "WAV, 44.1 kHz PCM"
        case .mp3_44100_128: "MP3, 44.1 kHz, 128 kbps"
        case .mp3_44100_192: "MP3, 44.1 kHz, 192 kbps"
        case .mp3_48000_192: "MP3, 48 kHz, 192 kbps"
        case .mp3_48000_240: "MP3, 48 kHz, 240 kbps"
        case .mp3_48000_320: "MP3, 48 kHz, 320 kbps"
        }
    }
}

struct MusicSection: Identifiable, Codable, Equatable {
    var id = UUID()
    var name = "New section"
    var body = ""
    var durationMilliseconds = 15_000
    var positiveStyles = ""
    var negativeStyles = ""
    var contextAdherence = "high"

    var payload: [String: Any] {
        [
            "text": "[\(name.trimmingCharacters(in: .whitespacesAndNewlines))]" + (body.isEmpty ? "" : "\n" + body.trimmingCharacters(in: .whitespacesAndNewlines)),
            "duration_ms": durationMilliseconds,
            "positive_styles": Self.styleLines(positiveStyles),
            "negative_styles": Self.styleLines(negativeStyles),
            "context_adherence": contextAdherence
        ]
    }

    static func styleLines(_ value: String) -> [String] {
        value.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }
    }
}

struct CompositionPlan: Codable, Equatable {
    var sections: [MusicSection] = []

    var totalMilliseconds: Int { sections.reduce(0) { $0 + $1.durationMilliseconds } }
    var payload: [String: Any] { ["chunks": sections.map(\.payload)] }

    func validate() throws {
        guard (1...30).contains(sections.count) else { throw MusicError.validation("A plan needs 1 to 30 sections.") }
        for section in sections {
            let name = section.name.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !name.isEmpty, !name.contains("["), !name.contains("]"), !name.contains("\n") else {
                throw MusicError.validation("Each section needs a name without brackets or line breaks.")
            }
            guard (3_000...120_000).contains(section.durationMilliseconds) else {
                throw MusicError.validation("Each section must last between 3 and 120 seconds.")
            }
            guard ["low", "medium", "high"].contains(section.contextAdherence) else {
                throw MusicError.validation("Context adherence must be low, medium, or high.")
            }
            guard MusicSection.styleLines(section.positiveStyles).count <= 50,
                  MusicSection.styleLines(section.negativeStyles).count <= 50 else {
                throw MusicError.validation("A section cannot contain more than 50 included or excluded styles.")
            }
            guard (section.payload["text"] as? String ?? "").count <= 6000 else {
                throw MusicError.validation("Section text must not exceed 6,000 characters.")
            }
        }
        guard (3_000...600_000).contains(totalMilliseconds) else {
            throw MusicError.validation("The complete plan must last between 3 seconds and 10 minutes.")
        }
    }

    func encodedPayload() throws -> Data {
        try validate()
        return try JSONSerialization.data(withJSONObject: payload, options: [.prettyPrinted, .sortedKeys])
    }

    static func decodePayload(_ data: Data) throws -> CompositionPlan {
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw MusicError.validation("This JSON file does not contain a reusable v2 or v2.5 composition plan.")
        }
        let payload = root["composition_plan"] as? [String: Any] ?? root
        guard let chunks = payload["chunks"] as? [[String: Any]] else {
            throw MusicError.validation("This JSON file does not contain a reusable v2 or v2.5 composition plan.")
        }
        var plan = CompositionPlan()
        for chunk in chunks {
            guard let text = chunk["text"] as? String,
                  text.first == "[", let close = text.firstIndex(of: "]"),
                  let duration = chunk["duration_ms"] as? Int else {
                throw MusicError.validation("A plan section is incomplete or unsupported.")
            }
            let name = String(text[text.index(after: text.startIndex)..<close])
            let body = String(text[text.index(after: close)...]).trimmingCharacters(in: .newlines)
            plan.sections.append(MusicSection(name: name, body: body, durationMilliseconds: duration,
                positiveStyles: (chunk["positive_styles"] as? [String] ?? []).joined(separator: "\n"),
                negativeStyles: (chunk["negative_styles"] as? [String] ?? []).joined(separator: "\n"),
                contextAdherence: chunk["context_adherence"] as? String ?? "high"))
        }
        try plan.validate()
        return plan
    }
}

enum MusicError: LocalizedError {
    case validation(String)
    case service(Int, String)
    case response(String)

    var errorDescription: String? {
        switch self {
        case .validation(let text), .response(let text): text
        case .service(let status, let text): "ElevenLabs returned HTTP \(status): \(text)"
        }
    }
}

struct GenerationRequest {
    var prompt: String
    var baseName: String
    var outputFolder: URL
    var durationSeconds: Int
    var variations: Int
    var instrumental: Bool
    var model: MusicModel
    var format: AudioFormat
    var includeDetails: Bool
    var plan: CompositionPlan?

    var stem: String { Self.safeStem(baseName.isEmpty ? prompt : baseName) }
    var promptURL: URL { outputFolder.appendingPathComponent(stem + (plan == nil ? ".txt" : ".plan.json")) }
    var sourceData: Data {
        if let plan { return (try? plan.encodedPayload()) ?? Data() }
        return Data((prompt.trimmingCharacters(in: .newlines) + "\n").utf8)
    }
    func outputURL(_ index: Int) -> URL {
        let suffix = variations == 1 ? "" : "_v\(index)"
        return outputFolder.appendingPathComponent("\(stem)\(suffix).\(format.fileExtension)")
    }

    func validate() throws {
        guard (1...10).contains(variations) else { throw MusicError.validation("Choose 1 to 10 variations.") }
        if let plan {
            guard model != .v1 else { throw MusicError.validation("Plans require Music v2 or v2.5.") }
            try plan.validate()
        } else {
            guard !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, prompt.count <= 4100 else {
                throw MusicError.validation("Enter a prompt of no more than 4,100 characters.")
            }
            guard (3...600).contains(durationSeconds) else { throw MusicError.validation("Choose 3 to 600 seconds.") }
        }
    }

    static func safeStem(_ value: String) -> String {
        let ascii = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
        let words = value.components(separatedBy: ascii.inverted).filter { !$0.isEmpty }
        let result = words.prefix(8).joined(separator: "_")
        return String((result.isEmpty ? "ElevenLabs_Music" : result).prefix(80))
    }
}

struct FilenameSuggestionState {
    private(set) var value: String
    private(set) var isAutomatic = true

    init(prompt: String) {
        value = Self.suggestion(for: prompt)
    }

    mutating func promptChanged(_ prompt: String) {
        if isAutomatic { value = Self.suggestion(for: prompt) }
    }

    mutating func userChanged(_ name: String) {
        value = name
        isAutomatic = false
    }

    mutating func openedDocument(named name: String) {
        value = name
        isAutomatic = true
    }

    mutating func reset() {
        value = ""
        isAutomatic = true
    }

    private static func suggestion(for prompt: String) -> String {
        prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "" : GenerationRequest.safeStem(prompt)
    }
}
