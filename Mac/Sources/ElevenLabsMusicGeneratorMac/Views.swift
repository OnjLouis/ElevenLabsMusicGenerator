import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct MainView: View {
    @ObservedObject var model: AppModel
    @State private var promptView: TabAwareTextView?
    @State private var balanceView: TabAwareTextView?
    @State private var statusView: TabAwareTextView?
    @FocusState private var focus: FocusField?

    private enum FocusField { case baseName, model, outputFormat }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("ElevenLabs Music and Sound FX Generator").font(.title2.weight(.semibold))
                Spacer()
                Button("Open Music Folder") { model.openOutputFolder() }
                    .accessibilityHint("Opens the folder where new tracks and prompts are saved. Command-Shift-O.")
            }
            Text("Balance").font(.headline)
            KeyboardTextView(text: .constant(model.balanceText), editable: false,
                accessibilityLabel: "Credit balance",
                accessibilityHelp: "Latest ElevenLabs credit balance. Read by line with the arrow keys or copy selected text. Option-B focuses this field.",
                onTab: { focusTextView(statusView) },
                onBackTab: {
                    if let balanceView { balanceView.window?.selectPreviousKeyView(balanceView) }
                },
                onReady: { balanceView = $0 })
                .frame(height: 85)
            Text("Status").font(.headline)
            KeyboardTextView(text: .constant(model.status.joined(separator: "\n")), editable: false,
                accessibilityLabel: "Status log",
                accessibilityHelp: "Generation and error messages, newest last.",
                onTab: { focusTextView(promptView) },
                onBackTab: { focusTextView(balanceView) },
                onReady: { statusView = $0 })
                .frame(height: 100)
            Text("Prompt").font(.headline)
            KeyboardTextView(text: $model.prompt, editable: true,
                accessibilityLabel: model.isSoundEffect ? "Sound effects prompt" : "Music prompt",
                accessibilityHelp: (model.isSoundEffect ? "Type or paste a sound effect description." : "Type or paste a music description.") +
                    " \(max(0, (model.isSoundEffect ? GenerationRequest.soundEffectsPromptLimit : 4100) - model.prompt.utf16.count)) characters remaining.",
                maximumLength: model.isSoundEffect ? GenerationRequest.soundEffectsPromptLimit : 4100,
                onTab: { focus = .baseName },
                onBackTab: { focusTextView(statusView) },
                onReady: { promptView = $0 })
                .frame(minHeight: 160)
            Text("\(model.prompt.utf16.count) of \(model.isSoundEffect ? GenerationRequest.soundEffectsPromptLimit : 4100) characters; \(max(0, (model.isSoundEffect ? GenerationRequest.soundEffectsPromptLimit : 4100) - model.prompt.utf16.count)) remaining")
                .font(.caption)
                .accessibilityLabel("Prompt character count: \(model.prompt.utf16.count) of \(model.isSoundEffect ? GenerationRequest.soundEffectsPromptLimit : 4100); \(max(0, (model.isSoundEffect ? GenerationRequest.soundEffectsPromptLimit : 4100) - model.prompt.utf16.count)) remaining")
            HStack(spacing: 16) {
                VStack(alignment: .leading) {
                    Text("Base filename")
                    TextField("Suggested from prompt", text: Binding(
                        get: { model.baseName }, set: { model.setBaseNameFromUser($0) }))
                        .focused($focus, equals: .baseName)
                        .accessibilityLabel("Base filename")
                        .accessibilityHint("Names the generated audio and prompt files.")
                }
                VStack(alignment: .leading) {
                    Text("Length, seconds")
                    if model.isSoundEffect {
                        Toggle("Automatic duration", isOn: $model.preferences.effects.automaticDuration)
                            .accessibilityHint("Let ElevenLabs choose a suitable length, up to 30 seconds.")
                        TextField("Length", value: $model.preferences.effects.duration, format: .number)
                            .frame(width: 110)
                            .disabled(model.preferences.effects.automaticDuration || model.isBusy)
                            .accessibilityLabel("Sound effect length in seconds")
                            .accessibilityHint("Choose from 0.5 to 30 seconds. Unavailable with automatic duration.")
                    } else {
                    TextField("Length", value: $model.preferences.durationSeconds, format: .number)
                        .frame(width: 110)
                        .disabled(model.planEnabled)
                        .accessibilityLabel("Length in seconds")
                        .accessibilityHint("Length of each variation. A composition plan supplies its own section lengths.")
                    }
                }
                VStack(alignment: .leading) {
                    Text("Variations")
                    TextField("Variations", value: $model.preferences.variations, format: .number)
                        .frame(width: 90)
                        .accessibilityLabel("Number of variations")
                        .accessibilityHint("Number of tracks to generate from this prompt.")
                }
            }
            if model.isSoundEffect {
                HStack(spacing: 20) {
                    Toggle("Loop", isOn: $model.preferences.effects.loop)
                        .accessibilityHint("Generate a sound effect that loops smoothly.")
                    Text("Prompt influence")
                    TextField("Prompt influence", value: $model.preferences.effects.promptInfluence, format: .number)
                        .frame(width: 70)
                        .accessibilityLabel("Prompt influence")
                        .accessibilityHint("Zero to one. Higher values follow the prompt more closely.")
                }.disabled(model.isBusy)
            } else {
            HStack(spacing: 20) {
                Toggle("Instrumental", isOn: $model.preferences.instrumental)
                    .disabled(model.planEnabled)
                    .accessibilityHint("Force instrumental output. Command-I. Unavailable with a composition plan.")
                Toggle("Use composition plan", isOn: $model.planEnabled)
                    .disabled(model.selectedModel == .v1)
                    .accessibilityHint("Use the current plan instead of the plain prompt. Command-U.")
                Button("Edit Plan...") { model.editPlan() }
                    .disabled(model.isBusy)
                    .accessibilityHint("Edit sections and lyrics, even when plan mode is off. Command-P.")
            }
            }
            HStack(spacing: 16) {
                Picker("Model", selection: $model.preferences.model) {
                    ForEach(MusicModel.allCases) { item in Text(item.title).tag(item.rawValue) }
                }
                .focused($focus, equals: .model)
                .accessibilityHint("Choose a Music model or Sound Effects. Command-Shift-M.")
                Picker("Output format", selection: $model.preferences.format) {
                    ForEach(AudioFormat.allCases.filter { !model.isSoundEffect || $0 == .wav || $0 == .mp3_44100_128 || $0 == .mp3_44100_192 }) { item in Text(item.title).tag(item.rawValue) }
                }
                .focused($focus, equals: .outputFormat)
                .accessibilityHint("Choose the audio format. Command-Shift-F.")
            }
            .disabled(model.isBusy)
            Text("Saving to: \(model.outputURL.path)")
                .font(.callout).foregroundStyle(.secondary)
                .lineLimit(2).textSelection(.enabled)
                .accessibilityLabel("Output folder: \(model.outputURL.path)")
                .accessibilityHint("Current save location. Change it in Settings.")
            HStack {
                Button("Generate") { model.prepareGeneration() }
                    .keyboardShortcut(.return, modifiers: .command)
                    .disabled(model.isBusy)
                    .help("Generate audio, Command-Return")
                    .accessibilityHint("Reviews the request before sending it to ElevenLabs. New generations can spend credits. Command-Return.")
                Button("Cancel") { model.cancelGeneration() }
                    .disabled(!model.isBusy)
                    .help("Cancel the current generation")
                    .accessibilityHint("Stops the current request. Completed tracks remain saved.")
                Spacer()
                if model.isBusy { ProgressView().controlSize(.small) }
            }
        }
        .padding(20)
        .sheet(isPresented: $model.showPlanEditor) { PlanEditorView(model: model) }
        .alert(model.isSoundEffect ? "Generate Sound Effects?" : "Generate Music?", isPresented: $model.showConfirmation) {
            Button("Generate") { model.beginGeneration() }
                .accessibilityHint("Send the confirmed request to ElevenLabs. Credits may be spent.")
            Button("Cancel", role: .cancel) {}
                .accessibilityHint("Return without generating audio or spending credits.")
        } message: { Text(model.confirmationText) }
        .alert(item: $model.notice) { item in
            Alert(title: Text(item.title), message: Text(item.message), dismissButton: .default(Text("OK")))
        }
        .onChange(of: model.preferences.model) { oldValue, value in
            model.modelChanged(from: oldValue, to: value)
            if value == MusicModel.v1.rawValue { model.planEnabled = false }
            if value == MusicModel.soundEffects.rawValue && model.preferences.format.hasPrefix("mp3_48000") {
                model.preferences.format = AudioFormat.mp3_44100_192.rawValue
                model.addStatus("Sound Effects selected. Output changed to MP3 44.1 kHz, 192 kbps.")
            }
        }
        .task { model.checkUpdatesOnLaunch(); model.checkBalance() }
        .onChange(of: model.balanceFocusRequest) { _, _ in focusTextView(balanceView) }
        .onChange(of: model.focusRequest) { _, request in
            guard let request else { return }
            focus = request.control == .model ? .model : .outputFormat
        }
    }

    private func focusTextView(_ view: TabAwareTextView?) {
        guard let view else { return }
        view.window?.makeFirstResponder(view)
    }
}

