// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// The shortcuts a C# developer expects (the owner's note 16), as data: the editor's line commands with their default
/// keys (VS Code's), the extra keys of the shell's commands, the keys the editor answers itself (shown on the keyboard
/// page, never registered), and the Visual Studio keymap, which differs where the two editors differ.
/// <see cref="ShellEditCommands"/> registers the commands and the bindings; the shell's own commands register their first
/// key themselves (<see cref="ShellModel"/>).
/// </summary>
public static class StandardShortcuts
{
    /// <summary>The Visual Studio keymap's name in <see cref="EditorKeyMaps"/>.</summary>
    public const string VisualStudio = "visualstudio";

    /// <summary>Edit: Toggle line comment.</summary>
    public const string ToggleComment = "craft.editor.toggleComment";

    /// <summary>Edit: Comment lines.</summary>
    public const string Comment = "craft.editor.comment";

    /// <summary>Edit: Uncomment lines.</summary>
    public const string Uncomment = "craft.editor.uncomment";

    /// <summary>Edit: Duplicate lines.</summary>
    public const string DuplicateLines = "craft.editor.duplicateLines";

    /// <summary>Edit: Delete lines.</summary>
    public const string DeleteLines = "craft.editor.deleteLines";

    /// <summary>Edit: Move lines up.</summary>
    public const string MoveLinesUp = "craft.editor.moveLinesUp";

    /// <summary>Edit: Move lines down.</summary>
    public const string MoveLinesDown = "craft.editor.moveLinesDown";

    /// <summary>Edit: Copy lines up.</summary>
    public const string CopyLinesUp = "craft.editor.copyLinesUp";

    /// <summary>Edit: Copy lines down.</summary>
    public const string CopyLinesDown = "craft.editor.copyLinesDown";

    /// <summary>Edit: Indent lines.</summary>
    public const string IndentLines = "craft.editor.indentLines";

    /// <summary>Edit: Outdent lines.</summary>
    public const string OutdentLines = "craft.editor.outdentLines";

    /// <summary>Edit: Select the line.</summary>
    public const string SelectLine = "craft.editor.selectLine";

    /// <summary>Edit: Expand the selection.</summary>
    public const string ExpandSelection = "craft.editor.expandSelection";

    /// <summary>Edit: Shrink the selection.</summary>
    public const string ShrinkSelection = "craft.editor.shrinkSelection";

    /// <summary>Selection: Add the next occurrence (Ctrl+D; Visual Studio: Shift+Alt+.).</summary>
    public const string AddNextOccurrence = "craft.editor.addNextOccurrence";

    /// <summary>Selection: Select all occurrences (Ctrl+Shift+L, VS Code only).</summary>
    public const string SelectAllOccurrences = "craft.editor.selectAllOccurrences";

    /// <summary>Selection: Add a caret above (Ctrl+Alt+Up).</summary>
    public const string AddCaretAbove = "craft.editor.addCaretAbove";

    /// <summary>Selection: Add a caret below (Ctrl+Alt+Down).</summary>
    public const string AddCaretBelow = "craft.editor.addCaretBelow";

    /// <summary>Selection: Column select up (Ctrl+Shift+Alt+Up; Visual Studio: Shift+Alt+Up).</summary>
    public const string ColumnSelectUp = "craft.editor.columnSelectUp";

    /// <summary>Selection: Column select down.</summary>
    public const string ColumnSelectDown = "craft.editor.columnSelectDown";

    /// <summary>Selection: Column select left.</summary>
    public const string ColumnSelectLeft = "craft.editor.columnSelectLeft";

    /// <summary>Selection: Column select right.</summary>
    public const string ColumnSelectRight = "craft.editor.columnSelectRight";

    /// <summary>Selection: Back to one caret (Escape does it too).</summary>
    public const string SingleCaret = "craft.editor.singleCaret";

    /// <summary>Fold: Fold the region at the caret (Ctrl+Shift+[; Visual Studio: Ctrl+M Ctrl+M toggles).</summary>
    public const string Fold = "craft.editor.fold";

    /// <summary>Fold: Unfold the region at the caret (Ctrl+Shift+]).</summary>
    public const string Unfold = "craft.editor.unfold";

    /// <summary>Fold: Toggle the fold at the caret (Visual Studio's Ctrl+M Ctrl+M).</summary>
    public const string ToggleFold = "craft.editor.toggleFold";

    /// <summary>Fold: Fold all (Ctrl+K Ctrl+0; Visual Studio: Ctrl+M Ctrl+O).</summary>
    public const string FoldAll = "craft.editor.foldAll";

    /// <summary>Fold: Unfold all (Ctrl+K Ctrl+J; Visual Studio: Ctrl+M Ctrl+L and Ctrl+M Ctrl+P).</summary>
    public const string UnfoldAll = "craft.editor.unfoldAll";

    /// <summary>Edit: Quick fix (the code actions at the caret).</summary>
    public const string QuickFix = "craft.quickFix";

