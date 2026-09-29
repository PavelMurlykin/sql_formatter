namespace TSqlFormatter.Core.Layout;

/// <summary>Reusable document composition for SQL-specific lists and boundaries.</summary>
public static class SqlLayoutPrimitives
{
    public static Doc Break(LayoutBreak preference) => preference switch
    {
        LayoutBreak.Never => new TextDoc(" "),
        LayoutBreak.Auto => SoftLineDoc.Instance,
        LayoutBreak.Always => HardLineDoc.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(preference))
    };

    public static Doc CommaSeparated(IReadOnlyList<Doc> items, LayoutList layout,
        LayoutComma placement = LayoutComma.Trailing)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (!Enum.IsDefined(typeof(LayoutList), layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        if (!Enum.IsDefined(typeof(LayoutComma), placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        if (items.Any(item => item is null)) throw new ArgumentException("Null list item.", nameof(items));
        if (items.Count == 0) return new ConcatDoc(Array.Empty<Doc>());
        var pieces = new List<Doc> { items[0] };
        for (var index = 1; index < items.Count; index++)
        {
            Doc boundary = layout == LayoutList.OnePerLine ? HardLineDoc.Instance : SoftLineDoc.Instance;
            if (placement == LayoutComma.Trailing)
            {
                pieces.Add(new TextDoc(","));
                pieces.Add(boundary);
            }
            else
            {
                pieces.Add(boundary);
                pieces.Add(new TextDoc(", "));
            }
            pieces.Add(items[index]);
        }
        return new ConcatDoc(pieces);
    }

    public static Doc Parenthesize(Doc content, LayoutBreak afterOpen,
        LayoutBreak beforeClose, int indentLevels = 1)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (indentLevels < 0) throw new ArgumentOutOfRangeException(nameof(indentLevels));
        return new ConcatDoc(new Doc[]
        {
            new TextDoc("("),
            new IndentDoc(indentLevels, new ConcatDoc(new[] {
                afterOpen == LayoutBreak.Never ? new TextDoc(string.Empty) : Break(afterOpen), content })),
            beforeClose == LayoutBreak.Never ? new TextDoc(string.Empty) : Break(beforeClose),
            new TextDoc(")")
        });
    }

    /// <summary>Aligns validated one-line pairs, or returns null for unsafe input/width.</summary>
    public static IReadOnlyList<string>? AlignPairs(IReadOnlyList<(string Left, string Right)> pairs,
        int maxLineWidth, int leadingWidth = 0)
    {
        if (pairs is null) throw new ArgumentNullException(nameof(pairs));
        if (maxLineWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxLineWidth));
        if (leadingWidth < 0) throw new ArgumentOutOfRangeException(nameof(leadingWidth));
        if (pairs.Count < 2 || pairs.Any(pair => !Safe(pair.Left) || !Safe(pair.Right))) return null;
        var width = pairs.Max(pair => pair.Left.Length);
        if (pairs.Any(pair => (long)leadingWidth + width + 1 + pair.Right.Length > maxLineWidth))
            return null;
        return pairs.Select(pair => pair.Left.PadRight(width) + " " + pair.Right).ToArray();

        static bool Safe(string text) => !string.IsNullOrWhiteSpace(text)
            && text.IndexOfAny(new[] { '\r', '\n', '\t' }) < 0
            && !text.Contains("--") && !text.Contains("/*") && !text.Contains("*/");
    }
}
