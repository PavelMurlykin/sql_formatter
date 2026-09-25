using System.Text;

namespace TSqlFormatter.Core.Layout;

/// <summary>Renders layout documents without any knowledge of SQL syntax trees.</summary>
public sealed class DocRenderer
{
    public string Render(Doc document, DocRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        options ??= new DocRenderOptions();
        cancellationToken.ThrowIfCancellationRequested();
        var state = new RenderState(options);
        var pending = new Stack<Frame>();
        pending.Push(new Frame(document, 0, false, false));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = pending.Pop();
            switch (frame.Node)
            {
                case TextDoc text:
                    state.AppendLiteral(text.Text, frame.IndentLevel, cancellationToken);
                    break;
                case ConcatDoc concat:
                    PushChildren(pending, concat, frame);
                    break;
                case SoftLineDoc:
                    if (frame.Flat)
                    {
                        state.AppendGeneratedSpace(frame.IndentLevel);
                    }
                    else
                    {
                        state.AppendNewline();
                    }
                    break;
                case HardLineDoc:
                    state.AppendNewline();
                    break;
                case IndentDoc indent:
                    pending.Push(new Frame(indent.Content, checked(frame.IndentLevel + indent.Levels), frame.Flat, false));
                    break;
                case GroupDoc group:
                    var flat = frame.Flat || Fits(group.Content, frame.IndentLevel, state,
                        pending, options, cancellationToken);
                    pending.Push(new Frame(group.Content, frame.IndentLevel, flat, false));
                    break;
                case IfBreakDoc conditional:
                    pending.Push(new Frame(frame.Flat ? conditional.Flat : conditional.Broken, frame.IndentLevel, frame.Flat, false));
                    break;
                default:
                    throw new InvalidOperationException("Unknown document node.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return state.Finish();
    }

    private static bool Fits(
        Doc candidate,
        int indentLevel,
        RenderState state,
        Stack<Frame> pending,
        DocRenderOptions options,
        CancellationToken cancellationToken)
    {
        var probe = new Stack<Frame>();
        var remaining = pending.ToArray();
        for (var index = remaining.Length - 1; index >= 0; index--)
        {
            probe.Push(remaining[index]);
        }

        probe.Push(new Frame(candidate, indentLevel, true, true));
        var column = (long)state.Column;
        var atLineStart = state.AtLineStart;

        while (probe.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = probe.Pop();
            switch (frame.Node)
            {
                case TextDoc text:
                    if (text.Text.Length == 0)
                    {
                        break;
                    }

                    var newlineIndex = text.Text.IndexOfAny(new[] { '\r', '\n' });
                    var length = newlineIndex >= 0 ? newlineIndex : text.Text.Length;
                    if (length > 0 && atLineStart)
                    {
                        column += (long)frame.IndentLevel * options.IndentWidth;
                        atLineStart = false;
                    }

                    column += length;
                    if (column > options.MaxLineWidth)
                    {
                        return false;
                    }

                    if (newlineIndex >= 0)
                    {
                        return !frame.Candidate;
                    }

                    break;
                case ConcatDoc concat:
                    PushChildren(probe, concat, frame);
                    break;
                case SoftLineDoc:
                    if (!frame.Flat)
                    {
                        return true;
                    }

                    if (atLineStart)
                    {
                        column += (long)frame.IndentLevel * options.IndentWidth;
                        atLineStart = false;
                    }

                    if (++column > options.MaxLineWidth)
                    {
                        return false;
                    }

                    break;
                case HardLineDoc:
                    return !frame.Candidate;
                case IndentDoc indent:
                    probe.Push(new Frame(indent.Content, checked(frame.IndentLevel + indent.Levels), frame.Flat, frame.Candidate));
                    break;
                case GroupDoc group:
                    probe.Push(new Frame(group.Content, frame.IndentLevel, true, frame.Candidate));
                    break;
                case IfBreakDoc conditional:
                    probe.Push(new Frame(frame.Flat ? conditional.Flat : conditional.Broken, frame.IndentLevel, frame.Flat, frame.Candidate));
                    break;
                default:
                    throw new InvalidOperationException("Unknown document node.");
            }
        }

        return true;
    }

    private static void PushChildren(Stack<Frame> pending, ConcatDoc concat, Frame parent)
    {
        for (var index = concat.Children.Count - 1; index >= 0; index--)
        {
            pending.Push(new Frame(concat.Children[index], parent.IndentLevel, parent.Flat, parent.Candidate));
        }
    }

    private readonly struct Frame
    {
        public Frame(Doc node, int indentLevel, bool flat, bool candidate)
        {
            Node = node;
            IndentLevel = indentLevel;
            Flat = flat;
            Candidate = candidate;
        }

        public Doc Node { get; }

        public int IndentLevel { get; }

        public bool Flat { get; }

        public bool Candidate { get; }
    }

    private sealed class RenderState
    {
        private readonly DocRenderOptions _options;
        private readonly StringBuilder _output = new();
        private int _lineStartIndex;
        private int _generatedTrailingSpaces;
        private bool _lineHasLiteral;

        public RenderState(DocRenderOptions options)
        {
            _options = options;
        }

        public int Column { get; private set; }

        public bool AtLineStart { get; private set; } = true;

        public void AppendLiteral(string value, int indentLevel, CancellationToken cancellationToken)
        {
            var allowIndent = true;
            for (var index = 0; index < value.Length; index++)
            {
                if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                var character = value[index];
                if (character == '\r' || character == '\n')
                {
                    _output.Append(character);
                    if (character == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
                    {
                        _output.Append(value[++index]);
                    }

                    Column = 0;
                    AtLineStart = true;
                    _lineStartIndex = _output.Length;
                    _generatedTrailingSpaces = 0;
                    _lineHasLiteral = false;
                    allowIndent = false;
                    continue;
                }

                if (AtLineStart && allowIndent)
                {
                    AppendIndent(indentLevel);
                }

                _output.Append(character);
                Column++;
                AtLineStart = false;
                _lineHasLiteral = true;
                _generatedTrailingSpaces = 0;
            }
        }

        public void AppendGeneratedSpace(int indentLevel)
        {
            if (AtLineStart)
            {
                AppendIndent(indentLevel);
            }

            _output.Append(' ');
            Column++;
            AtLineStart = false;
            _generatedTrailingSpaces++;
        }

        public void AppendNewline()
        {
            TrimGeneratedWhitespace();

            _output.Append(_options.Newline);
            _lineStartIndex = _output.Length;
            _generatedTrailingSpaces = 0;
            _lineHasLiteral = false;
            Column = 0;
            AtLineStart = true;
        }

        public string Finish()
        {
            TrimGeneratedWhitespace();
            if (_options.FinalNewline && _output.Length > 0)
            {
                var last = _output[_output.Length - 1];
                if (last != '\r' && last != '\n')
                {
                    AppendNewline();
                }
            }

            return _output.ToString();
        }

        private void TrimGeneratedWhitespace()
        {
            if (!_lineHasLiteral)
            {
                _output.Length = _lineStartIndex;
            }
            else if (_generatedTrailingSpaces > 0)
            {
                _output.Length -= _generatedTrailingSpaces;
            }

            _generatedTrailingSpaces = 0;
        }

        private void AppendIndent(int levels)
        {
            if (levels == 0)
            {
                return;
            }

            if (_options.UseTabs)
            {
                _output.Append('\t', levels);
            }
            else
            {
                _output.Append(' ', checked(levels * _options.IndentWidth));
            }

            Column = checked(Column + levels * _options.IndentWidth);
        }
    }
}
