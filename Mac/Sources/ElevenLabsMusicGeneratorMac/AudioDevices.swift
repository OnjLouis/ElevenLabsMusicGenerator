import AppKit
import CoreAudio
import SwiftUI

struct PlaybackDevice: Identifiable { let id: String; let name: String }

enum AudioDevices {
    static func list() -> [PlaybackDevice] {
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDevices, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain), size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size) == noErr else { return [] }
        var ids = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.compactMap { id in
            var stream = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreams, mScope: kAudioDevicePropertyScopeOutput, mElement: kAudioObjectPropertyElementMain), bytes: UInt32 = 0
            guard AudioObjectGetPropertyDataSize(id, &stream, 0, nil, &bytes) == noErr, bytes > 0 else { return nil }
            func string(_ selector: AudioObjectPropertySelector) -> String? {
                var a = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain), s = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
                let value = UnsafeMutablePointer<Unmanaged<CFString>?>.allocate(capacity: 1); value.initialize(to: nil)
                defer { value.deinitialize(count: 1); value.deallocate() }
                guard AudioObjectGetPropertyData(id, &a, 0, nil, &s, value) == noErr else { return nil }; return value.pointee?.takeRetainedValue() as String?
            }
            guard let uid = string(kAudioDevicePropertyDeviceUID), let name = string(kAudioObjectPropertyName) else { return nil }; return PlaybackDevice(id: uid, name: name)
        }
    }
}

struct AudioSettingsView: View {
    @ObservedObject var model: AppModel
    @State private var devices: [PlaybackDevice] = []
    var body: some View {
        Form {
            Picker("Playback Device", selection: Binding(get: { model.preferences.playbackDevice ?? "" }, set: { model.stopPlayback(); model.preferences.playbackDevice = $0 })) {
                Text("System default").tag("")
                ForEach(devices) { Text($0.name).tag($0.id) }
            }.accessibilityHint("Choose the output for this app's audio playback.")
            Picker("Default Output Format", selection: $model.preferences.format) {
                ForEach(AudioFormat.allCases.filter { !model.isSoundEffect || !$0.rawValue.hasPrefix("mp3_48000") }) { Text($0.title).tag($0.rawValue) }
            }.accessibilityHint("Choose the format for generated audio. The main window can change it too.")
            Toggle("Play new generations automatically in sequence", isOn: Binding(get: { model.preferences.autoPlayGenerations == true }, set: { model.stopPlayback(); model.preferences.autoPlayGenerations = $0 }))
                .accessibilityHint("Off by default. Play only the new audio from a successfully completed batch, one file at a time. Stop ends the queue.")
            Toggle("Play a completion sound", isOn: Binding(get: { model.preferences.completionSound == true }, set: { model.preferences.completionSound = $0 }))
                .accessibilityHint("Play a sound when all requested generations finish successfully.")
        }.padding(18).onAppear { devices = AudioDevices.list() }
    }
}
