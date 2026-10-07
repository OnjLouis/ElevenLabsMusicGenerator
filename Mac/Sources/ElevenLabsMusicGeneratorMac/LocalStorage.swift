import Foundation
import Security

struct AppPreferences: Codable {
    var outputFolder: String = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Music/ElevenLabs Music Generator", isDirectory: true).path
    var durationSeconds = 60
    var variations = 2
    var instrumental = false
    var model = MusicModel.v25.rawValue
    var format = AudioFormat.wav.rawValue
    var includeDetails = true
    var autoPlayGenerations: Bool? = false
    var playbackDevice: String?
    var autoUpdateOnLaunch: Bool? = true
    var installUpdatesSilently: Bool? = false
    var soundEffects: SoundEffectOptions?
    var effects: SoundEffectOptions {
        get { soundEffects ?? SoundEffectOptions() }
        set { soundEffects = newValue }
    }

    static func load() -> AppPreferences {
        guard let data = UserDefaults.standard.data(forKey: "preferences"),
              let decoded = try? JSONDecoder().decode(AppPreferences.self, from: data) else { return AppPreferences() }
        return decoded
    }

    func save() {
        if let data = try? JSONEncoder().encode(self) { UserDefaults.standard.set(data, forKey: "preferences") }
    }
}

enum KeychainStore {
    private static let service = "me.onj.ElevenLabsMusicGeneratorMac"
    private static let account = "ElevenLabsAPIKey"

    static func read() throws -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else {
            throw MusicError.response("Could not read the API key from Keychain (\(status)).")
        }
        return String(data: data, encoding: .utf8)
    }

    static func write(_ key: String) throws {
        let clean = key.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { throw MusicError.validation("Enter an API key.") }
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                    kSecAttrService as String: service, kSecAttrAccount as String: account]
        let update: [String: Any] = [kSecValueData as String: Data(clean.utf8)]
        var status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var item = query
            item[kSecValueData as String] = Data(clean.utf8)
            status = SecItemAdd(item as CFDictionary, nil)
        }
        guard status == errSecSuccess else { throw MusicError.response("Could not save the API key to Keychain (\(status)).") }
    }

    static func delete() throws {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                    kSecAttrService as String: service, kSecAttrAccount as String: account]
        let status = SecItemDelete(query as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw MusicError.response("Could not remove the API key from Keychain (\(status)).")
        }
    }
}

struct PromptDrafts: Codable, Equatable {
    let version: Int
    var music: String
    var soundEffects: String

    init(music: String = "", soundEffects: String = "") {
        version = 1
        self.music = music
        self.soundEffects = soundEffects
    }
}