    /// <summary>The editor's line commands: id, title, what it does on the active editor, and whether the Edit menu lists it.</summary>
    public static IReadOnlyList<(string Id, string Title, Func<EditorModel, bool> Run, bool InMenu)> LineCommands { get; } =
    [
        (ToggleComment, "Edit: Toggle line comment", static m => m.ToggleLineComment(), true),
        (Comment, "Edit: Comment lines", static m => m.CommentLines(), false),
        (Uncomment, "Edit: Uncomment lines", static m => m.UncommentLines(), false),
        (DuplicateLines, "Edit: Duplicate lines", static m => m.DuplicateLines(), true),
        (DeleteLines, "Edit: Delete lines", static m => m.DeleteLines(), true),
        (MoveLinesUp, "Edit: Move lines up", static m => m.MoveLines(LineDirection.Up), true),
        (MoveLinesDown, "Edit: Move lines down", static m => m.MoveLines(LineDirection.Down), true),
        (CopyLinesUp, "Edit: Copy lines up", static m => m.CopyLines(LineDirection.Up), false),
        (CopyLinesDown, "Edit: Copy lines down", static m => m.CopyLines(LineDirection.Down), false),
        (IndentLines, "Edit: Indent lines", static m => m.IndentLines(), false),
        (OutdentLines, "Edit: Outdent lines", static m => m.OutdentLines(), false),
        (SelectLine, "Selection: Select the line", static m => m.SelectLine(), true),
        (ExpandSelection, "Selection: Expand", static m => m.ExpandSelection(), true),
        (ShrinkSelection, "Selection: Shrink", static m => m.ShrinkSelection(), false),
        (AddNextOccurrence, "Selection: Add the next occurrence", static m => m.AddNextOccurrence(), true),
        (SelectAllOccurrences, "Selection: Select all occurrences", static m => m.SelectAllOccurrences(), true),
        (AddCaretAbove, "Selection: Add a caret above", static m => m.AddCaretVertical(LineDirection.Up), true),
        (AddCaretBelow, "Selection: Add a caret below", static m => m.AddCaretVertical(LineDirection.Down), true),
        (ColumnSelectUp, "Selection: Column select up", static m => m.ColumnSelect(EditorKey.Up), false),
        (ColumnSelectDown, "Selection: Column select down", static m => m.ColumnSelect(EditorKey.Down), false),
        (ColumnSelectLeft, "Selection: Column select left", static m => m.ColumnSelect(EditorKey.Left), false),
        (ColumnSelectRight, "Selection: Column select right", static m => m.ColumnSelect(EditorKey.Right), false),
        (SingleCaret, "Selection: Back to one caret", static m => m.CollapseCarets() || true, false),
        (Fold, "Fold: Fold the region at the caret", static m => m.Folding.Fold(m.CaretLine) || true, true),
        (Unfold, "Fold: Unfold the region at the caret", static m => m.Folding.Unfold(m.CaretLine) || true, true),
        (ToggleFold, "Fold: Toggle the fold at the caret", static m => (m.Folding.Unfold(m.CaretLine) || m.Folding.Fold(m.CaretLine)) || true, false),
        (FoldAll, "Fold: Fold all", static m => m.Folding.FoldAll() >= 0, true),
        (UnfoldAll, "Fold: Unfold all", static m => m.Folding.UnfoldAll() || true, true),
    ];

    /// <summary>
    /// The default bindings registered beside the shell's own first keys (VS Code's where the two editors differ): the
    /// line commands in the text editor only, and more keys for the shell's commands.
    /// </summary>
    public static IReadOnlyList<KeyBinding> Bindings { get; } =
    [
        Editor("Ctrl+/", ToggleComment),
        Editor("Ctrl+K Ctrl+C", Comment),
        Editor("Ctrl+K Ctrl+U", Uncomment),
        Editor("Ctrl+Shift+K", DeleteLines),
        Editor("Alt+Up", MoveLinesUp),
        Editor("Alt+Down", MoveLinesDown),
        Editor("Shift+Alt+Up", CopyLinesUp),
        Editor("Shift+Alt+Down", CopyLinesDown),
        Editor("Ctrl+]", IndentLines),
        Editor("Ctrl+[", OutdentLines),
        Editor("Ctrl+L", SelectLine),
        Editor("Shift+Alt+Right", ExpandSelection),
        Editor("Shift+Alt+Left", ShrinkSelection),
        Editor("Ctrl+K Ctrl+D", "craft.formatDocument"),
        Editor("Ctrl+D", AddNextOccurrence),
        Editor("Ctrl+Shift+L", SelectAllOccurrences),
        Editor("Ctrl+Alt+Up", AddCaretAbove),
        Editor("Ctrl+Alt+Down", AddCaretBelow),
        Editor("Ctrl+Shift+Alt+Up", ColumnSelectUp),
        Editor("Ctrl+Shift+Alt+Down", ColumnSelectDown),
        Editor("Ctrl+Shift+Alt+Left", ColumnSelectLeft),
        Editor("Ctrl+Shift+Alt+Right", ColumnSelectRight),
        Editor("Ctrl+Shift+[", Fold),
        Editor("Ctrl+Shift+]", Unfold),
        Editor("Ctrl+K Ctrl+0", FoldAll),
        Editor("Ctrl+K Ctrl+J", UnfoldAll),
        Anywhere("Ctrl+.", QuickFix),
        Anywhere("F1", ShellCommandIds.CommandPalette),
        Anywhere("Ctrl+F4", ShellCommandIds.CloseTab),
        Anywhere("Ctrl+PageDown", ShellCommandIds.NextTab),
        Anywhere("Ctrl+PageUp", ShellCommandIds.PreviousTab),
        Anywhere("Ctrl+Shift+Alt+M", ConversationCommandIds.Expand),
        InComposer("Enter", ComposerCommandIds.Send),
        InComposer("Shift+Enter", ComposerCommandIds.NewLine),
        InComposer("Escape", ComposerCommandIds.Leave),
    ];