struct SettingsView: View {
    @ObservedObject var model: AppModel
    @State private var enteredKey = ""
    @State private var hasKey = false
    @State private var keyStatus = ""
    @State private var isTestingKey = false

    var body: some View {
        VStack(spacing: 0) {
            TabView {
                Form {
                HStack {
                    Text("Music folder")
                    Text(model.preferences.outputFolder)
                        .textSelection(.enabled)
                        .lineLimit(2)
                        .accessibilityLabel("Default music output folder")
                        .accessibilityHint("Current default save location. Use Choose to change it.")
                    Spacer()
                    Button("Choose...") { chooseFolder() }
                        .accessibilityHint("Choose a default output folder for music and prompt files.")
                }
                Toggle("Save generated lyrics and details", isOn: $model.preferences.includeDetails)
                    .accessibilityHint("Save returned lyrics and metadata beside the music in a Lyrics folder.")
                Toggle("Check for updates when the app opens", isOn: Binding(
                    get: { model.preferences.autoUpdateOnLaunch == true },
                    set: { model.preferences.autoUpdateOnLaunch = $0 }))
                    .accessibilityHint("New versions are announced; installation remains your choice.")
                Text("The folder is created when music or a prompt is first saved.")
                    .foregroundStyle(.secondary)
            }
                .padding(18).tabItem { Text("General").accessibilityHint("Music folder and saved details.") }
                Form {
                SecureField("New API key", text: $enteredKey)
                    .accessibilityLabel("New ElevenLabs API key")
                    .accessibilityHint("Paste a key, then choose Save Key to store it in Keychain.")
                HStack {
                    Button("Save Key") { saveKey() }
                        .accessibilityHint("Stores the entered API key in this Mac user's Keychain.")
                    Button("Test Key") { testKey() }
                        .disabled(isTestingKey)
                        .accessibilityHint("Tests Music access without audio, or asks before generating a credit-using Sound Effects test.")
                    Button("Remove Key") { removeKey() }
                        .disabled(!hasKey)
                        .accessibilityHint("Removes this app's API key from Keychain.")
                }
                Text(hasKey ? "An API key is stored in macOS Keychain." : "No API key is stored yet.")
                Text(keyStatus).textSelection(.enabled)
                    .accessibilityHint("Result of the most recent key action.")
                Link("Get an ElevenLabs API key", destination: URL(string: "https://elevenlabs.io/app/settings/api-keys")!)
                    .accessibilityHint("Opens ElevenLabs' API key page in your default browser.")
            }
                .padding(18).tabItem { Text("API Key").accessibilityHint("Save, test, or remove the API key.") }
            }
            HStack {
                Spacer()
                Button("Close") { NSApp.keyWindow?.performClose(nil) }
                    .keyboardShortcut(.cancelAction)
                    .accessibilityHint("Closes Settings. Escape also works.")
            }
            .padding([.horizontal, .bottom], 18)
        }
        .frame(width: 580, height: 290)
        .onAppear { hasKey = (try? KeychainStore.read()) != nil }
        .onExitCommand { NSApp.keyWindow?.performClose(nil) }
    }