enum DraftStore {
    private static var folder: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ElevenLabs Music Generator", isDirectory: true)
    }

    static func loadPrompts(activeEffects: Bool, from directory: URL? = nil) throws -> PromptDrafts {
        let directory = directory ?? folder
        let path = directory.appendingPathComponent("Prompt Drafts.json")
        if FileManager.default.fileExists(atPath: path.path) { return try readPrompts(at: path) }
        let active = try readLegacy("Prompt Draft.txt", from: directory)
        let music = try readLegacy("Music Prompt Draft.txt", from: directory)
        let effects = try readLegacy("Sound Effects Prompt Draft.txt", from: directory)
        let drafts = PromptDrafts(
            music: activeEffects ? (music ?? "") : (active ?? music ?? ""),
            soundEffects: activeEffects ? (active ?? effects ?? "") : (effects ?? ""))
        if active != nil || music != nil || effects != nil { try savePrompts(drafts, to: directory) }
        return drafts
    }

    static func savePrompts(_ drafts: PromptDrafts, to directory: URL? = nil) throws {
        let directory = directory ?? folder
        let path = directory.appendingPathComponent("Prompt Drafts.json")
        if FileManager.default.fileExists(atPath: path.path) { _ = try readPrompts(at: path) }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try JSONEncoder().encode(drafts).write(to: path, options: .atomic)
        guard try readPrompts(at: path) == drafts else {
            throw MusicError.response("The saved prompt drafts did not match the edited text.")
        }
        for name in ["Prompt Draft.txt", "Music Prompt Draft.txt", "Sound Effects Prompt Draft.txt"] {
            try? FileManager.default.removeItem(at: directory.appendingPathComponent(name))
        }
    }

    private static func readLegacy(_ name: String, from directory: URL) throws -> String? {
        let path = directory.appendingPathComponent(name)
        return FileManager.default.fileExists(atPath: path.path) ? try String(contentsOf: path, encoding: .utf8) : nil
    }

    private static func readPrompts(at path: URL) throws -> PromptDrafts {
        let drafts = try JSONDecoder().decode(PromptDrafts.self, from: Data(contentsOf: path))
        guard drafts.version == 1 else { throw MusicError.response("The prompt drafts file uses an unsupported format.") }
        return drafts
    }

    static func loadPlan() -> CompositionPlan? {
        guard let data = try? Data(contentsOf: folder.appendingPathComponent("Composition Draft.json")) else { return nil }
        return try? JSONDecoder().decode(CompositionPlan.self, from: data)
    }

    static func savePlan(_ plan: CompositionPlan?) {
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let path = folder.appendingPathComponent("Composition Draft.json")
        if let plan, let data = try? JSONEncoder().encode(plan) { try? data.write(to: path, options: .atomic) }
        else { try? FileManager.default.removeItem(at: path) }
    }
}

enum BatchPlanner {
    static func pending(_ request: GenerationRequest) throws -> [Int] {
        try request.validate()
        let fm = FileManager.default
        let paths = try (1...request.variations).map { try request.resolvedOutputURL($0) }
        let existing = (1...request.variations).filter { fm.fileExists(atPath: paths[$0 - 1].path) }
        if !existing.isEmpty {
            guard let source = try? Data(contentsOf: request.promptURL),
                  matchesSource(source, request.sourceData, isJSON: request.promptURL.pathExtension.lowercased() == "json") else {
                throw MusicError.validation("Existing tracks cannot be resumed because their saved prompt or plan does not match. Choose a new base filename.")
            }
            for index in existing {
                let url = paths[index - 1]
                let data = try Data(contentsOf: url, options: .mappedIfSafe)
                guard matchesAudio(data, request: request) else {
                    throw MusicError.validation("Existing track \(url.lastPathComponent) does not match the selected format. Choose a new base filename.")
                }
            }
        }
        return (1...request.variations).filter { !existing.contains($0) }
    }

    private static func matchesSource(_ saved: Data, _ current: Data, isJSON: Bool) -> Bool {
        if saved == current { return true }
        guard isJSON,
              let left = (try? JSONSerialization.jsonObject(with: saved)) as? [String: Any],
              let right = (try? JSONSerialization.jsonObject(with: current)) as? [String: Any] else { return false }
        return NSDictionary(dictionary: left).isEqual(to: right)
    }

    private static func matchesAudio(_ data: Data, request: GenerationRequest) -> Bool {
        if request.format == .wav {
            guard data.count >= 44, String(data: data[0..<4], encoding: .ascii) == "RIFF",
                  String(data: data[8..<12], encoding: .ascii) == "WAVE" else { return false }
            let rate = data[24..<28].enumerated().reduce(UInt32(0)) { $0 | (UInt32($1.element) << ($1.offset * 8)) }
            if request.isSoundEffect && request.effects.automaticDuration { return rate == 44_100 && data.count > 44 && (data.count - 44).isMultiple(of: 4) }
            let expected = request.isSoundEffect ? Int(request.effects.duration * 1000) : request.plan?.totalMilliseconds ?? request.durationSeconds * 1000
            let actual = (data.count - 44) / (44_100 * 4)
            return rate == 44_100 && abs(actual - expected / 1000) <= 5
        }
        guard data.count > 128 else { return false }
        return data.starts(with: Data("ID3".utf8)) || (data[0] == 0xff && data[1] & 0xe0 == 0xe0)
    }
}