    /// <summary>
    /// The keys the editor answers itself (its own key handling, which every view of it keeps), by command: the keyboard
    /// page shows them as built in. A user's binding of one of these keys to another command takes it from the editor.
    /// </summary>
    public static IReadOnlyList<(string Keys, string CommandId)> EditorKeys { get; } =
    [
        ("Ctrl+Z", "craft.undo"), ("Ctrl+Shift+Z", "craft.redo"), ("Ctrl+Y", "craft.redo"), ("Ctrl+X", "craft.cut"), ("Ctrl+C", "craft.copy"),
        ("Ctrl+V", "craft.paste"), ("Ctrl+A", "craft.selectAll"), ("Ctrl+F", "craft.find"), ("Ctrl+H", "craft.replace"),
        ("F3", "craft.findNext"), ("Shift+F3", "craft.findPrevious"), ("Shift+Alt+F", "craft.formatDocument"), ("F12", "craft.goToDefinition"),
        ("Ctrl+Space", "craft.triggerCompletion"), ("Ctrl+Shift+Space", "craft.parameterHints"), ("Shift+Tab", OutdentLines),
        ("Ctrl+S", ShellCommandIds.Save),
    ];

    /// <summary>The Visual Studio keymap: its keys where Visual Studio's differ from VS Code's (an empty command unbinds).</summary>
    public static KeyMap VisualStudioKeyMap { get; } = new(VisualStudio, "Visual Studio",
    [
        Editor("Ctrl+D", DuplicateLines),
        Editor("Ctrl+Shift+L", DeleteLines),
        Editor("Shift+Alt+.", AddNextOccurrence),
        Editor("Shift+Alt+Up", ColumnSelectUp),
        Editor("Shift+Alt+Down", ColumnSelectDown),
        Editor("Ctrl+M Ctrl+M", ToggleFold),
        Editor("Ctrl+M Ctrl+O", FoldAll),
        Editor("Ctrl+M Ctrl+L", UnfoldAll),
        Editor("Ctrl+M Ctrl+P", UnfoldAll),
        Editor("Ctrl+K Ctrl+/", ToggleComment),
        Editor("Shift+Alt+=", ExpandSelection),
        Editor("Shift+Alt+-", ShrinkSelection),
        Editor("Shift+Alt+Right", ColumnSelectRight),
        Editor("Shift+Alt+Left", ColumnSelectLeft),
        Anywhere("Ctrl+Shift+S", ShellCommandIds.SaveAll),
        Anywhere("Ctrl+T", ShellCommandIds.QuickOpen),
        Anywhere("Ctrl+Q", ShellCommandIds.CommandPalette),
        Anywhere("Ctrl+Alt+L", ShellCommandIds.ToggleTree),
        Anywhere("Ctrl+Alt+PageDown", ShellCommandIds.NextTab),
        Anywhere("Ctrl+Alt+PageUp", ShellCommandIds.PreviousTab),
        Anywhere("Ctrl+Shift+.", ShellCommandIds.ZoomIn),
        Anywhere("Ctrl+Shift+,", ShellCommandIds.ZoomOut),
        Anywhere("Alt+Enter", QuickFix),
    ]);

    /// <summary>A binding of <paramref name="keys"/> to <paramref name="commandId"/> in the text editor only.</summary>
    public static KeyBinding Editor(string keys, string commandId)
    {
        var (chord, then) = KeyBinding.ParseKeys(keys);
        return new KeyBinding(chord, commandId, Then: then, When: KeyContext.TextEditor);
    }

    /// <summary>A binding of <paramref name="keys"/> to <paramref name="commandId"/> in a composer (the chat's or the agent's message box) only.</summary>
    public static KeyBinding InComposer(string keys, string commandId)
    {
        var (chord, then) = KeyBinding.ParseKeys(keys);
        return new KeyBinding(chord, commandId, Then: then, When: KeyContext.Composer);
    }

    /// <summary>A binding of <paramref name="keys"/> to <paramref name="commandId"/> wherever the key arrives.</summary>
    public static KeyBinding Anywhere(string keys, string commandId)
    {
        var (chord, then) = KeyBinding.ParseKeys(keys);
        return new KeyBinding(chord, commandId, Then: then);
    }
}
