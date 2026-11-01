// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Spelling;

namespace Craft.Editor;

// X part 9, item 22: the spell check of the lines this editor draws (comments and strings, or a prose file's text).
public sealed partial class EditorModel
{
    private SpellCheckLayer? _spelling;
    private SpellChecker? _spellChecker;
    private bool _spellServed;

    /// <summary>This editor's spell check: the unknown words of the lines it drew, kept per line.</summary>
    public SpellCheckLayer Spelling => _spelling ??= new SpellCheckLayer(this);

    /// <summary>The words the spell check knows (<see cref="SpellChecker.Shared"/> unless set); null turns it off for this editor.</summary>
    public SpellChecker? SpellChecker
    {
        get => _spellChecker ?? (_spellServed ? null : SpellChecker.Shared);
        set
        {
            _spellChecker = value;
            _spellServed = value is null;
            Invalidate();
        }
    }

    /// <summary>Whether a frame checks spelling: <c>CRAFT_SPELL_CHECK</c>, a checker, and an editable text (not a read-only surface).</summary>
    internal SpellChecker? SpellCheckerForFrame
    {
        get
        {
            if (!Options.SpellCheck || IsReadOnly || SpellChecker is not { } checker)
            {
                return null;
            }

            if (!_spellRegistered)
            {
                _spellRegistered = true;
                checker.Serve(this);
            }

            return checker;
        }
    }

    private bool _spellRegistered;

    /// <summary>What the spell check knows changed (a dictionary landed, a word was added): the editor is drawn again.</summary>
    internal void SpellingChanged()
    {
        if (!_disposed)
        {
            Invalidate();
        }
    }
}
