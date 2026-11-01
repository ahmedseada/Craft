// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;

namespace Craft.Web.Script;

// What the token before the next one was, carried in a frame's state across lines: it decides a '/' (a regular
// expression or a division), a '{' (a block or an object), a '<' (JSX or less-than) and the class of the next name.
internal enum Prev : byte
{
    StatementStart,     // the start, ';', a block's '{': regex, '{' a block
    OperatorStart,      // an operator, '(', ',', '[', '=': regex, '{' an object
    Value,              // a name, a literal, ')' '[' '}' of an expression: division
    ControlParenClose,  // the ')' of if/while/for/with: regex
    BlockClose,         // the '}' of a block: regex
    KeywordExpr,        // return, typeof, case, new, ...: regex, '{' an object
    KeywordBlock,       // else, do, try, finally: regex, '{' a block
    Arrow,              // '=>': regex, '{' a block
    Dot,                // '.' or '?.': the next word is a member's name
    ExpectClass,        // after class, extends, new: a class's name
    ExpectInterface,    // after interface (TS)
    ExpectEnum,         // after enum
    ExpectType,         // after a type alias's 'type' (TS)
    ExpectNamespace,    // after namespace or module (TS)
    ExpectFunction,     // after function
    ImportExport,       // after import or export
    Modifier,           // after static, get, set, public, readonly, ...: a member's start
    ControlKeyword,     // if, while, for, with, switch, catch: their '(' is a control paren
    ExpectImplements,   // after implements: an interface's name
    Pipe,               // an Angular pipe's '|': the next name is the pipe's
    TagName,            // JSX: a tag's name is next
    TagAttributes,      // JSX: a tag's attributes
}

// When a word is a keyword (else it is a plain name).
internal enum WordRule : byte
{
    Always,             // a reserved word
    Modifier,           // followed (after white space) by a name, '#', '[', '*', a quote or '{'
    AfterValue,         // after a value and followed by a name or a type (as, satisfies, is)
    From,               // followed by a quote
    Async,              // followed (after white space) by a name, '(' or function
    Using,              // a declaration: followed by a name, at a statement's start or after await
    Type,               // TS's type alias or type-only import and export
    TypeWord,           // TS's predefined types (string, number, ...): not before '=', '(', ':' or '.'
    Of,                 // in a for's parentheses after a name
    Out,                // TS's variance: after '<' or ',' and followed by a name
    ImportPhase,        // import defer / import source
    Unique,             // unique symbol
    Global,             // declare global {
}

internal readonly record struct Word(WordRule Rule, Prev Next, bool Control, bool TypeScriptOnly, int MinEcmaScript, int MinTypeScript);

// The words of JavaScript, TypeScript and Angular expressions, with their rule, the token they leave, and the versions
// that read them (a FrozenDictionary looked up by span: rule 15, nothing allocated).
internal static class ScriptWords
{
    public static readonly FrozenDictionary<string, Word> All = Make();

    public static readonly FrozenDictionary<string, Word>.AlternateLookup<ReadOnlySpan<char>> Lookup = All.GetAlternateLookup<ReadOnlySpan<char>>();

    // Words that follow a value: a modifier followed by one of them is a name ("type in x").
    private static readonly FrozenSet<string> Binary = FrozenSet.Create(StringComparer.Ordinal, "in", "of", "instanceof", "as", "satisfies", "extends", "implements");

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> BinaryLookup = Binary.GetAlternateLookup<ReadOnlySpan<char>>();

    // The words that are values (true, this): what follows them divides.
    private static readonly FrozenSet<string> Values = FrozenSet.Create(StringComparer.Ordinal, "this", "super", "true", "false", "null", "undefined");

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ValueLookup = Values.GetAlternateLookup<ReadOnlySpan<char>>();

