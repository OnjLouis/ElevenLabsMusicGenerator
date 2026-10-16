import AppKit
import SwiftUI
import UniformTypeIdentifiers

@MainActor final class VideoSoundtrackEditor: ObservableObject {
    @Published var request: VideoSoundtrackRequest
    @Published var selected: UUID?
    @Published var busy = false
    @Published var reading = false
    @Published var status = "Ready."
    @Published private(set) var progress: SoundtrackProgress?
    @Published private(set) var startedAt: TimeInterval?
    private var finishedElapsed: TimeInterval = 0
    private var runID: UUID?
    private var cancelling = false
    var elapsed: TimeInterval { startedAt.map { max(0, ProcessInfo.processInfo.systemUptime - $0) } ?? finishedElapsed }
    @Published var recordings: [URL] = []
    @Published var selectedRecording: URL?
    private var task: Task<Void, Never>?
    private let parent: AppModel

    init(parent: AppModel) {
        self.parent = parent
        request = VideoSoundtrackRequest(format: parent.selectedFormat, outputFolder: parent.outputURL)
    }

    func addVideos() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.movie, .video]
        panel.allowsMultipleSelection = true
        guard panel.runModal() == .OK else { return }
        let urls = panel.urls
        reading = true
        task = Task {
            defer { reading = false; task = nil }
            do {
                for url in urls {
                    guard request.videos.count < 10 else { throw MusicError.validation("You can add no more than 10 videos.") }
                    let video = try await SoundtrackVideo.read(url)
                    guard video.bytes <= VideoSoundtrackRequest.maximumUploadBytes - request.videos.reduce(0, { $0 + $1.bytes }), video.seconds + request.videos.reduce(0, { $0 + $1.seconds }) <= 600 else {
                        throw MusicError.validation("The videos together must not exceed 200 MB or 10 minutes. This file was not added.")
                    }
                    request.videos.append(video); selected = video.id
                }
                status = "\(request.videos.count) videos; \(String(format: "%.1f", request.videos.reduce(0) { $0 + $1.seconds })) seconds in playback order. Nothing uploaded."
            } catch { show(error) }
        }
    }
    func removeVideo() {
        request.videos.removeAll { $0.id == selected }; selected = request.videos.first?.id
    }
    func move(_ step: Int) {
        guard let index = request.videos.firstIndex(where: { $0.id == selected }), request.videos.indices.contains(index + step) else { return }
        request.videos.swapAt(index, index + step)
    }
    func generate() {
        guard !busy && !reading else { return }
        do {
            try request.validate()
            guard let key = try KeychainStore.read(), !key.isEmpty else { throw MusicError.validation("Save your API key in Settings before generating a soundtrack.") }
            let alert = NSAlert()
            alert.messageText = "Generate video soundtrack?"
            alert.informativeText = "Upload \(request.videos.count) videos (\(String(format: "%.1f", request.videos.reduce(0) { $0 + $1.seconds })) seconds) to ElevenLabs in their listed order?\n\n\(request.separateTracks ? "Separate audio tracks" : "A mixed audio soundtrack") will be saved under:\n\(request.outputFolder.path)\n\nYour original videos will not be changed. Generation uses account credits. Cancelling after upload may still be charged."
            alert.addButton(withTitle: "Cancel"); alert.addButton(withTitle: "Generate")
            guard alert.runModal() == .alertSecondButtonReturn else { return }
            let input = request
            let run = UUID(); runID = run; cancelling = false; progress = nil
            startedAt = ProcessInfo.processInfo.systemUptime
            busy = true; parent.isBusy = true; parent.stopPlayback()
            task = Task { [self] in
                defer { finishedElapsed = elapsed; startedAt = nil; busy = false; parent.isBusy = false; task = nil; runID = nil; parent.checkBalance() }
                do {
                    let urls = try await MusicService().generateSoundtrack(input, key: key) { [weak self] update in
                        Task { @MainActor in
                            guard let self, self.runID == run, !self.cancelling,
                                  update.sequence > (self.progress?.sequence ?? 0) else { return }
                            if self.progress?.stage != update.stage { self.status = update.message }
                            self.progress = update
                        }
                    }
                    recordings.append(contentsOf: urls); selectedRecording = urls.first
                    status = "Generation complete. Saved \(urls.count) soundtrack recordings in \(urls[0].deletingLastPathComponent().path).\nElapsed: \(SoundtrackProgress.elapsedText(elapsed))."
                    parent.acceptSoundtrack(urls)
                } catch is CancellationError { status = "Generation cancelled. Original videos and completed recordings were kept. An accepted request can still be charged." }
                catch let error as URLError where error.code == .cancelled { status = "Generation cancelled. An accepted request can still be charged." }
                catch { show(error) }
            }
        } catch { show(error) }
    }
    func cancel() { cancelling = true; task?.cancel(); status = "Cancelling. An accepted request can still be charged." }
    func showProgress() {
        let text = busy ? (cancelling ? status + "\nElapsed: " + SoundtrackProgress.elapsedText(elapsed) + "." : progress?.describe(elapsed: elapsed) ?? status) : status
        let (alert, editor) = ApiKeyTestResultDialog.make(text)
        alert.messageText = "Soundtrack progress"; editor.setAccessibilityLabel("Soundtrack progress")
        let owner = NSApp.keyWindow, previous = owner?.firstResponder
        DispatchQueue.main.async { alert.window.makeFirstResponder(editor) }
        alert.runModal()
        if let previous, owner?.isVisible == true { owner?.makeFirstResponder(previous) }
    }
    func play() { if let selectedRecording { parent.selectedOutput = selectedRecording; parent.playSelected() } }
    func stop() { parent.stopPlayback() }
    func openOutput() { parent.openOutputFolder() }
    private func show(_ error: Error) {
        status = error.localizedDescription
        let (alert, editor) = ApiKeyTestResultDialog.make(status)
        alert.messageText = "Video soundtrack"; editor.setAccessibilityLabel("Soundtrack error")
        DispatchQueue.main.async { alert.window.makeFirstResponder(editor) }
        alert.runModal()
    }
}

