import Foundation
import XCTest
@testable import ElevenLabsMusicGeneratorMac

final class MusicTests: XCTestCase {
    func testPromptDraftMigrationAndRoundTrip() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("elevenlabs-drafts-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        try Data("Current effect".utf8).write(to: folder.appendingPathComponent("Prompt Draft.txt"))
        try Data("Saved music".utf8).write(to: folder.appendingPathComponent("Music Prompt Draft.txt"))
        try Data("Old effect".utf8).write(to: folder.appendingPathComponent("Sound Effects Prompt Draft.txt"))
        var drafts = try DraftStore.loadPrompts(activeEffects: true, from: folder)
        XCTAssertEqual(drafts.music, "Saved music")
        XCTAssertEqual(drafts.soundEffects, "Current effect")
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("Prompt Drafts.json").path))
        for name in ["Prompt Draft.txt", "Music Prompt Draft.txt", "Sound Effects Prompt Draft.txt"] {
            XCTAssertFalse(FileManager.default.fileExists(atPath: folder.appendingPathComponent(name).path))
        }
        drafts.music = "Edited music"
        drafts.soundEffects = "Edited effect"
        try DraftStore.savePrompts(drafts, to: folder)
        XCTAssertEqual(try DraftStore.loadPrompts(activeEffects: false, from: folder), drafts)
        try Data("{bad json".utf8).write(to: folder.appendingPathComponent("Prompt Drafts.json"))
        try Data("Keep this".utf8).write(to: folder.appendingPathComponent("Prompt Draft.txt"))
        XCTAssertThrowsError(try DraftStore.savePrompts(drafts, to: folder))
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("Prompt Draft.txt").path))
    }

    func testModelDisplayOrder() {
        XCTAssertEqual(MusicModel.allCases, [.soundEffects, .v25, .v2, .v1])
        XCTAssertEqual(AppPreferences().model, MusicModel.v25.rawValue)
    }
    func testConfirmationWording() {
        var request = GenerationRequest(prompt: "A door", baseName: "Door", outputFolder: URL(fileURLWithPath: "/tmp"), durationSeconds: 60, variations: 3, instrumental: false, model: .soundEffects, format: .wav, includeDetails: false, plan: nil)
        XCTAssertEqual(request.confirmationIntro(pendingCount: 3), "Generate 3 sound effects?\nDuration: Automatic, up to 30 seconds each.")
        XCTAssertTrue(request.confirmationIntro(pendingCount: 1).hasPrefix("Generate 1 remaining sound effect?"))
        request.model = .v25
        XCTAssertEqual(request.confirmationIntro(pendingCount: 3), "Generate 3 music tracks?\nDuration: 60 seconds each.")
    }
    func testSoundEffectsPayloadAndSavedPrompt() throws {
        var input = GenerationRequest(prompt: "A wooden door closing", baseName: "Door", outputFolder: URL(fileURLWithPath: "/tmp"), durationSeconds: 60, variations: 2, instrumental: true, model: .soundEffects, format: .wav, includeDetails: true, plan: nil)
        input.effects = SoundEffectOptions(duration: 0.5, automaticDuration: false, loop: true, promptInfluence: 0.7)
        try input.validate()
        XCTAssertEqual(input.endpoint, "/v1/sound-generation")
        XCTAssertEqual(input.payload["duration_seconds"] as? Double, 0.5)
        XCTAssertEqual(input.payload["loop"] as? Bool, true)
        XCTAssertNil(input.payload["force_instrumental"])
        XCTAssertNil(input.payload["music_length_ms"])
        let saved = try GenerationRequest.readSoundEffect(input.sourceData)
        XCTAssertTrue(String(data: input.sourceData, encoding: .utf8)!.contains("\n"))
        XCTAssertEqual(saved.prompt, input.prompt)
        XCTAssertEqual(saved.effects.duration, 0.5)
        XCTAssertTrue(saved.effects.loop)
        input.effects.automaticDuration = true
        XCTAssertNil(input.payload["duration_seconds"])
        XCTAssertTrue(try GenerationRequest.readSoundEffect(input.sourceData).effects.automaticDuration)
        input.format = .mp3_48000_192
        XCTAssertThrowsError(try input.validate())
    }

    func testSoundEffectPromptLengthLimit() throws {
        var input = GenerationRequest(prompt: String(repeating: "a", count: 450), baseName: "Test", outputFolder: URL(fileURLWithPath: "/tmp"), durationSeconds: 5, variations: 1, instrumental: false, model: .soundEffects, format: .wav, includeDetails: false, plan: nil)
        try input.validate()
        input.prompt += "b"
        XCTAssertThrowsError(try input.validate())
        input.model = .v25
        try input.validate()
    }

    func testExistingPreferencesSurviveNewSoundEffectsSettings() throws {
        let old = #"{"outputFolder":"/Music/Test","durationSeconds":90,"variations":3,"instrumental":true,"model":"music_v2_5","format":"pcm_44100","includeDetails":true}"#
        var prefs = try JSONDecoder().decode(AppPreferences.self, from: Data(old.utf8))
        XCTAssertEqual(prefs.durationSeconds, 90)
        XCTAssertEqual(prefs.outputFolder, "/Music/Test")
        XCTAssertEqual(prefs.effects.duration, 5)
        prefs.effects.loop = true
        let restored = try JSONDecoder().decode(AppPreferences.self, from: JSONEncoder().encode(prefs))
        XCTAssertTrue(restored.effects.loop)
        XCTAssertEqual(restored.durationSeconds, 90)
    }
    func testMacReleaseCheckSelectsOnlyNewerPublishedMacPackage() throws {
        let json = """
        [
          {"tag_name":"v9.0.0","draft":true,"prerelease":false,"html_url":"https://github.com/OnjLouis/ElevenLabsMusicGenerator/releases/tag/v9.0.0","assets":[{"name":"ElevenLabs-Music-Generator-Mac-9.0.0.zip"}]},
          {"tag_name":"v3.0.0","draft":false,"prerelease":true,"html_url":"https://github.com/OnjLouis/ElevenLabsMusicGenerator/releases/tag/v3.0.0","assets":[{"name":"ElevenLabs-Music-Generator-Mac-3.0.0.zip"}]},
          {"tag_name":"v2.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/OnjLouis/ElevenLabsMusicGenerator/releases/tag/v2.0.0","assets":[{"name":"ElevenLabsMusicGenerator.zip"}]},
          {"tag_name":"v1.2.0","draft":false,"prerelease":false,"html_url":"https://github.com/OnjLouis/ElevenLabsMusicGenerator/releases/tag/v1.2.0","assets":[{"name":"ElevenLabs-Music-Generator-Mac-1.2.0.zip"}]},
          {"tag_name":"v1.1.0","draft":false,"prerelease":false,"html_url":"https://github.com/OnjLouis/ElevenLabsMusicGenerator/releases/tag/v1.1.0","assets":[{"name":"ElevenLabs-Music-Generator-Mac-1.1.0.zip"}]}
        ]
        """
        let data = Data(json.utf8)
        XCTAssertEqual(try ReleaseCatalog.newerMacRelease(in: data, currentVersion: "1.0.0", architecture: .appleSilicon)?.version, "1.2.0")
        XCTAssertNil(try ReleaseCatalog.newerMacRelease(in: data, currentVersion: "1.2.0", architecture: .appleSilicon))
        XCTAssertThrowsError(try ReleaseCatalog.newerMacRelease(in: Data("{}".utf8), currentVersion: "1.0.0", architecture: .appleSilicon))
    }

    func testIntelReleaseCheckRequiresIntelPackage() throws {
        let json = """
        [
          {"tag_name":"v1.3.0","draft":false,"prerelease":false,"html_url":"https://github.com/OnjLouis/ElevenLabsMusicGenerator/releases/tag/v1.3.0","assets":[{"name":"ElevenLabs-Music-Generator-Mac-1.3.0.zip"}]},
          {"tag_name":"v1.2.0","draft":false,"prerelease":false,"html_url":"https://github.com/OnjLouis/ElevenLabsMusicGenerator/releases/tag/v1.2.0","assets":[{"name":"ElevenLabs-Music-Generator-Mac-Intel-1.2.0.zip"}]}
        ]
        """
        let data = Data(json.utf8)
        XCTAssertEqual(try ReleaseCatalog.newerMacRelease(in: data, currentVersion: "1.0.0", architecture: .intel)?.version, "1.2.0")
        XCTAssertNil(try ReleaseCatalog.newerMacRelease(in: data, currentVersion: "1.2.0", architecture: .intel))
        XCTAssertEqual(try ReleaseCatalog.newerMacRelease(in: data, currentVersion: "1.0.0", architecture: .appleSilicon)?.version, "1.3.0")
    }

    func testDefaultPackageMatchesBuildArchitecture() {
        #if arch(x86_64)
        XCTAssertEqual(MacPackageArchitecture.current.assetName(version: "1.0.0"), "ElevenLabs-Music-Generator-Mac-Intel-1.0.0.zip")
        #else
        XCTAssertEqual(MacPackageArchitecture.current.assetName(version: "1.0.0"), "ElevenLabs-Music-Generator-Mac-1.0.0.zip")
        #endif
    }

    private final class StubProtocol: URLProtocol {
        static var reply: ((URLRequest) -> (HTTPURLResponse, Data))?
        override class func canInit(with request: URLRequest) -> Bool { true }
        override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
        override func startLoading() {
            let (response, data) = Self.reply!(request)
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        }
        override func stopLoading() {}
    }

    func testVisibleFilenameTracksPromptUntilEdited() {
        var filename = FilenameSuggestionState(prompt: "Gentle piano with soft strings")
        XCTAssertEqual(filename.value, "Gentle piano with soft strings")
        filename.promptChanged("Bright jazz piano")
        XCTAssertEqual(filename.value, "Bright jazz piano")
        filename.userChanged("My own title")
        filename.promptChanged("A completely different prompt")
        XCTAssertEqual(filename.value, "My own title")
        filename.reset()
        XCTAssertEqual(filename.value, "")
        filename.promptChanged("Fresh start")
        XCTAssertEqual(filename.value, "Fresh start")
        filename.openedDocument(named: "Saved Song")
        XCTAssertEqual(filename.value, "Saved Song")
    }

    func testSafeFilenameKeepsSpaces() {
        XCTAssertEqual(GenerationRequest.safeStem("My new track"), "My new track")
        XCTAssertEqual(GenerationRequest.safeStem(" My: new / track? "), "My new track")
        XCTAssertNotEqual(GenerationRequest.safeStem("CON"), "CON")
    }

    func testPromptPasteDoesNotDeleteExistingTail() {
        XCTAssertEqual(PromptInputLimiter.replacement("abcXYZ", range: NSRange(location: 3, length: 0),
            with: "12345", limit: 8), "12")
        XCTAssertNil(PromptInputLimiter.replacement("abcXYZ", range: NSRange(location: 3, length: 0),
            with: "12", limit: 8))
        XCTAssertEqual(PromptInputLimiter.replacement("abcXYZ", range: NSRange(location: 3, length: 3),
            with: "123456", limit: 8), "12345")
        XCTAssertEqual(PromptInputLimiter.replacement("abc", range: NSRange(location: 3, length: 0),
            with: "🙂x", limit: 4), "")
        XCTAssertEqual(PromptInputLimiter.replacement("abc", range: NSRange(location: 3, length: 0),
            with: "🙂x", limit: 5), "🙂")
    }

    func testNewPreferencesEnableStartupChecks() throws {
        XCTAssertEqual(AppPreferences().autoUpdateOnLaunch, true)
        let old = #"{"outputFolder":"/tmp/Music","durationSeconds":60,"variations":2,"instrumental":false,"model":"music_v2_5","format":"pcm_44100","includeDetails":true}"#
        XCTAssertNil(try JSONDecoder().decode(AppPreferences.self, from: Data(old.utf8)).autoUpdateOnLaunch)
    }

    func testKeyUsesNoCreditMusicPlanEndpoint() async throws {
        defer { StubProtocol.reply = nil }
        StubProtocol.reply = { request in
            XCTAssertEqual(request.url?.path, "/v1/music/plan")
            XCTAssertEqual(request.httpMethod, "POST")
            XCTAssertEqual(request.value(forHTTPHeaderField: "xi-api-key"), "music-only-key")
            let bodyData: Data
            if let data = request.httpBody {
                bodyData = data
            } else if let stream = request.httpBodyStream {
                stream.open()
                defer { stream.close() }
                var data = Data()
                var buffer = [UInt8](repeating: 0, count: 4096)
                while stream.hasBytesAvailable {
                    let count = stream.read(&buffer, maxLength: buffer.count)
                    if count <= 0 { break }
                    data.append(contentsOf: buffer.prefix(count))
                }
                bodyData = data
            } else {
                XCTFail("Expected a JSON request body")
                return (HTTPURLResponse(url: request.url!, statusCode: 400, httpVersion: nil,
                    headerFields: nil)!, Data())
            }
            let body = try! JSONSerialization.jsonObject(with: bodyData) as! [String: Any]
            XCTAssertEqual(body["model_id"] as? String, "music_v2_5")
            XCTAssertEqual(body["music_length_ms"] as? Int, 3000)
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil,
                headerFields: nil)!, Data("{}".utf8))
        }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        let result = try await MusicService(session: URLSession(configuration: configuration)).testKey("music-only-key")
        XCTAssertTrue(result.contains("Music API key accepted"))
    }

    func testSubscriptionBalanceRequestAndFormatting() async throws {
        defer { StubProtocol.reply = nil }
        StubProtocol.reply = { request in
            XCTAssertEqual(request.url?.path, "/v1/user/subscription")
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.value(forHTTPHeaderField: "xi-api-key"), "test-key")
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!,
                Data(#"{"character_count":2224,"character_limit":64422,"next_character_count_reset_unix":1790796665}"#.utf8))
        }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        let balance = try await MusicService(session: URLSession(configuration: configuration)).subscriptionBalance("test-key")
        XCTAssertEqual(balance.remaining, 62198)
        XCTAssertTrue(balance.display(now: Date(timeIntervalSince1970: 1790793065)).contains("1 hour"))
        XCTAssertEqual(try SubscriptionBalance.decode(Data(#"{"character_count":12,"character_limit":10,"next_character_count_reset_unix":null}"#.utf8)).remaining, 0)
        let overage = try SubscriptionBalance.decode(Data(#"{"character_count":244469,"character_limit":114089,"can_extend_character_limit":true,"max_credit_limit_extension":"unlimited","current_overage":{"amount":"39.11","currency":"usd"}}"#.utf8)).display()
        XCTAssertTrue(overage.contains("130,380"))
        XCTAssertTrue(overage.contains("USD 39.11"))
        XCTAssertTrue(overage.contains("Usage-based billing is enabled"))
        XCTAssertTrue(overage.contains("total spendable balance is not available"))
        XCTAssertTrue(try SubscriptionBalance.decode(Data(#"{"character_count":0,"character_limit":10}"#.utf8)).display().contains("unavailable"))
        XCTAssertThrowsError(try SubscriptionBalance.decode(Data(#"{"character_count":4}"#.utf8)))
    }

    func testCompositionPlanRoundTrip() throws {
        let original = CompositionPlan(sections: [MusicSection(name: "Verse", body: "First line", durationMilliseconds: 12_500,
            positiveStyles: "jazz\nsoft", negativeStyles: "loud", contextAdherence: "high")])
        let recovered = try CompositionPlan.decodePayload(original.encodedPayload())
        XCTAssertEqual(recovered.sections.count, 1)
        XCTAssertEqual(recovered.sections[0].name, "Verse")
        XCTAssertEqual(recovered.sections[0].durationMilliseconds, 12_500)
        XCTAssertEqual(MusicSection.styleLines(recovered.sections[0].positiveStyles), ["jazz", "soft"])
    }

    func testTrackDetailsCanReopenCompositionPlan() throws {
        let details = Data(#"{"song_metadata":{"title":"Test"},"composition_plan":{"chunks":[{"text":"[Verse]\nSing again","duration_ms":12500,"positive_styles":["warm piano"]}]}}"#.utf8)
        let plan = try CompositionPlan.decodePayload(details)
        XCTAssertEqual(plan.sections.count, 1)
        XCTAssertEqual(plan.sections[0].body, "Sing again")
        XCTAssertEqual(plan.sections[0].durationMilliseconds, 12_500)
        XCTAssertEqual(plan.sections[0].positiveStyles, "warm piano")
        XCTAssertThrowsError(try CompositionPlan.decodePayload(Data(#"{"song_metadata":{"title":"No plan"}}"#.utf8))) { error in
            XCTAssertTrue(error.localizedDescription.contains("composition plan"))
        }
    }

    func testResumeKeepsExistingTrack() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let request = GenerationRequest(prompt: "A piano song", baseName: "Test", outputFolder: folder,
            durationSeconds: 10, variations: 2, instrumental: false, model: .v25,
            format: .mp3_44100_128, includeDetails: false, plan: nil)
        var mp3 = Data("ID3".utf8)
        mp3.append(Data(repeating: 0, count: 200))
        try mp3.write(to: request.outputURL(1))
        try request.sourceData.write(to: request.promptURL)
        XCTAssertEqual(try BatchPlanner.pending(request), [2])
        var changed = request
        changed.prompt = "A different piano song"
        XCTAssertThrowsError(try BatchPlanner.pending(changed))
    }

    func testLegacyCompactPlanResumesWithoutChangingLyrics() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let plan = CompositionPlan(sections: [MusicSection(name: "Verse", body: "First line\nSecond line",
            durationMilliseconds: 3_000)])
        let compact = try JSONSerialization.data(withJSONObject: plan.payload, options: [.sortedKeys])
        var request = GenerationRequest(prompt: "", baseName: "Legacy", outputFolder: folder,
            durationSeconds: 3, variations: 2, instrumental: false, model: .v25,
            format: .mp3_44100_128, includeDetails: false, plan: plan)
        var mp3 = Data("ID3".utf8)
        mp3.append(Data(repeating: 0, count: 200))
        try mp3.write(to: request.outputURL(1))
        try compact.write(to: request.promptURL)
        XCTAssertTrue(String(data: request.sourceData, encoding: .utf8)!.contains("\n"))
        XCTAssertEqual(try BatchPlanner.pending(request), [2])
        request.plan?.sections[0].body = "Changed lyrics"
        XCTAssertThrowsError(try BatchPlanner.pending(request))
    }

    func testMultipartAudioAndMetadata() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let file = folder.appendingPathComponent("response")
        let payload = "--abc\r\nContent-Type: audio/mpeg\r\n\r\nAUDIO\r\n--abc\r\nContent-Type: application/json\r\n\r\n{\"composition_plan\":{}}\r\n--abc--\r\n"
        try Data(payload.utf8).write(to: file)
        let parsed = try MultipartMusicResponse.parse(file: file, contentType: "multipart/mixed; boundary=abc")
        XCTAssertEqual(String(data: parsed.audio, encoding: .utf8), "AUDIO")
        XCTAssertTrue(parsed.metadata.contains("composition_plan"))
    }

    func testWaveHeader() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let input = folder.appendingPathComponent("raw")
        let output = folder.appendingPathComponent("audio.wav")
        try Data(repeating: 0, count: 40).write(to: input)
        try WaveWriter.wrap(input: input, output: output, sampleRate: 44_100)
        let result = try Data(contentsOf: output)
        XCTAssertEqual(result.count, 84)
        XCTAssertEqual(String(data: result[0..<4], encoding: .ascii), "RIFF")
    }

    func testGenerationWritesAudioAndLyricsWithoutTempFiles() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder); StubProtocol.reply = nil }
        let fixture = "--abc\r\nContent-Type: audio/mpeg\r\n\r\nID3AUDIO\r\n--abc\r\nContent-Type: application/json\r\n\r\n{\"composition_plan\":{\"chunks\":[{\"text\":\"[Verse]\\nHello world\",\"duration_ms\":3000}]}}\r\n--abc--\r\n"
        StubProtocol.reply = { request in
            XCTAssertEqual(request.url?.path, "/v1/music/detailed")
            XCTAssertEqual(request.url?.query, "output_format=mp3_44100_128")
            XCTAssertEqual(request.value(forHTTPHeaderField: "xi-api-key"), "fake-key")
            let body = try! JSONSerialization.jsonObject(with: request.httpBody!) as! [String: Any]
            XCTAssertEqual(body["model_id"] as? String, "music_v2_5")
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil,
                headerFields: ["Content-Type": "multipart/mixed; boundary=abc"])!, Data(fixture.utf8))
        }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        let service = MusicService(session: URLSession(configuration: configuration))
        let request = GenerationRequest(prompt: "A piano song", baseName: "Test", outputFolder: folder,
            durationSeconds: 10, variations: 1, instrumental: false, model: .v25,
            format: .mp3_44100_128, includeDetails: true, plan: nil)
        let result = try await service.generate(request, index: 1, key: "fake-key")
        XCTAssertTrue(FileManager.default.fileExists(atPath: result.url.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: request.promptURL.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: result.detailsURL!.path))
        XCTAssertTrue(try String(contentsOf: result.detailsURL!).contains("\n"))
        let reopenedPlan = try CompositionPlan.decodePayload(Data(contentsOf: result.detailsURL!))
        XCTAssertEqual(reopenedPlan.sections.first?.body, "Hello world")
        XCTAssertTrue(try String(contentsOf: result.lyricsURL!).contains("Hello world"))
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path)
        XCTAssertFalse(names.contains(where: { $0.contains(".part") }))
    }

    func testReturnedTitleNamesAudioAndResumes() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder); StubProtocol.reply = nil }
        let fixture = "--abc\r\nContent-Type: audio/mpeg\r\n\r\nID3" + String(repeating: "A", count: 200) + "\r\n--abc\r\nContent-Type: application/json\r\n\r\n{\"song_metadata\":{\"title\":\"Returned Song\"}}\r\n--abc--\r\n"
        StubProtocol.reply = { request in
            XCTAssertEqual(request.url?.path, "/v1/music/detailed")
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil,
                headerFields: ["Content-Type": "multipart/mixed; boundary=abc"])!, Data(fixture.utf8))
        }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        let service = MusicService(session: URLSession(configuration: configuration))
        var request = GenerationRequest(prompt: "A piano song", baseName: "", outputFolder: folder,
            durationSeconds: 10, variations: 2, instrumental: false, model: .v25,
            format: .mp3_44100_128, includeDetails: false, plan: nil)
        request.useGeneratedTitle = true
        let result = try await service.generate(request, index: 1, key: "fake-key")
        XCTAssertEqual(result.url.lastPathComponent, "01 - Returned Song.mp3")
        XCTAssertNil(result.detailsURL)
        XCTAssertEqual(try BatchPlanner.pending(request), [2])
        var wavRequest = request
        wavRequest.format = .wav
        XCTAssertNotEqual(try request.resolvedOutputURL(1), try wavRequest.resolvedOutputURL(1))
        let wavOutput = try GeneratedTitleManifest.chooseOutputURL(wavRequest, title: "Returned Song", index: 1)
        try GeneratedTitleManifest.reserve(wavRequest, index: 1, output: wavOutput)
        XCTAssertEqual(try request.resolvedOutputURL(1), result.url)
        XCTAssertEqual(try wavRequest.resolvedOutputURL(1), wavOutput)
        let second = try GeneratedTitleManifest.chooseOutputURL(request, title: "Returned Song", index: 2)
        XCTAssertEqual(second.lastPathComponent, "02 - Returned Song.mp3")
        var single = request
        single.prompt = "Single track"
        single.variations = 1
        let collision = try GeneratedTitleManifest.chooseOutputURL(single, title: "Returned Song", index: 1)
        XCTAssertEqual(collision.lastPathComponent, "01 - Returned Song (2).mp3")
        var older = request
        older.prompt = "Older batch"
        let olderURL = folder.appendingPathComponent("Older song_v1.mp3")
        try GeneratedTitleManifest.reserve(older, index: 1, output: olderURL)
        XCTAssertEqual(try older.resolvedOutputURL(1), olderURL)
    }

    func testSoundEffectGenerationAndAutomaticResume() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder); StubProtocol.reply = nil }
        StubProtocol.reply = { request in
            XCTAssertEqual(request.url?.path, "/v1/sound-generation")
            XCTAssertEqual(request.url?.query, "output_format=pcm_44100")
            let body = try! JSONSerialization.jsonObject(with: request.httpBody!) as! [String: Any]
            XCTAssertEqual(body["text"] as? String, "A door closing")
            XCTAssertNil(body["music_length_ms"])
            XCTAssertNil(body["duration_seconds"])
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil,
                headerFields: ["Content-Type": "audio/pcm"])!, Data(repeating: 0, count: 44100 * 4))
        }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        let service = MusicService(session: URLSession(configuration: configuration))
        var request = GenerationRequest(prompt: "A door closing", baseName: "Door", outputFolder: folder,
            durationSeconds: 60, variations: 2, instrumental: true, model: .soundEffects,
            format: .wav, includeDetails: true, plan: nil)
        let result = try await service.generate(request, index: 1, key: "fake-key")
        XCTAssertEqual(try Data(contentsOf: result.url).count, 44100 * 4 + 44)
        XCTAssertNil(result.lyricsURL)
        XCTAssertNil(result.detailsURL)
        XCTAssertTrue(try String(contentsOf: request.promptURL).contains("\n"))
        XCTAssertEqual(try BatchPlanner.pending(request), [2])
        let compactEffects = try JSONSerialization.data(withJSONObject:
            JSONSerialization.jsonObject(with: request.sourceData), options: [.sortedKeys])
        try compactEffects.write(to: request.promptURL)
        XCTAssertEqual(try BatchPlanner.pending(request), [2])
        request.effects.loop = true
        XCTAssertThrowsError(try BatchPlanner.pending(request))
    }

    func testMalformedDetailedResponseLeavesNoPartialOutput() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder); StubProtocol.reply = nil }
        StubProtocol.reply = { request in
            (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil,
                headerFields: ["Content-Type": "application/json"])!, Data("{}".utf8))
        }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        let service = MusicService(session: URLSession(configuration: configuration))
        let request = GenerationRequest(prompt: "A piano song", baseName: "Test", outputFolder: folder,
            durationSeconds: 10, variations: 1, instrumental: false, model: .v25,
            format: .mp3_44100_128, includeDetails: true, plan: nil)
        do {
            _ = try await service.generate(request, index: 1, key: "fake-key")
            XCTFail("Malformed multipart response should fail")
        } catch {}
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path)
        XCTAssertTrue(names.isEmpty, "No audio, sidecar, or temporary file should be published")
    }
}
