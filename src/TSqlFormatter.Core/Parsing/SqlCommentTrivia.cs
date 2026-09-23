namespace TSqlFormatter.Core.Parsing;

/// <summary>A source-preserving comment and its relation to a meaningful token.</summary>
public sealed class SqlCommentTrivia
{
    public SqlCommentTrivia(
        int tokenIndex,
        SqlTextSpan span,
        string text,
        SqlCommentKind kind,
        SqlTriviaPlacement placement,
        int? anchorTokenIndex)
    {
        if (tokenIndex < 0) throw new ArgumentOutOfRangeException(nameof(tokenIndex));
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (!Enum.IsDefined(typeof(SqlCommentKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(SqlTriviaPlacement), placement))
            throw new ArgumentOutOfRangeException(nameof(placement));
        if (anchorTokenIndex < 0) throw new ArgumentOutOfRangeException(nameof(anchorTokenIndex));
        if (placement == SqlTriviaPlacement.Standalone && anchorTokenIndex is not null)
            throw new ArgumentException("Standalone comments cannot have an anchor.", nameof(anchorTokenIndex));
        if (placement != SqlTriviaPlacement.Standalone && anchorTokenIndex is null)
            throw new ArgumentException("Attached comments require an anchor.", nameof(anchorTokenIndex));

        TokenIndex = tokenIndex;
        Span = span;
        Text = text;
        Kind = kind;
        Placement = placement;
        AnchorTokenIndex = anchorTokenIndex;
    }

    public int TokenIndex { get; }

    public SqlTextSpan Span { get; }

    public string Text { get; }

    public SqlCommentKind Kind { get; }

    public SqlTriviaPlacement Placement { get; }

    /// <summary>Index in SqlParseResult.Tokens; null for standalone comments.</summary>
    public int? AnchorTokenIndex { get; }
}