    private func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        model.preferences.outputFolder = url.path
    }

    private func saveKey() {
        do {
            try KeychainStore.write(enteredKey)
            enteredKey = ""
            hasKey = true
            keyStatus = "API key saved in Keychain."
            model.checkBalance()
        } catch { keyStatus = error.localizedDescription }
    }

    private func testKey() {
        guard !isTestingKey else { return }
        guard !enteredKey.isEmpty || hasKey else {
            keyStatus = "Enter or save an API key first."
            return
        }
        let selectedModel = model.selectedModel
        if selectedModel == .soundEffects {
            let alert = NSAlert()
            alert.messageText = "Test Sound Effects API key?"
            alert.informativeText = "ElevenLabs must generate a 0.5-second test effect to verify access. The audio will be discarded, but this request may spend credits."
            alert.addButton(withTitle: "Cancel")
            alert.addButton(withTitle: "Generate Test Effect")
            guard alert.runModal() == .alertSecondButtonReturn else {
                keyStatus = "Sound Effects key test cancelled. No request was sent."
                return
            }
        }
        isTestingKey = true
        Task {
            defer { isTestingKey = false }
            do {
                let key = enteredKey.isEmpty ? (try KeychainStore.read() ?? "") : enteredKey
                guard !key.isEmpty else { throw MusicError.validation("Enter or save an API key first.") }
                keyStatus = selectedModel == .soundEffects ? "Testing Sound Effects access..." : "Testing Music access..."
                let service = MusicService()
                let generationResult: String
                do { generationResult = try await service.testKey(key, model: selectedModel) }
                catch { generationResult = "API key test failed: " + error.localizedDescription }
                keyStatus = generationResult + "\n" + (await service.testBalanceAccess(key))
            } catch { keyStatus = error.localizedDescription }
        }
    }

    private func removeKey() {
        do { try KeychainStore.delete(); hasKey = false; keyStatus = "API key removed from Keychain." }
        catch { keyStatus = error.localizedDescription }
    }
}

