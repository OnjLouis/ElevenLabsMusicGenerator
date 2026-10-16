import AppKit

@MainActor
enum VersionHistory {
    static let text = """
    1.5.0

    Added Video Soundtrack: combine ordered videos, request music and sound effects, use original audio as a reference, and save a mixed soundtrack or separate tracks. Open it from File with Command-Shift-V.
    Soundtrack progress shows upload, waiting and saving stages with elapsed time. Command-Shift-R opens a stable, readable progress snapshot; generation can take several minutes even for a short video.

    1.4.4

    Maintenance release. The Windows app gains multi-letter dropdown navigation; Mac controls are unchanged.

    """

    static func make() -> (NSAlert, NSTextView) {
        let alert = NSAlert()
        alert.messageText = "Version history"
        alert.addButton(withTitle: "Close").keyEquivalent = "\u{1b}"
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 600, height: 300))
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        let editor = NSTextView(frame: scroll.bounds)
        editor.isEditable = false
        editor.isSelectable = true
        editor.isRichText = false
        editor.isHorizontallyResizable = false
        editor.isVerticallyResizable = true
        editor.textContainer?.widthTracksTextView = true
        editor.autoresizingMask = [.width]
        editor.font = .systemFont(ofSize: NSFont.systemFontSize)
        editor.string = text
        editor.setAccessibilityLabel("Version history")
        scroll.documentView = editor
        alert.accessoryView = scroll
        alert.window.initialFirstResponder = editor
        return (alert, editor)
    }

    static func show() {
        let (alert, editor) = make()
        let window = alert.window
        DispatchQueue.main.async { window.makeFirstResponder(editor) }
        alert.runModal()
    }
}
