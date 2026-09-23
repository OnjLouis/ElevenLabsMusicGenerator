import AppKit
import Foundation

struct AppNotice: Identifiable {
    let id = UUID()
    var title: String
    var message: String
}

enum MainControlFocus: Equatable { case model, outputFormat }

struct MainFocusRequest: Equatable {
    let id = UUID()
    let control: MainControlFocus
}

@MainActor
final class AppModel: ObservableObject {
    @Published var prompt: String = DraftStore.loadPrompt() {
        didSet {
            scheduleDraftSave()
            filename.promptChanged(prompt)
        }
    }
    @Published private(set) var filename = FilenameSuggestionState(prompt: "")
    @Published var preferences = AppPreferences() {
        didSet { preferences.save() }
    }
    @Published var plan: CompositionPlan? = DraftStore.loadPlan() {
        didSet { DraftStore.savePlan(plan) }
    }
    @Published var planEnabled = false
    @Published var isBusy = false
    @Published var showConfirmation = false
    @Published var showPlanEditor = false
    @Published var notice: AppNotice?
    @Published var status: [String] = ["Ready."]
    @Published var completed: [URL] = []
    @Published var focusRequest: MainFocusRequest?

    private var promptDocument: URL?
    private var generationTask: Task<Void, Never>?
    private var draftTask: Task<Void, Never>?
    private var pendingRequest: GenerationRequest?
    private var pendingIndices: [Int] = []
    private let service = MusicService()

    init() {
        preferences = AppPreferences.load()
        planEnabled = plan != nil
        filename = FilenameSuggestionState(prompt: prompt)
    }

    func newPrompt() {
        guard !isBusy else { return }
        if !prompt.isEmpty || plan != nil {
            let alert = NSAlert()
            alert.messageText = "Start a new prompt?"
            alert.informativeText = "The current prompt and composition plan will be cleared. Save or export anything you want to keep first."
            alert.addButton(withTitle: "New Prompt")
            alert.addButton(withTitle: "Cancel")
            guard alert.runModal() == .alertFirstButtonReturn else { return }
        }
        prompt = ""
        filename.reset()
        promptDocument = nil
        plan = nil
        planEnabled = false
        addStatus("New prompt ready.")
    }

    var outputURL: URL { URL(fileURLWithPath: preferences.outputFolder, isDirectory: true) }
    var baseName: String { filename.value }
    var selectedModel: MusicModel { MusicModel(rawValue: preferences.model) ?? .v25 }
    var selectedFormat: AudioFormat { AudioFormat(rawValue: preferences.format) ?? .wav }

    func focus(_ control: MainControlFocus) {
        focusRequest = MainFocusRequest(control: control)
    }

    func setBaseNameFromUser(_ value: String) {
        filename.userChanged(value)
    }

    func addStatus(_ text: String) {
        status.append(text)
        if status.count > 100 { status.removeFirst(status.count - 100) }
    }

    private func scheduleDraftSave() {
        draftTask?.cancel()
        let text = prompt
        draftTask = Task {
            try? await Task.sleep(for: .milliseconds(600))
            guard !Task.isCancelled else { return }
            DraftStore.savePrompt(text)
        }
    }

