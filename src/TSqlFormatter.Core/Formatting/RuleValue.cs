namespace TSqlFormatter.Core.Formatting;

public enum RuleValueKind { Boolean, Integer, Choice, Threshold, Indent }

/// <summary>A switch with a numeric threshold; disabled values remain round-trippable.</summary>
public sealed class ThresholdRule : IEquatable<ThresholdRule>
{
    public ThresholdRule(bool enabled, int value)
    {
        Enabled = enabled;
        Value = value;
    }

    public bool Enabled { get; }
    public int Value { get; }
    public bool Equals(ThresholdRule? other) => other is not null && Enabled == other.Enabled && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as ThresholdRule);
    public override int GetHashCode() => (Enabled.GetHashCode() * 397) ^ Value;
}

/// <summary>Local indentation, including a signed offset relative to an anchor.</summary>
public sealed class IndentRule : IEquatable<IndentRule>
{
    public IndentRule(bool enabled, int offset, bool onNewLineOnly = true,
        string style = "relative", bool transparent = false)
    {
        if (style is not ("relative" or "absolute" or "anchor" or "relativeSpaces" or "absoluteSpaces"))
            throw new ArgumentOutOfRangeException(nameof(style));
        Enabled = enabled;
        Offset = offset;
        OnNewLineOnly = onNewLineOnly;
        Style = style;
        Transparent = transparent;
    }

    public bool Enabled { get; }
    public int Offset { get; }
    public bool OnNewLineOnly { get; }
    public string Style { get; }
    public bool Transparent { get; }

    /// <summary>Signed width in spaces; legacy styles measure the offset in indentation levels.</summary>
    public int Width(int indentSize) => Offset * (Style is "relativeSpaces" or "absoluteSpaces" ? 1 : indentSize);
    public int Column(int anchorColumn, int indentSize) => Transparent ? 0
        : Math.Max(0, (Style is "absolute" or "absoluteSpaces" ? 0 : anchorColumn) + Width(indentSize));

    public bool Equals(IndentRule? other) => other is not null && Enabled == other.Enabled
        && Offset == other.Offset && OnNewLineOnly == other.OnNewLineOnly
        && Style == other.Style && Transparent == other.Transparent;
    public override bool Equals(object? obj) => Equals(obj as IndentRule);
    public override int GetHashCode() => (((Enabled.GetHashCode() * 397) ^ Offset) * 397)
        ^ StringComparer.Ordinal.GetHashCode(Style) ^ OnNewLineOnly.GetHashCode() ^ Transparent.GetHashCode();
}

/// <summary>Closed, typed value for a future formatting rule.</summary>
public sealed class RuleValue : IEquatable<RuleValue>
{
    private readonly object value;

    private RuleValue(RuleValueKind kind, object value)
    {
        Kind = kind;
        this.value = value;
    }

    public RuleValueKind Kind { get; }
    public bool Boolean => Kind == RuleValueKind.Boolean ? (bool)value : throw WrongKind();
    public int Integer => Kind == RuleValueKind.Integer ? (int)value : throw WrongKind();
    public string Choice => Kind == RuleValueKind.Choice ? (string)value : throw WrongKind();
    public ThresholdRule Threshold => Kind == RuleValueKind.Threshold ? (ThresholdRule)value : throw WrongKind();
    public IndentRule Indent => Kind == RuleValueKind.Indent ? (IndentRule)value : throw WrongKind();

    public static RuleValue FromBoolean(bool value) => new(RuleValueKind.Boolean, value);
    public static RuleValue FromInteger(int value) => new(RuleValueKind.Integer, value);
    public static RuleValue FromChoice(string value) => new(RuleValueKind.Choice,
        !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Choice is required.", nameof(value)));
    public static RuleValue FromThreshold(ThresholdRule value) => new(RuleValueKind.Threshold,
        value ?? throw new ArgumentNullException(nameof(value)));
    public static RuleValue FromIndent(IndentRule value) => new(RuleValueKind.Indent,
        value ?? throw new ArgumentNullException(nameof(value)));

    public bool Equals(RuleValue? other) => other is not null && Kind == other.Kind && value.Equals(other.value);
    public override bool Equals(object? obj) => Equals(obj as RuleValue);
    public override int GetHashCode() => ((int)Kind * 397) ^ value.GetHashCode();
    private static InvalidOperationException WrongKind() => new("Rule value has a different kind.");
}