struct VideoSoundtrackView: View {
    @StateObject private var editor: VideoSoundtrackEditor
    @Environment(\.dismiss) private var dismiss
    @State private var descriptionView: TabAwareTextView?
    @State private var tagsView: TabAwareTextView?
    @State private var statusView: TabAwareTextView?
    @State private var nameView: NSTextField?

    init(parent: AppModel) { _editor = StateObject(wrappedValue: VideoSoundtrackEditor(parent: parent)) }
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Video Soundtrack").font(.headline)
            List(editor.request.videos, selection: $editor.selected) { video in
                Text(video.url.lastPathComponent + " - " + String(format: "%.1f seconds", video.seconds)).tag(video.id)
            }.frame(height: 90).accessibilityLabel("Videos in playback order")
                .accessibilityHint("Videos are combined in this order. Selecting a file does not upload it.")
                .disabled(editor.busy || editor.reading)
            HStack {
                Button("Add Videos...", action: editor.addVideos).accessibilityHint("Choose up to 10 videos, at most 200 MB and 10 minutes in total.")
                Button("Remove", action: editor.removeVideo).disabled(editor.selected == nil).accessibilityHint("Remove the selected video from this request without deleting the original.")
                Button("Move Up") { editor.move(-1) }.accessibilityHint("Move the selected video earlier in playback order.")
                Button("Move Down") { editor.move(1) }.accessibilityHint("Move the selected video later in playback order.")
            }.disabled(editor.busy || editor.reading)
            Text("Description (optional)")
            KeyboardTextView(text: $editor.request.description, editable: !editor.busy,
                accessibilityLabel: "Soundtrack description", accessibilityHelp: "Optional musical direction, up to 4100 characters. Leave blank to let the video guide the soundtrack.",
                maximumLength: 4100, initialFocus: true,
                onTab: { tagsView?.window?.makeFirstResponder(tagsView) },
                onBackTab: { if let view = descriptionView { view.window?.selectPreviousKeyView(view) } },
                onReady: { descriptionView = $0 }).frame(height: 90)
            Text("Style tags (optional, one per line)")
            KeyboardTextView(text: $editor.request.tags, editable: !editor.busy,
                accessibilityLabel: "Style tags", accessibilityHelp: "Up to 10 musical styles, such as cinematic or upbeat. Put each on its own line.",
                onTab: { nameView?.window?.makeFirstResponder(nameView) }, onBackTab: { descriptionView?.window?.makeFirstResponder(descriptionView) },
                onReady: { tagsView = $0 }).frame(height: 55)
            SoundtrackNameField(text: $editor.request.name, enabled: !editor.busy,
                onBackTab: { tagsView?.window?.makeFirstResponder(tagsView) }, onReady: { nameView = $0 })
                .frame(height: 24)
            HStack {
                Toggle("Add sound effects", isOn: $editor.request.addSoundEffects).accessibilityHint("Include sound-effect cues based on events in the video.")
                Toggle("Separate tracks", isOn: $editor.request.separateTracks).accessibilityHint("Save returned music and sound effects as separate recordings rather than a mixed soundtrack.")
            }.disabled(editor.busy)
            Toggle("Use original audio as reference", isOn: $editor.request.useAudioAsReference)
                .accessibilityHint("Use the video's existing audio to guide genre, tempo, instruments, mood and musical progression. It is not copied into the output.").disabled(editor.busy)
            HStack {
                Picker("Model", selection: $editor.request.model) {
                    ForEach(MusicModel.allCases.filter { $0 != .soundEffects }) { item in Text(item.title).tag(item) }
                }.accessibilityHint("Music model for the soundtrack. Music v2.5 is selected initially.")
                Picker("Output format", selection: $editor.request.format) {
                    ForEach(AudioFormat.allCases) { item in Text(item.title).tag(item) }
                }.accessibilityHint("Choose the audio format. PCM is saved as stereo WAV. Subscription restrictions can apply.")
            }.disabled(editor.busy)
            KeyboardTextView(text: .constant(editor.status), editable: false,
                accessibilityLabel: "Soundtrack status", accessibilityHelp: "Upload progress, generation status and errors.",
                onTab: { if let view = statusView { view.window?.selectNextKeyView(view) } },
                onBackTab: { if let view = statusView { view.window?.selectPreviousKeyView(view) } },
                onReady: { statusView = $0 }).frame(height: 65)
            Group {
                if editor.busy {
                    TimelineView(.periodic(from: .now, by: 1)) { _ in Text("Elapsed: " + SoundtrackProgress.elapsedText(editor.elapsed) + ".").monospacedDigit() }
                } else { Text("Elapsed: " + SoundtrackProgress.elapsedText(editor.elapsed) + ".") }
            }.accessibilityHidden(true)
            List(editor.recordings, id: \.self, selection: $editor.selectedRecording) { url in Text(url.lastPathComponent).tag(url) }
                .frame(height: 60).accessibilityLabel("Soundtrack recordings").accessibilityHint("Completed recordings saved automatically. Return plays the selected recording.")
                .onKeyPress(.return) { editor.play(); return .handled }
                .onKeyPress(.escape) { editor.stop(); return .handled }
            HStack {
                Button("Generate", action: editor.generate).keyboardShortcut(.return, modifiers: .command)
                    .accessibilityLabel("Generate soundtrack").accessibilityHint("Command-Return. Validate the videos, then confirm their upload to ElevenLabs. Generation uses credits.").disabled(editor.busy || editor.reading)
                Button("Cancel Generation", action: editor.cancel).disabled(!editor.busy).accessibilityHint("Cancel the request. An accepted request may still be charged.")
                Button("Check Progress...", action: editor.showProgress).keyboardShortcut("r", modifiers: [.command, .shift])
                    .accessibilityHint("Command-Shift-R. Show the current stage, elapsed time and real transfer counts, without another API request.")
                Spacer()
                if editor.busy || editor.reading {
                    if let percent = editor.progress?.percent, !editor.reading {
                        ProgressView(value: Double(percent), total: 100).frame(width: 100).accessibilityLabel("Transfer progress")
                    } else { ProgressView().controlSize(.small).accessibilityLabel("Soundtrack progress") }
                }
            }
            HStack {
                Button("Play", action: editor.play).disabled(editor.selectedRecording == nil).accessibilityHint("Play the selected soundtrack recording.")
                Button("Stop", action: editor.stop).accessibilityHint("Stop audio playback.")
                Button("Open Output Folder", action: editor.openOutput).accessibilityHint("Open the output folder configured in Settings.")
                Spacer()
                Button("Close") { editor.stop(); dismiss() }.disabled(editor.busy || editor.reading).accessibilityHint("Close this dialog and return to Music.")
            }
        }.padding(20).frame(width: 760)
            .interactiveDismissDisabled()
            .onExitCommand { if editor.busy { editor.cancel() } else if !editor.reading { editor.stop(); dismiss() } }
            .onDisappear { editor.stop(); if editor.busy { editor.cancel() } }
    }
}