    func openPrompt() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.plainText]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            prompt = try String(contentsOf: url, encoding: .utf8)
            filename.openedDocument(named: url.deletingPathExtension().lastPathComponent)
            promptDocument = url
            addStatus("Opened \(url.lastPathComponent).")
        } catch { show(error) }
    }

    func savePrompt(as newFile: Bool = false) {
        if !newFile, let promptDocument {
            do {
                try Data((prompt + "\n").utf8).write(to: promptDocument, options: .atomic)
                addStatus("Saved \(promptDocument.lastPathComponent).")
            } catch { show(error) }
            return
        }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.plainText]
        panel.nameFieldStringValue = GenerationRequest.safeStem(baseName.isEmpty ? prompt : baseName) + ".txt"
        panel.directoryURL = outputURL
        do { try FileManager.default.createDirectory(at: outputURL, withIntermediateDirectories: true) }
        catch { show(error); return }
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try Data((prompt + "\n").utf8).write(to: url, options: .atomic)
            promptDocument = url
            addStatus("Saved \(url.lastPathComponent).")
        } catch { show(error) }
    }

    func openPlan() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            plan = try CompositionPlan.decodePayload(Data(contentsOf: url))
            planEnabled = true
            showPlanEditor = true
            addStatus("Opened \(url.lastPathComponent).")
        } catch { show(error) }
    }

    func editPlan() {
        guard !isBusy else { return }
        guard selectedModel != .v1 else {
            show(MusicError.validation("Composition plans require Music v2 or v2.5."))
            return
        }
        showPlanEditor = true
    }

    func exportPlan() {
        guard let plan else { show(MusicError.validation("Create a composition plan first.")); return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = GenerationRequest.safeStem(baseName.isEmpty ? prompt : baseName) + ".plan.json"
        panel.directoryURL = outputURL
        do { try FileManager.default.createDirectory(at: outputURL, withIntermediateDirectories: true) }
        catch { show(error); return }
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try plan.encodedPayload().write(to: url, options: .atomic)
            addStatus("Exported \(url.lastPathComponent).")
        } catch { show(error) }
    }

    func suggestPlan() {
        guard !isBusy else { return }
        let clean = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty, clean.count <= 4100 else { show(MusicError.validation("Enter a prompt of no more than 4,100 characters.")); return }
        guard selectedModel != .v1 else { show(MusicError.validation("Choose Music v2 or v2.5 for a plan.")); return }
        isBusy = true
        addStatus("Requesting a composition plan.")
        Task {
            defer { isBusy = false }
            do {
                guard let key = try KeychainStore.read(), !key.isEmpty else { throw MusicError.validation("Add an API key in Settings first.") }
                plan = try await service.suggestPlan(prompt: clean, seconds: preferences.durationSeconds, model: selectedModel, key: key)
                planEnabled = true
                showPlanEditor = true
                addStatus("Composition plan received. Review its sections before generation.")
            } catch { show(error) }
        }
    }

    func prepareGeneration() {
        guard !isBusy else { return }
        do {
            let request = GenerationRequest(prompt: prompt, baseName: baseName, outputFolder: outputURL,
                durationSeconds: preferences.durationSeconds, variations: preferences.variations,
                instrumental: preferences.instrumental, model: selectedModel, format: selectedFormat,
                includeDetails: preferences.includeDetails, plan: planEnabled ? plan : nil)
            if planEnabled && plan == nil { throw MusicError.validation("Create or open a composition plan first.") }
            let indices = try BatchPlanner.pending(request)
            guard !indices.isEmpty else {
                addStatus("All requested variations already exist. Nothing to generate.")
                notice = AppNotice(title: "Already Complete", message: "All requested tracks already exist. No new request was sent to ElevenLabs.")
                return
            }
            pendingRequest = request
            pendingIndices = indices
            showConfirmation = true
        } catch { show(error) }
    }

    var confirmationText: String {
        guard let request = pendingRequest else { return "" }
        let existing = request.variations - pendingIndices.count
        let duration = request.plan.map { $0.totalMilliseconds / 1000 } ?? request.durationSeconds
        return "Generate \(pendingIndices.count) track(s) of about \(duration) seconds using \(request.model.title), \(request.format.title)? \(existing) existing variation(s) will be kept. ElevenLabs credits may be charged for each new request."
    }

    func beginGeneration() {
        guard let request = pendingRequest, !pendingIndices.isEmpty else { return }
        isBusy = true
        completed = []
        let indices = pendingIndices
        generationTask = Task {
            defer { isBusy = false; generationTask = nil; pendingRequest = nil; pendingIndices = [] }
            do {
                guard let key = try KeychainStore.read(), !key.isEmpty else { throw MusicError.validation("Add an API key in Settings first.") }
                for index in indices {
                    try Task.checkCancellation()
                    addStatus("Generating variation \(index) of \(request.variations).")
                    let result = try await service.generate(request, index: index, key: key)
                    completed.append(result.url)
                    addStatus("Saved \(result.url.lastPathComponent). Time: \(String(format: "%.1f", result.seconds)) seconds.")
                }
                let summary = "Generation complete. \(completed.count) new track(s) saved to \(request.outputFolder.path)."
                addStatus(summary)
                NSSound.beep()
                NSApp.requestUserAttention(.informationalRequest)
                notice = AppNotice(title: "Music Generation Complete", message: summary)
            } catch is CancellationError {
                addStatus("Generation cancelled. Completed tracks were kept.")
            } catch {
                show(error)
                addStatus("Generation stopped. Completed tracks were kept.")
            }
        }
    }

    func cancelGeneration() { generationTask?.cancel() }

    func openOutputFolder() {
        do {
            try FileManager.default.createDirectory(at: outputURL, withIntermediateDirectories: true)
            NSWorkspace.shared.open(outputURL)
        } catch { show(error) }
    }

    func openManual() {
        guard let url = Bundle.main.url(forResource: "Manual", withExtension: "html") else {
            show(MusicError.response("The Mac manual is missing from this build.")); return
        }
        NSWorkspace.shared.open(url)
    }

    func openProjectPage() {
        let url = URL(string: "https://github.com/OnjLouis/ElevenLabsMusicGenerator")!
        if !NSWorkspace.shared.open(url) {
            show(MusicError.response("Could not open the project page in your browser."))
        }
    }

    func openDonatePage() {
        let url = URL(string: "https://onj.me/donate")!
        if !NSWorkspace.shared.open(url) {
            show(MusicError.response("Could not open the donation page in your browser."))
        }
    }

    func openUsageAnalytics() {
        let url = URL(string: "https://elevenlabs.io/app/developers/analytics/usage")!
        if !NSWorkspace.shared.open(url) {
            show(MusicError.response("Could not open ElevenLabs Usage Analytics in your browser."))
        }
    }

    func checkUpdates() {
        Task {
            do {
                let url = URL(string: "https://api.github.com/repos/OnjLouis/ElevenLabsMusicGenerator/releases?per_page=100")!
                var request = URLRequest(url: url)
                request.setValue("ElevenLabs Music Generator updater", forHTTPHeaderField: "User-Agent")
                request.timeoutInterval = 20
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse else {
                    throw MusicError.response("The release server did not respond.")
                }
                if http.statusCode == 404 {
                    notice = AppNotice(title: "Check for Updates", message: "No published release channel is available yet.")
                    return
                }
                guard http.statusCode == 200 else {
                    throw MusicError.response("The release check returned HTTP \(http.statusCode).")
                }
                guard let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String else {
                    throw MusicError.response("The app version could not be read.")
                }
                guard let release = try ReleaseCatalog.newerMacRelease(in: data, currentVersion: current) else {
                    notice = AppNotice(title: "Check for Updates", message: "ElevenLabs Music Generator \(current) is up to date.")
                    return
                }
                let alert = NSAlert()
                alert.messageText = "Version \(release.version) is available"
                alert.informativeText = "Open the official releases page to download the Mac ZIP? The app will not install it automatically."
                alert.addButton(withTitle: "Open Releases")
                alert.addButton(withTitle: "Later")
                if alert.runModal() == .alertFirstButtonReturn && !NSWorkspace.shared.open(release.page) {
                    throw MusicError.response("Could not open the releases page in your browser.")
                }
            } catch { show(error) }
        }
    }

    func show(_ error: Error) {
        let message = error.localizedDescription
        notice = AppNotice(title: "ElevenLabs Music Generator", message: message)
        addStatus("Error: \(message)")
    }
}
