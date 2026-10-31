// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>The classes C# adds to <see cref="TokenClasses"/>, named as Roslyn's <c>ClassificationTypeNames</c> names them.</summary>
public static class CSharpTokenClasses
{
    /// <summary>An enum member's name.</summary>
    public const string EnumMemberName = "enum member name";

    /// <summary>A constant's name.</summary>
    public const string ConstantName = "constant name";

    /// <summary>An event's name.</summary>
    public const string EventName = "event name";

    /// <summary>A label's name.</summary>
    public const string LabelName = "label name";

    /// <summary>An extension method's name.</summary>
    public const string ExtensionMethodName = "extension method name";

    /// <summary>A comment inside a documentation comment.</summary>
    public const string DocCommentComment = "xml doc comment - comment";
}

/// <summary>
/// The syntactic classifier: walks the tokens and trivia of a syntax tree that overlap a range and adds their classes,
/// following Roslyn's own syntactic classifier (its keyword, control-keyword, punctuation and operator choices, and the
/// identifiers it can name from syntax alone: declarations, type parameters, parameters, locals), with the additions the
/// editor asks of syntax (an invoked simple name is a method name; <c>var</c>, <c>nameof</c>, a setter's <c>value</c>,
/// <c>notnull</c> and <c>unmanaged</c> in their positions are keywords; escapes inside strings). It allocates nothing of
/// its own per token: names are shared constants, token texts are Roslyn's.
/// </summary>
public static class CSharpClassifier
{
    /// <summary>
    /// Adds the spans of <paramref name="root"/>'s tokens and trivia overlapping <paramref name="start"/> to
    /// <paramref name="start"/> + <paramref name="length"/>, in order. It finds the range's first token by position
    /// (descending by the green tree's widths) and walks to the next token from there, so only the nodes on the way are
    /// made, not every sibling before the range (as <c>DescendantTokens</c> does: on a fresh tree of a large file, a red node
    /// per type above the visible lines). The tokens are the same as <c>DescendantTokens(range)</c>'s (proven in the tests).
    /// </summary>
    public static void Classify(SyntaxNode root, int start, int length, TokenSpanBuffer into)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(into);
        var range = new TextSpan(start, length);
        var full = root.FullSpan;
        if (length == 0 || start < full.Start || start >= full.End)
        {
            // The edges (an empty range, a range outside the root) as Roslyn's own walk answers them.
            ClassifyByDescendants(root, start, length, into);
            return;
        }

