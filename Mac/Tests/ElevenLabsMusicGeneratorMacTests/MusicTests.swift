import Foundation
import XCTest
@testable import ElevenLabsMusicGeneratorMac

final class MusicTests: XCTestCase {
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
        XCTAssertEqual(filename.value, "Gentle_piano_with_soft_strings")
        filename.promptChanged("Bright jazz piano")
        XCTAssertEqual(filename.value, "Bright_jazz_piano")
        filename.userChanged("My own title")
        filename.promptChanged("A completely different prompt")
        XCTAssertEqual(filename.value, "My own title")
        filename.reset()
        XCTAssertEqual(filename.value, "")
        filename.promptChanged("Fresh start")
        XCTAssertEqual(filename.value, "Fresh_start")
        filename.openedDocument(named: "Saved Song")
        XCTAssertEqual(filename.value, "Saved Song")
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
        let reopenedPlan = try CompositionPlan.decodePayload(Data(contentsOf: result.detailsURL!))
        XCTAssertEqual(reopenedPlan.sections.first?.body, "Hello world")
        XCTAssertTrue(try String(contentsOf: result.lyricsURL!).contains("Hello world"))
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path)
        XCTAssertFalse(names.contains(where: { $0.contains(".part") }))
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
