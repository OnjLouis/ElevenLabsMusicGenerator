import Foundation

enum MusicModel: String, CaseIterable, Identifiable {
    case soundEffects = "eleven_text_to_sound_v2"
    case v25 = "music_v2_5"
    case v2 = "music_v2"
    case v1 = "music_v1"

    var id: String { rawValue }
    var title: String {
        switch self {
        case .v1: "Music v1"
        case .v2: "Music v2"
        case .v25: "Music v2.5"
        case .soundEffects: "Sound Effects v2"
        }
    }
}

struct SoundEffectOptions: Codable {
    var duration = 5.0
    var automaticDuration = true
    var loop = false
    var promptInfluence = 0.3
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
    static let soundEffectsPromptLimit = 450
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
    var effects = SoundEffectOptions()
    var useGeneratedTitle = false

    var isSoundEffect: Bool { model == .soundEffects }
    func confirmationIntro(pendingCount: Int) -> String {
        let kind = isSoundEffect ? "sound effect" : "music track"
        let remaining = pendingCount < variations ? " remaining " : " "
        let plural = pendingCount == 1 ? "" : "s"
        let seconds = isSoundEffect ? effects.duration : Double(plan?.totalMilliseconds ?? durationSeconds * 1000) / 1000
        let duration = isSoundEffect && effects.automaticDuration ? "Automatic, up to 30 seconds each." : "\(seconds.formatted(.number.precision(.fractionLength(0...3)))) seconds each."
        return "Generate \(pendingCount)\(remaining)\(kind)\(plural)?\nDuration: \(duration)"
    }
    var endpoint: String { isSoundEffect ? "/v1/sound-generation" : (includeDetails || useGeneratedTitle) ? "/v1/music/detailed" : "/v1/music" }
    var payload: [String: Any] {
        if isSoundEffect {
            var body: [String: Any] = ["model_id": model.rawValue, "text": prompt,
                "loop": effects.loop, "prompt_influence": effects.promptInfluence]
            if !effects.automaticDuration { body["duration_seconds"] = effects.duration }
            return body
        }
        if let plan { return ["model_id": model.rawValue, "composition_plan": plan.payload] }
        return ["model_id": model.rawValue, "prompt": prompt, "music_length_ms": durationSeconds * 1000, "force_instrumental": instrumental]
    }

    var stem: String { baseName.isEmpty ? Self.suggestedStem(prompt) : Self.safeStem(baseName) }
    var promptURL: URL { outputFolder.appendingPathComponent(stem + (isSoundEffect ? ".sfx.json" : plan == nil ? ".txt" : ".plan.json")) }
    var sourceData: Data {
        if isSoundEffect {
            var saved = payload
            saved["duration_seconds"] = effects.automaticDuration ? NSNull() : effects.duration as Any
            saved["output_format"] = format.rawValue
            return (try? JSONSerialization.data(withJSONObject: saved, options: [.prettyPrinted, .sortedKeys])) ?? Data()
        }
        if let plan { return (try? plan.encodedPayload()) ?? Data() }
        return Data((prompt.trimmingCharacters(in: .newlines) + "\n").utf8)
    }
    func outputURL(_ index: Int) -> URL {
        let suffix = variations == 1 ? "" : "_v\(index)"
        return outputFolder.appendingPathComponent("\(stem)\(suffix).\(format.fileExtension)")
    }

    func resolvedOutputURL(_ index: Int) throws -> URL {
        useGeneratedTitle ? try GeneratedTitleManifest.resolvedOutputURL(self, index: index) : outputURL(index)
    }

    func validate() throws {
        guard (1...10).contains(variations) else { throw MusicError.validation("Choose 1 to 10 variations.") }
        if isSoundEffect {
            guard !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, prompt.utf16.count <= Self.soundEffectsPromptLimit else { throw MusicError.validation("Enter a sound effects prompt of no more than 450 characters. The prompt has been kept for editing.") }
            guard effects.duration.isFinite, effects.promptInfluence.isFinite,
                  (0.5...30).contains(effects.duration), (0...1).contains(effects.promptInfluence) else { throw MusicError.validation("Sound effects need 0.5 to 30 seconds and prompt influence between 0 and 1.") }
            guard format == .wav || format == .mp3_44100_128 || format == .mp3_44100_192 else { throw MusicError.validation("Choose a 44.1 kHz WAV or MP3 format for Sound Effects.") }
            return
        }
        if let plan {
            guard model != .v1 else { throw MusicError.validation("Plans require Music v2 or v2.5.") }
            try plan.validate()
        } else {
            guard !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, prompt.utf16.count <= 4100 else {
                throw MusicError.validation("Enter a prompt of no more than 4,100 characters.")
            }
            guard (3...600).contains(durationSeconds) else { throw MusicError.validation("Choose 3 to 600 seconds.") }
        }
    }

    static func readSoundEffect(_ data: Data) throws -> (prompt: String, effects: SoundEffectOptions, format: AudioFormat) {
        guard let saved = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              saved["model_id"] as? String == MusicModel.soundEffects.rawValue,
              let prompt = saved["text"] as? String,
              let loop = saved["loop"] as? Bool,
              let influence = saved["prompt_influence"] as? Double,
              let rawFormat = saved["output_format"] as? String,
              let format = AudioFormat(rawValue: rawFormat) else { throw MusicError.validation("This is not a saved sound effects prompt.") }
        let seconds = saved["duration_seconds"] as? Double
        guard saved["duration_seconds"] is NSNull || seconds != nil else {
            throw MusicError.validation("The saved sound effect duration is invalid.")
        }
        let effects = SoundEffectOptions(duration: seconds ?? 5, automaticDuration: seconds == nil, loop: loop, promptInfluence: influence)
        let request = GenerationRequest(prompt: prompt, baseName: "", outputFolder: URL(fileURLWithPath: "/"), durationSeconds: 5, variations: 1, instrumental: false, model: .soundEffects, format: format, includeDetails: false, plan: nil, effects: effects)
        try request.validate()
        return (prompt, effects, format)
    }

    static func safeStem(_ value: String) -> String {
        let forbidden = CharacterSet(charactersIn: "<>:\"/\\|?*").union(.controlCharacters)
        let words = value.components(separatedBy: forbidden.union(.whitespacesAndNewlines)).filter { !$0.isEmpty }
        var result = String(words.joined(separator: " ").prefix(80)).trimmingCharacters(in: CharacterSet(charactersIn: " ."))
        if result.isEmpty { result = "ElevenLabs Music" }
        let reserved = ["CON", "PRN", "AUX", "NUL"] + (1...9).flatMap { ["COM\($0)", "LPT\($0)"] }
        if reserved.contains(result.uppercased()) { result += "_" }
        return result
    }

    static func suggestedStem(_ prompt: String) -> String {
        safeStem(prompt.components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }.prefix(8).joined(separator: " "))
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
        prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "" : GenerationRequest.suggestedStem(prompt)
    }
}