        int end = range.End;
        for (var token = root.FindToken(start); token.RawKind != 0 && token.FullSpan.Start < end; token = token.GetNextToken(includeZeroWidth: true))
        {
            TokenWithTrivia(token, range, into);
        }
    }

    /// <summary>As <see cref="Classify"/>, through Roslyn's <c>DescendantTokens(range)</c>: the reference the tests compare it with.</summary>
    internal static void ClassifyByDescendants(SyntaxNode root, int start, int length, TokenSpanBuffer into)
    {
        var range = new TextSpan(start, length);
        foreach (var token in root.DescendantTokens(range))
        {
            TokenWithTrivia(token, range, into);
        }
    }

    private static void TokenWithTrivia(SyntaxToken token, TextSpan range, TokenSpanBuffer into)
    {
        if (token.HasLeadingTrivia)
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                if (trivia.FullSpan.IntersectsWith(range))
                {
                    Trivia(trivia, range, into);
                }
            }
        }

        if (token.Span.Length > 0 && token.Span.IntersectsWith(range))
        {
            Token(token, into);
        }

        if (token.HasTrailingTrivia)
        {
            foreach (var trivia in token.TrailingTrivia)
            {
                if (trivia.FullSpan.IntersectsWith(range))
                {
                    Trivia(trivia, range, into);
                }
            }
        }
    }

    private static void Trivia(SyntaxTrivia trivia, TextSpan range, TokenSpanBuffer into)
    {
        switch (trivia.Kind())
        {
            case SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia:
                return;
            case SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia or SyntaxKind.ConflictMarkerTrivia:
                Add(into, trivia.Span, TokenClasses.Comment);
                return;
            case SyntaxKind.DisabledTextTrivia:
                Add(into, trivia.Span, TokenClasses.ExcludedCode);
                return;
            case SyntaxKind.DocumentationCommentExteriorTrivia:
                Add(into, trivia.Span, TokenClasses.DocCommentTag);
                return;
            case SyntaxKind.PreprocessingMessageTrivia:
                Add(into, trivia.Span, TokenClasses.PreprocessorText);
                return;
            case SyntaxKind.SkippedTokensTrivia:
                if (trivia.GetStructure() is { } skipped)
                {
                    foreach (var token in skipped.DescendantTokens())
                    {
                        TokenWithTrivia(token, range, into);
                    }
                }

                return;
            case SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia:
                if (trivia.GetStructure() is { } documentation)
                {
                    foreach (var token in documentation.DescendantTokens(range))
                    {
                        DocumentationToken(token, range, into);
                    }
                }

                return;
        }

        if (trivia.IsDirective && trivia.GetStructure() is DirectiveTriviaSyntax directive)
        {
            if (directive.IsKind(SyntaxKind.ShebangDirectiveTrivia))
            {
                Add(into, trivia.Span, TokenClasses.Comment);
                return;
            }

            foreach (var token in directive.DescendantTokens(range))
            {
                DirectiveToken(token, range, into);
            }
        }
    }

    // ---- documentation comments: text vs tags and delimiters; a cref's code as code ----

    private static void DocumentationToken(SyntaxToken token, TextSpan range, TokenSpanBuffer into)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            if (trivia.FullSpan.IntersectsWith(range))
            {
                Trivia(trivia, range, into);
            }
        }

        if (token.Span.Length > 0 && token.Span.IntersectsWith(range))
        {
            switch (token.Kind())
            {
                case SyntaxKind.XmlTextLiteralNewLineToken:
                    break;
                case SyntaxKind.XmlTextLiteralToken or SyntaxKind.XmlEntityLiteralToken:
                    Add(into, token.Span, token.Parent is XmlCommentSyntax ? CSharpTokenClasses.DocCommentComment : TokenClasses.DocComment);
                    break;
                default:
                    if (InCode(token))
                    {
                        Token(token, into);
                    }
                    else
                    {
                        Add(into, token.Span, TokenClasses.DocCommentTag);
                    }

                    break;
            }
        }

        foreach (var trivia in token.TrailingTrivia)
        {
            if (trivia.FullSpan.IntersectsWith(range))
            {
                Trivia(trivia, range, into);
            }
        }
    }

    private static bool InCode(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null and not DocumentationCommentTriviaSyntax; node = node.Parent)
        {
            if (node is CrefSyntax or IdentifierNameSyntax { Parent: XmlNameAttributeSyntax })
            {
                return true;
            }
        }

        return false;
    }

    // ---- preprocessor directives: keywords vs text; a condition's code as code ----

    private static void DirectiveToken(SyntaxToken token, TextSpan range, TokenSpanBuffer into)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            if (trivia.FullSpan.IntersectsWith(range))
            {
                Trivia(trivia, range, into);
            }
        }

        if (token.Span.Length > 0 && token.Span.IntersectsWith(range))
        {
            var kind = token.Kind();
            if (kind == SyntaxKind.HashToken)
            {
                Add(into, token.Span, TokenClasses.PreprocessorKeyword);
            }
            else if (SyntaxFacts.IsKeywordKind(kind) || SyntaxFacts.IsPreprocessorKeyword(kind))
            {
                Add(into, token.Span, token.Parent is ExpressionSyntax ? TokenClasses.Keyword : TokenClasses.PreprocessorKeyword);
            }
            else
            {
                Token(token, into);
            }
        }

        foreach (var trivia in token.TrailingTrivia)
        {
            if (trivia.FullSpan.IntersectsWith(range))
            {
                Trivia(trivia, range, into);
            }
        }
    }

    // ---- tokens ----

    private static void Token(SyntaxToken token, TokenSpanBuffer into)
    {
        var kind = token.Kind();
        switch (kind)
        {
            case SyntaxKind.IdentifierToken:
                Add(into, token.Span, Identifier(token));
                return;
            case SyntaxKind.NumericLiteralToken:
                Add(into, token.Span, TokenClasses.Number);
                return;
            case SyntaxKind.CharacterLiteralToken:
                StringWithEscapes(token.Text, token.SpanStart, 1, token.Text.Length - 1, TokenClasses.String, Escapes.Backslash, into);
                return;
            case SyntaxKind.StringLiteralToken or SyntaxKind.Utf8StringLiteralToken:
                {
                    var text = token.Text;
                    if (text.Length > 0 && text[0] == '@')
                    {
                        // @"..."": the doubled quote is its escape; the content runs from after @" to before the closing quote.
                        int close = text.LastIndexOf('"');
                        StringWithEscapes(text, token.SpanStart, 2, Math.Max(2, close), TokenClasses.VerbatimString, Escapes.DoubledQuote, into);
                    }
                    else
                    {
                        int close = text.LastIndexOf('"');
                        StringWithEscapes(text, token.SpanStart, 1, Math.Max(1, close), TokenClasses.String, Escapes.Backslash, into);
                    }

                    return;
                }

            case SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.Utf8SingleLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken
                or SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedSingleLineRawStringStartToken
                or SyntaxKind.InterpolatedMultiLineRawStringStartToken:
                Add(into, token.Span, TokenClasses.String);
                return;
            case SyntaxKind.InterpolatedVerbatimStringStartToken:
                Add(into, token.Span, TokenClasses.VerbatimString);
                return;
            case SyntaxKind.InterpolatedStringEndToken or SyntaxKind.InterpolatedRawStringEndToken:
                Add(into, token.Span, InterpolatedKind(token) == SyntaxKind.InterpolatedVerbatimStringStartToken ? TokenClasses.VerbatimString : TokenClasses.String);
                return;
            case SyntaxKind.InterpolatedStringTextToken:
                {
                    var start = InterpolatedKind(token);
                    var text = token.Text;
                    if (start == SyntaxKind.InterpolatedVerbatimStringStartToken)
                    {
                        StringWithEscapes(text, token.SpanStart, 0, text.Length, TokenClasses.VerbatimString, Escapes.DoubledQuote | Escapes.DoubledBrace, into);
                    }
                    else if (start == SyntaxKind.InterpolatedStringStartToken)
                    {
                        StringWithEscapes(text, token.SpanStart, 0, text.Length, TokenClasses.String, Escapes.Backslash | Escapes.DoubledBrace, into);
                    }
                    else
                    {
                        Add(into, token.Span, TokenClasses.String);
                    }

                    return;
                }
        }

        if (SyntaxFacts.IsKeywordKind(kind))
        {
            Add(into, token.Span, IsControlKeyword(token, kind) ? TokenClasses.ControlKeyword : TokenClasses.Keyword);
            return;
        }

        if (SyntaxFacts.IsPunctuation(kind))
        {
            Add(into, token.Span, Punctuation(token, kind));
        }
    }

    [Flags]
    private enum Escapes
    {
        Backslash = 1,
        DoubledQuote = 2,
        DoubledBrace = 4,
    }

    /// <summary>
    /// Adds a string token as runs of <paramref name="stringClass"/> and <see cref="TokenClasses.StringEscape"/>: the
    /// escapes are looked for from <paramref name="from"/> to <paramref name="to"/> of its text; the rest is string.
    /// </summary>
    private static void StringWithEscapes(string text, int position, int from, int to, string stringClass, Escapes escapes, TokenSpanBuffer into)
    {
        int run = 0;
        to = Math.Min(to, text.Length);
        for (int i = Math.Max(0, from); i < to; i++)
        {
            char c = text[i];
            int length = 0;
            if (c == '\\' && (escapes & Escapes.Backslash) != 0 && i + 1 < to)
            {
                length = 2;
                int digits = text[i + 1] switch { 'x' => 4, 'u' => 4, 'U' => 8, _ => 0 };
                if (digits > 0)
                {
                    int end = i + 2;
                    while (end < to && end < i + 2 + digits && char.IsAsciiHexDigit(text[end]))
                    {
                        end++;
                    }

                    length = end - i;
                }
            }
            else if (c == '"' && (escapes & Escapes.DoubledQuote) != 0 && i + 1 < to && text[i + 1] == '"')
            {
                length = 2;
            }
            else if (c is '{' or '}' && (escapes & Escapes.DoubledBrace) != 0 && i + 1 < to && text[i + 1] == c)
            {
                length = 2;
            }

            if (length > 0)
            {
                into.Add(position + run, i - run, stringClass);
                into.Add(position + i, length, TokenClasses.StringEscape);
                i += length - 1;
                run = i + 1;
            }
        }

        into.Add(position + run, text.Length - run, stringClass);
    }

    private static SyntaxKind InterpolatedKind(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is InterpolatedStringExpressionSyntax interpolated)
            {
                return interpolated.StringStartToken.Kind();
            }
        }

        return SyntaxKind.InterpolatedStringStartToken;
    }

    /// <summary>Roslyn's control keywords: a flow keyword in the statement, clause, label or expression it controls.</summary>
    private static bool IsControlKeyword(SyntaxToken token, SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.IfKeyword or SyntaxKind.ElseKeyword or SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword or SyntaxKind.ForEachKeyword
                or SyntaxKind.DoKeyword or SyntaxKind.SwitchKeyword or SyntaxKind.CaseKeyword or SyntaxKind.TryKeyword or SyntaxKind.CatchKeyword
                or SyntaxKind.FinallyKeyword or SyntaxKind.GotoKeyword or SyntaxKind.BreakKeyword or SyntaxKind.ContinueKeyword
                or SyntaxKind.ReturnKeyword or SyntaxKind.ThrowKeyword or SyntaxKind.YieldKeyword or SyntaxKind.DefaultKeyword:
                break;
            default:
                return false;
        }

        return token.Parent?.Kind() switch
        {
            SyntaxKind.GotoStatement or SyntaxKind.GotoCaseStatement or SyntaxKind.GotoDefaultStatement or SyntaxKind.BreakStatement
                or SyntaxKind.ContinueStatement or SyntaxKind.ReturnStatement or SyntaxKind.YieldReturnStatement or SyntaxKind.YieldBreakStatement
                or SyntaxKind.ThrowStatement or SyntaxKind.ThrowExpression or SyntaxKind.WhileStatement or SyntaxKind.DoStatement
                or SyntaxKind.ForStatement or SyntaxKind.ForEachStatement or SyntaxKind.ForEachVariableStatement or SyntaxKind.IfStatement
                or SyntaxKind.ElseClause or SyntaxKind.SwitchStatement or SyntaxKind.SwitchSection or SyntaxKind.CaseSwitchLabel
                or SyntaxKind.CasePatternSwitchLabel or SyntaxKind.DefaultSwitchLabel or SyntaxKind.TryStatement or SyntaxKind.CatchClause
                or SyntaxKind.FinallyClause or SyntaxKind.SwitchExpression => true,
            _ => false,
        };
    }

    /// <summary>Roslyn's split of punctuation: operators, except the angle brackets of type lists and a colon outside a conditional.</summary>
    private static string Punctuation(SyntaxToken token, SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken:
                return token.Parent?.Kind() is SyntaxKind.TypeParameterList or SyntaxKind.TypeArgumentList or SyntaxKind.FunctionPointerParameterList
                    ? TokenClasses.Punctuation
                    : TokenClasses.Operator;
            case SyntaxKind.ColonToken:
                return token.Parent is ConditionalExpressionSyntax ? TokenClasses.Operator : TokenClasses.Punctuation;
            case SyntaxKind.TildeToken or SyntaxKind.ExclamationToken or SyntaxKind.PercentToken or SyntaxKind.CaretToken
                or SyntaxKind.AmpersandToken or SyntaxKind.AsteriskToken or SyntaxKind.MinusToken or SyntaxKind.PlusToken or SyntaxKind.EqualsToken
                or SyntaxKind.BarToken or SyntaxKind.DotToken or SyntaxKind.QuestionToken or SyntaxKind.SlashToken or SyntaxKind.BarBarToken
                or SyntaxKind.AmpersandAmpersandToken or SyntaxKind.MinusMinusToken or SyntaxKind.PlusPlusToken or SyntaxKind.ColonColonToken
                or SyntaxKind.QuestionQuestionToken or SyntaxKind.MinusGreaterThanToken or SyntaxKind.ExclamationEqualsToken
                or SyntaxKind.EqualsEqualsToken or SyntaxKind.EqualsGreaterThanToken or SyntaxKind.LessThanEqualsToken
                or SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanLessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken
                or SyntaxKind.GreaterThanGreaterThanToken or SyntaxKind.GreaterThanGreaterThanEqualsToken
                or SyntaxKind.GreaterThanGreaterThanGreaterThanToken or SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken
                or SyntaxKind.SlashEqualsToken or SyntaxKind.AsteriskEqualsToken or SyntaxKind.BarEqualsToken or SyntaxKind.AmpersandEqualsToken
                or SyntaxKind.PlusEqualsToken or SyntaxKind.MinusEqualsToken or SyntaxKind.CaretEqualsToken or SyntaxKind.PercentEqualsToken
                or SyntaxKind.QuestionQuestionEqualsToken or SyntaxKind.DotDotToken:
                return TokenClasses.Operator;
            default:
                return TokenClasses.Punctuation;
        }
    }

    // ---- identifiers ----

    private static string Identifier(SyntaxToken token)
    {
        switch (token.Parent)
        {
            case BaseTypeDeclarationSyntax type when type.Identifier == token:
                return TypeClass(type);
            case DelegateDeclarationSyntax @delegate when @delegate.Identifier == token:
                return TokenClasses.DelegateName;
            case TypeParameterSyntax parameter when parameter.Identifier == token:
                return TokenClasses.TypeParameterName;
            case MethodDeclarationSyntax method when method.Identifier == token:
                return method.ParameterList.Parameters is [{ } first, ..] && first.Modifiers.Any(SyntaxKind.ThisKeyword)
                    ? CSharpTokenClasses.ExtensionMethodName
                    : TokenClasses.MethodName;
            case ConstructorDeclarationSyntax constructor when constructor.Identifier == token:
                return constructor.Parent is BaseTypeDeclarationSyntax owner ? TypeClass(owner) : TokenClasses.ClassName;
            case DestructorDeclarationSyntax destructor when destructor.Identifier == token:
                return destructor.Parent is BaseTypeDeclarationSyntax destructed ? TypeClass(destructed) : TokenClasses.ClassName;
            case LocalFunctionStatementSyntax local when local.Identifier == token:
                return TokenClasses.MethodName;
            case PropertyDeclarationSyntax property when property.Identifier == token:
                return TokenClasses.PropertyName;
            case EnumMemberDeclarationSyntax member when member.Identifier == token:
                return CSharpTokenClasses.EnumMemberName;
            case EventDeclarationSyntax @event when @event.Identifier == token:
                return CSharpTokenClasses.EventName;
            case VariableDeclaratorSyntax declarator when declarator.Identifier == token:
                return (declarator.Parent as VariableDeclarationSyntax)?.Parent switch
                {
                    FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ConstKeyword) ? CSharpTokenClasses.ConstantName : TokenClasses.FieldName,
                    LocalDeclarationStatementSyntax local => local.IsConst ? CSharpTokenClasses.ConstantName : TokenClasses.LocalName,
                    EventFieldDeclarationSyntax => CSharpTokenClasses.EventName,
                    _ => TokenClasses.LocalName,
                };
            case ParameterSyntax parameter when parameter.Identifier == token:
                return TokenClasses.ParameterName;
            case SingleVariableDesignationSyntax or CatchDeclarationSyntax or ForEachStatementSyntax or FromClauseSyntax or LetClauseSyntax
                or JoinClauseSyntax or JoinIntoClauseSyntax or QueryContinuationSyntax:
                return TokenClasses.LocalName;
            case LabeledStatementSyntax label when label.Identifier == token:
                return CSharpTokenClasses.LabelName;
            case ExternAliasDirectiveSyntax:
                return TokenClasses.NamespaceName;
            case SimpleNameSyntax name:
                return SimpleName(token, name);
            default:
                return TokenClasses.Identifier;
        }
    }

    private static string SimpleName(SyntaxToken token, SimpleNameSyntax name)
    {
        var parent = name.Parent;
        if (name is IdentifierNameSyntax)
        {
            var text = token.ValueText;
            switch (SyntaxFacts.GetContextualKeywordKind(text))
            {
                case SyntaxKind.NameOfKeyword when parent is InvocationExpressionSyntax nameOf && nameOf.Expression == name:
                    return TokenClasses.Keyword;
                case SyntaxKind.VarKeyword when IsTypeOfADeclaration(name):
                    return TokenClasses.Keyword;
            }

            if ((text == "notnull" || text == "unmanaged") && parent is TypeConstraintSyntax)
            {
                return TokenClasses.Keyword;
            }

            if (text == "value" && !(parent is MemberAccessExpressionSyntax member && member.Name == name) && InValueAccessor(name))
            {
                return TokenClasses.Keyword;
            }
        }

        switch (parent)
        {
            case InvocationExpressionSyntax invocation when invocation.Expression == name:
                return TokenClasses.MethodName;
            case MemberAccessExpressionSyntax member when member.Name == name && member.Parent is InvocationExpressionSyntax call && call.Expression == member:
                return TokenClasses.MethodName;
            case MemberBindingExpressionSyntax binding when binding.Name == name && binding.Parent is InvocationExpressionSyntax bound && bound.Expression == binding:
                return TokenClasses.MethodName;
        }

        // A namespace declaration's name, dotted or not.
        SyntaxNode node = name;
        while (node.Parent is QualifiedNameSyntax qualified)
        {
            node = qualified;
        }

        if (node.Parent is BaseNamespaceDeclarationSyntax @namespace && @namespace.Name == node)
        {
            return TokenClasses.NamespaceName;
        }

        return TokenClasses.Identifier;
    }

    private static bool IsTypeOfADeclaration(SyntaxNode type)
    {
        while (type.Parent is RefTypeSyntax or ScopedTypeSyntax)
        {
            type = type.Parent;
        }

        return type.Parent switch
        {
            VariableDeclarationSyntax declaration => declaration.Type == type,
            ForEachStatementSyntax loop => loop.Type == type,
            DeclarationExpressionSyntax expression => expression.Type == type,
            _ => false,
        };
    }

    private static bool InValueAccessor(SyntaxNode node)
    {
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            switch (at)
            {
                case AccessorDeclarationSyntax accessor:
                    return accessor.Kind() is SyntaxKind.SetAccessorDeclaration or SyntaxKind.InitAccessorDeclaration
                        or SyntaxKind.AddAccessorDeclaration or SyntaxKind.RemoveAccessorDeclaration;
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static string TypeClass(BaseTypeDeclarationSyntax type) => type.Kind() switch
    {
        SyntaxKind.ClassDeclaration => TokenClasses.ClassName,
        SyntaxKind.StructDeclaration => TokenClasses.StructName,
        SyntaxKind.InterfaceDeclaration => TokenClasses.InterfaceName,
        SyntaxKind.EnumDeclaration => TokenClasses.EnumName,
        SyntaxKind.RecordDeclaration => TokenClasses.RecordClassName,
        SyntaxKind.RecordStructDeclaration => TokenClasses.RecordStructName,
        _ => TokenClasses.ClassName,
    };

    private static void Add(TokenSpanBuffer into, TextSpan span, string tokenClass) => into.Add(span.Start, span.Length, tokenClass);
}