private struct SoundtrackNameField: NSViewRepresentable {
    @Binding var text: String
    var enabled: Bool
    var onBackTab: () -> Void
    var onReady: (NSTextField) -> Void
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    func makeNSView(context: Context) -> NSTextField {
        let field = NSTextField(string: text)
        field.placeholderString = "Folder name (optional)"
        field.setAccessibilityLabel("Soundtrack folder name")
        field.setAccessibilityHelp("Name for a new soundtrack subfolder. A unique suffix prevents overwriting recordings.")
        field.delegate = context.coordinator
        DispatchQueue.main.async { onReady(field) }
        return field
    }
    func updateNSView(_ field: NSTextField, context: Context) {
        context.coordinator.parent = self
        if field.stringValue != text { field.stringValue = text }
        field.isEnabled = enabled
    }
    final class Coordinator: NSObject, NSTextFieldDelegate {
        var parent: SoundtrackNameField
        init(_ parent: SoundtrackNameField) { self.parent = parent }
        func controlTextDidChange(_ notification: Notification) {
            if let field = notification.object as? NSTextField { parent.text = field.stringValue }
        }
        func control(_ control: NSControl, textView: NSTextView, doCommandBy command: Selector) -> Bool {
            guard command == #selector(NSResponder.insertBacktab(_:)) else { return false }
            parent.onBackTab(); return true
        }
    }
}
