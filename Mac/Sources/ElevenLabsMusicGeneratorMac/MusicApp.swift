import AppKit
import SwiftUI

@main
struct ElevenLabsMusicGeneratorApp: App {
    @StateObject private var model = AppModel()

    var body: some Scene {
        WindowGroup("ElevenLabs Music and Sound FX Generator") {
            MainView(model: model)
                .frame(minWidth: 760, minHeight: 700)
                .onDisappear { model.flushDrafts() }
                .onReceive(NotificationCenter.default.publisher(for: NSApplication.willTerminateNotification)) { _ in model.flushDrafts() }
        }
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("New Prompt") { model.newPrompt() }
                    .keyboardShortcut("n", modifiers: .command)
                Divider()
                Button("Open Prompt...") { model.openPrompt() }
                    .keyboardShortcut("o", modifiers: .command)
                Button("Open Composition Plan...") { model.openPlan() }
                    .disabled(model.isSoundEffect)
                Button("Edit Composition Plan...") { model.editPlan() }
                    .disabled(model.isSoundEffect)
                    .keyboardShortcut("p", modifiers: .command)
                Divider()
                Button("Save Prompt") { model.savePrompt() }
                    .keyboardShortcut("s", modifiers: .command)
                Button("Save Prompt As...") { model.savePrompt(as: true) }
                    .keyboardShortcut("s", modifiers: [.command, .shift])
                Button("Export Composition Plan...") { model.exportPlan() }
                Divider()
                Button("Open Music Folder") { model.openOutputFolder() }
                    .keyboardShortcut("o", modifiers: [.command, .shift])
            }
            CommandGroup(after: .appInfo) {
                Button("Check for Updates...") { model.checkUpdates() }
                    .keyboardShortcut(KeyEquivalent("\u{F704}"), modifiers: .shift)
                Button("Donate") { model.openDonatePage() }
            }
            CommandMenu("Controls") {
                Toggle("Instrumental", isOn: $model.preferences.instrumental)
                    .disabled(model.planEnabled || model.isSoundEffect)
                    .keyboardShortcut("i", modifiers: .command)
                Toggle("Use Composition Plan", isOn: $model.planEnabled)
                    .disabled(model.selectedModel == .v1 || model.isSoundEffect)
                    .keyboardShortcut("u", modifiers: .command)
                Divider()
                Button("Choose Model") { model.focus(.model) }
                    .keyboardShortcut("m", modifiers: [.command, .shift])
                Button("Choose Output Format") { model.focus(.outputFormat) }
                    .keyboardShortcut("f", modifiers: [.command, .shift])
            }
            CommandGroup(replacing: .help) {
                Button("ElevenLabs Music and Sound FX Generator Help") { model.openManual() }
                    .keyboardShortcut(KeyEquivalent("\u{F704}"), modifiers: [])
                Button("Project Page") { model.openProjectPage() }
                    .keyboardShortcut(KeyEquivalent("\u{F704}"), modifiers: .command)
                Button("Usage Analytics") { model.openUsageAnalytics() }
                Button("Donate") { model.openDonatePage() }
            }
        }
        Settings {
            SettingsView(model: model)
        }
    }
}