    private static FrozenDictionary<string, Word> Make()
    {
        var words = new Dictionary<string, Word>(StringComparer.Ordinal);
        void Js(string name, Prev next, bool control = false, WordRule rule = WordRule.Always, int es = 0, int ts = 0) =>
            words[name] = new Word(rule, next, control, false, es, ts);
        void Ts(string name, Prev next, WordRule rule = WordRule.Modifier, int ts = 0) =>
            words[name] = new Word(rule, next, false, true, 0, ts);

        // Reserved words (ES2025).
        foreach (var name in (string[])["if", "while", "for", "with", "switch", "catch"])
        {
            Js(name, Prev.ControlKeyword, control: true);
        }

        foreach (var name in (string[])["else", "do", "try", "finally"])
        {
            Js(name, Prev.KeywordBlock, control: true);
        }

        foreach (var name in (string[])["return", "throw", "case", "default", "yield", "await", "break", "continue"])
        {
            Js(name, Prev.KeywordExpr, control: true);
        }

        foreach (var name in (string[])["typeof", "delete", "void", "in", "instanceof", "debugger"])
        {
            Js(name, Prev.KeywordExpr);
        }

        foreach (var name in (string[])["this", "super", "true", "false", "null", "undefined"])
        {
            Js(name, Prev.Value);
        }

        Js("new", Prev.ExpectClass);
        Js("class", Prev.ExpectClass);
        Js("extends", Prev.ExpectClass);
        Js("implements", Prev.ExpectImplements);
        Js("interface", Prev.ExpectInterface);
        Js("enum", Prev.ExpectEnum);
        Js("function", Prev.ExpectFunction);
        Js("import", Prev.ImportExport);
        Js("export", Prev.ImportExport);
        foreach (var name in (string[])["const", "let", "var"])
        {
            Js(name, Prev.OperatorStart);
        }

        foreach (var name in (string[])["package", "private", "protected", "public"])
        {
            Js(name, Prev.Modifier);
        }

        // Contextual words.
        Js("static", Prev.Modifier, rule: WordRule.Modifier);
        Js("get", Prev.Modifier, rule: WordRule.Modifier);
        Js("set", Prev.Modifier, rule: WordRule.Modifier);
        Js("async", Prev.OperatorStart, rule: WordRule.Async);
        Js("of", Prev.KeywordExpr, rule: WordRule.Of);
        Js("as", Prev.OperatorStart, rule: WordRule.AfterValue);
        Js("from", Prev.OperatorStart, rule: WordRule.From);
        Js("using", Prev.OperatorStart, rule: WordRule.Using, es: 2026, ts: 502);                    // ES2026 (confirm), TS 5.2
        Js("accessor", Prev.Modifier, rule: WordRule.Modifier, es: ScriptVersion.Latest, ts: 409);   // decorators' auto-accessors: stage 3, TS 4.9
        Js("defer", Prev.OperatorStart, rule: WordRule.ImportPhase, es: ScriptVersion.Latest, ts: 509); // import defer: stage 3 (confirm), TS 5.9 (confirm)
        Js("source", Prev.OperatorStart, rule: WordRule.ImportPhase, es: ScriptVersion.Latest, ts: ScriptVersion.Latest); // source phase imports: stage 3 (confirm)

        // TypeScript's words.
        Ts("abstract", Prev.Modifier);
        Ts("declare", Prev.Modifier);
        Ts("readonly", Prev.Modifier, ts: 200);
        Ts("override", Prev.Modifier, ts: 403);
        Ts("namespace", Prev.ExpectNamespace, ts: 105);
        Ts("module", Prev.ExpectNamespace);
        Ts("global", Prev.ExpectNamespace, WordRule.Global);
        Ts("type", Prev.ExpectType, WordRule.Type);
        Ts("keyof", Prev.OperatorStart, ts: 201);
        Ts("infer", Prev.OperatorStart, ts: 208);
        Ts("unique", Prev.OperatorStart, WordRule.Unique, ts: 207);
        Ts("asserts", Prev.OperatorStart, ts: 307);
        Ts("is", Prev.OperatorStart, WordRule.AfterValue);
        Ts("satisfies", Prev.OperatorStart, WordRule.AfterValue, ts: 409);
        Ts("out", Prev.OperatorStart, WordRule.Out, ts: 407);
        foreach (var name in (string[])["string", "number", "boolean", "any", "unknown", "never", "object", "symbol", "bigint"])
        {
            Ts(name, Prev.Value, WordRule.TypeWord);
        }

        return words.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
