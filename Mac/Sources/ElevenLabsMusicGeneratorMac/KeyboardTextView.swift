import AppKit
import SwiftUI

final class TabAwareTextView: NSTextView {
    var tabAction: (() -> Void)?
    var backTabAction: (() -> Void)?

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 48 && event.modifierFlags.intersection([.command, .option, .control]).isEmpty {
            if event.modifierFlags.contains(.shift) { backTabAction?() }
            else { tabAction?() }
            return
        }
        super.keyDown(with: event)
    }
}

struct KeyboardTextView: NSViewRepresentable {
    @Binding var text: String
    var editable: Bool
    var accessibilityLabel: String
    var accessibilityHelp: String
    var onTab: () -> Void
    var onBackTab: () -> Void
    var onReady: (TabAwareTextView) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(parent: self) }

    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        scroll.autohidesScrollers = true
        let editor = TabAwareTextView(frame: .zero)
        editor.isRichText = false
        editor.isEditable = editable
        editor.isSelectable = true
        editor.allowsUndo = editable
        editor.font = .systemFont(ofSize: NSFont.systemFontSize)
        editor.textContainerInset = NSSize(width: 7, height: 7)
        editor.isHorizontallyResizable = false
        editor.isVerticallyResizable = true
        editor.textContainer?.widthTracksTextView = true
        editor.autoresizingMask = [.width]
        editor.string = text
        editor.setAccessibilityLabel(accessibilityLabel)
        editor.setAccessibilityHelp(accessibilityHelp)
        editor.delegate = context.coordinator
        editor.tabAction = onTab
        editor.backTabAction = onBackTab
        scroll.documentView = editor
        DispatchQueue.main.async { onReady(editor) }
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let editor = scroll.documentView as? TabAwareTextView else { return }
        context.coordinator.parent = self
        editor.tabAction = onTab
        editor.backTabAction = onBackTab
        editor.isEditable = editable
        editor.setAccessibilityHelp(accessibilityHelp)
        if editor.string != text {
            let focused = editor.window?.firstResponder === editor
            let selection = editor.selectedRange()
            editor.string = text
            let location = focused ? min(selection.location, (text as NSString).length) : (text as NSString).length
            editor.setSelectedRange(NSRange(location: location, length: 0))
            if !focused { editor.scrollToEndOfDocument(nil) }
        }
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: KeyboardTextView
        init(parent: KeyboardTextView) { self.parent = parent }

        func textDidChange(_ notification: Notification) {
            guard let editor = notification.object as? NSTextView, parent.editable else { return }
            parent.text = editor.string
        }
    }
}
