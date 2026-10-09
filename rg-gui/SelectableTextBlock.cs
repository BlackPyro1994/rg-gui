using System.Reflection;
using System.Windows.Controls;
using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace rg_gui
{
    // https://stackoverflow.com/a/45627524

    public class SelectableTextBlock : TextBlock
    {
        // Reflection types, properties, and methods
        private static readonly Type TextEditorType = Type.GetType("System.Windows.Documents.TextEditor, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
        private static readonly PropertyInfo TextEditorIsReadOnly = TextEditorType.GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo TextEditorTextView = TextEditorType.GetProperty("TextView", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo TextEditorSelection = TextEditorType.GetProperty("Selection", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo TextEditorRegisterCommandHandlersMethod = TextEditorType.GetMethod("RegisterCommandHandlers", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Type), typeof(bool), typeof(bool), typeof(bool) }, null);

        private static readonly Type TextContainerType = Type.GetType("System.Windows.Documents.ITextContainer, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
        private static readonly PropertyInfo TextContainerTextView = TextContainerType.GetProperty("TextView");

        private static readonly PropertyInfo TextBlockTextContainer = typeof(TextBlock).GetProperty("TextContainer", BindingFlags.Instance | BindingFlags.NonPublic);

        static SelectableTextBlock()
        {
            // Make this control focusable
            FocusableProperty.OverrideMetadata(typeof(SelectableTextBlock), new FrameworkPropertyMetadata(true));

            // Show the text cursor before the TextEditor (which would otherwise provide it) is created.
            CursorProperty.OverrideMetadata(typeof(SelectableTextBlock), new FrameworkPropertyMetadata(Cursors.IBeam));

            // Register class event handlers
            // (Type controlType, bool acceptsRichContent, bool readOnly, bool registerEventListeners)
            TextEditorRegisterCommandHandlersMethod.Invoke(null, new object[] { typeof(SelectableTextBlock), false, true, true });
        }

        private object? _textEditor;

        // Creating the TextEditor is expensive and made scrolling long result lists stall, so it is only
        // created when this text is clicked or focused, not for every line that scrolls into view or under the mouse.
        private void EnsureTextEditor()
        {
            if (_textEditor != null)
            {
                return;
            }

            var textContainer = TextBlockTextContainer.GetValue(this);

            // Create TextEditor instance, assign the TextContainer to it.
            // (ITextContainer textContainer, FrameworkElement uiScope, bool isUndoEnabled)
            _textEditor = Activator.CreateInstance(TextEditorType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance, null, new[] { textContainer, this, false }, null);

            // Set IsReadOnly and TextView properties.
            var textView = TextContainerTextView.GetValue(textContainer);
            TextEditorIsReadOnly.SetValue(_textEditor, true);
            TextEditorTextView.SetValue(_textEditor, textView);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            EnsureTextEditor();
            base.OnPreviewMouseDown(e);
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            EnsureTextEditor();
            base.OnGotKeyboardFocus(e);
        }

        public bool HasSelectedText => _textEditor != null && TextEditorSelection?.GetValue(_textEditor) is TextRange { IsEmpty: false };
    }
}
