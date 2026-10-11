import AppKit

@MainActor
enum VersionHistory {
    static let text = """
    1.4.3

    Version History is now available from the Help menu, including when offline.
    The app opens with keyboard focus in the prompt editor.

    1.4.2

    - Shorter screen-reader announcements for Generate, Play and Stop, with detailed explanations still available through F1.
    - The window title shows the prompt character count without repeating it in the prompt description.
    - Successful generation updates the status log without an OK dialog. Audio settings offers an optional completion sound or automatic playback of new recordings.
    - Windows context help provides specific descriptions throughout the main window, Preferences and composition-plan editor.
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