struct PlanEditorView: View {
    @ObservedObject var model: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var selectedID: UUID?

    private var selectedIndex: Int? { model.plan?.sections.firstIndex(where: { $0.id == selectedID }) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Composition Plan").font(.title2.weight(.semibold))
            HStack {
                List(selection: $selectedID) {
                    ForEach(model.plan?.sections ?? []) { section in
                        Text("\(section.name), \(String(format: "%.3f", Double(section.durationMilliseconds) / 1000)) seconds")
                            .tag(section.id)
                    }
                }
                .frame(minWidth: 260)
                .accessibilityHint("Select a plan section to edit.")
                VStack(alignment: .leading, spacing: 10) {
                    if let selectedIndex {
                        sectionFields(index: selectedIndex)
                    } else {
                        Text("Select a section or add one.").foregroundStyle(.secondary)
                    }
                    Spacer()
                }
                .frame(minWidth: 390)
            }
            HStack {
                Button("Add") { addSection() }
                    .accessibilityHint("Adds a new section to the end of the composition plan.")
                Button("Remove") { removeSection() }.disabled(selectedIndex == nil)
                    .accessibilityHint("Removes the selected section.")
                Button("Move Up") { moveSection(-1) }.disabled(selectedIndex == nil)
                    .accessibilityHint("Moves the selected section earlier in the song.")
                Button("Move Down") { moveSection(1) }.disabled(selectedIndex == nil)
                    .accessibilityHint("Moves the selected section later in the song.")
                Spacer()
                Text("Total: \(String(format: "%.3f", Double(model.plan?.totalMilliseconds ?? 0) / 1000)) seconds")
            }
            HStack {
                Button("Suggest from Prompt") { model.suggestPlan() }.disabled(model.isBusy)
                    .accessibilityHint("Asks ElevenLabs to draft sections from the prompt. Review them before generation.")
                Button("Open Plan or Details...") { model.openPlan() }
                    .accessibilityHint("Imports a composition plan from a plan JSON file or generated track details containing a plan.")
                Button("Export Plan...") { model.exportPlan() }
                    .accessibilityHint("Saves the current plan as reusable JSON.")
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
                    .accessibilityHint("Returns to the main window. The plan remains available for generation.")
            }
        }
        .padding(20)
        .frame(minWidth: 750, minHeight: 520)
        .onAppear { selectedID = model.plan?.sections.first?.id }
        .onChange(of: model.plan?.sections.first?.id) { _, firstID in
            if !(model.plan?.sections.contains(where: { $0.id == selectedID }) ?? false) {
                selectedID = firstID
            }
        }
    }

    @ViewBuilder
    private func sectionFields(index: Int) -> some View {
        TextField("Section name", text: binding(index, \.name))
            .accessibilityHint("Name this part of the song, such as Verse or Chorus.")
        TextField("Duration, milliseconds", value: binding(index, \.durationMilliseconds), format: .number)
            .accessibilityHint("Length of this section in milliseconds.")
        Text("Lyrics and musical cues")
        TextEditor(text: binding(index, \.body))
            .frame(minHeight: 120)
            .overlay(RoundedRectangle(cornerRadius: 5).stroke(.separator))
            .accessibilityLabel("Lyrics and musical cues")
            .accessibilityHint("Enter lyrics or performance directions for this section.")
        TextField("Included styles, one per line", text: binding(index, \.positiveStyles), axis: .vertical)
            .lineLimit(2...4)
            .accessibilityHint("Enter one desired style per line.")
        TextField("Excluded styles, one per line", text: binding(index, \.negativeStyles), axis: .vertical)
            .lineLimit(2...4)
            .accessibilityHint("Enter one style to avoid per line.")
        Picker("Context adherence", selection: binding(index, \.contextAdherence)) {
            Text("Low").tag("low")
            Text("Medium").tag("medium")
            Text("High").tag("high")
        }
        .accessibilityHint("Choose how closely this section should follow the preceding music.")
    }

    private func binding<Value>(_ index: Int, _ keyPath: WritableKeyPath<MusicSection, Value>) -> Binding<Value> {
        Binding(get: { model.plan!.sections[index][keyPath: keyPath] }, set: { newValue in
            var plan = model.plan!
            plan.sections[index][keyPath: keyPath] = newValue
            model.plan = plan
        })
    }

    private func addSection() {
        var plan = model.plan ?? CompositionPlan()
        let section = MusicSection()
        plan.sections.append(section)
        model.plan = plan
        selectedID = section.id
    }

    private func removeSection() {
        guard let selectedIndex else { return }
        var plan = model.plan!
        plan.sections.remove(at: selectedIndex)
        model.plan = plan
        selectedID = plan.sections.first?.id
    }

    private func moveSection(_ direction: Int) {
        guard let selectedIndex, let plan = model.plan else { return }
        let target = selectedIndex + direction
        guard plan.sections.indices.contains(target) else { return }
        var reordered = plan
        reordered.sections.swapAt(selectedIndex, target)
        model.plan = reordered
    }
}
