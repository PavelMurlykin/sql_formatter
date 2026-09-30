using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Formatting;

internal static class CreateTableLayout
{
    public static string ApplySafe(string source, FormattingOptions options, ISqlParser parser,
        SqlDialectVersion dialect, CancellationToken cancellationToken)
    {
        if (!options.Rules.Overrides.Keys.Any(key => key.StartsWith("createTable.", StringComparison.Ordinal))) return source;
        var parsed = parser.Parse(source, dialect, cancellationToken);
        if (!parsed.ParseSucceeded || parsed.Root is null) return source;
        var editor = new SqlTokenGapEditor(parsed, options);
        var lists = new SqlListLayout(parsed, options, editor);
        foreach (var table in new SqlFragmentWalker().Walk(parsed.Root, cancellationToken).OfType<CreateTableStatement>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (table.Definition is not { } definition || table.SelectStatement is not null || table.CloneSource is not null) continue;
            var items = definition.ColumnDefinitions.Cast<TSqlFragment>().Concat(definition.TableConstraints)
                .Concat(definition.Indexes).Concat(definition.SystemTimePeriod is { } period ? new[] { period } : Array.Empty<TSqlFragment>())
                .OrderBy(f => f.StartOffset).ToArray();
            if (items.Length == 0) continue;
            var open = editor.Find(End(table.SchemaObjectName), items[0].StartOffset, t => t.Text == "(");
            var close = editor.Find(End(items[items.Length - 1]), End(table), t => t.Text == ")");
            if (open is null || close is null) continue;
            editor.Before(open.Offset, Choice("columns.breakBeforeOpen"), Indent("columns.braceIndent"), Choice("columns.spaceBeforeOpen"), table.StartOffset);
            editor.Before(items[0].StartOffset, Choice("columns.breakAfterOpen"), Indent("columns.listIndent"), Choice("columns.spaceWithin"), open.Offset);
            lists.Apply(items, "createTable.columns", open.Offset, open.Offset, close.Offset + close.Text.Length);
            editor.Before(close.Offset, Choice("columns.breakBeforeClose"), Indent("columns.braceIndent"), Choice("columns.spaceWithin"), table.StartOffset);
            Storage(table.OnFileGroupOrPartitionScheme, "ON");
            Storage(table.TextImageOn, "TEXTIMAGE_ON");
            Storage(table.FileStreamOn, "FILESTREAM_ON");
            if (table.Options.Count > 0)
            {
                var with = editor.FindLast(close.Offset + close.Text.Length, table.Options[0].StartOffset, Is("WITH"));
                if (with is not null)
                {
                    editor.Before(with.Offset, Choice("storage.breakBefore"), Indent("storage.listIndent"), anchorOffset: table.StartOffset);
                    editor.Before(table.Options[0].StartOffset, "inherit", Indent("storage.listIndent"), anchorOffset: with.Offset);
                    lists.Apply(table.Options.Cast<TSqlFragment>().ToArray(), "createTable.storage", with.Offset, with.Offset);
                }
            }
            if (NativeRules.Get(options, "createTable.blankLinesAround").Boolean)
            {
                editor.BlankBefore(table.StartOffset);
                var next = editor.Find(End(table), source.Length, _ => true);
                if (next is not null && !Is("END")(next) && !Is("GO")(next)) editor.BlankBefore(next.Offset);
            }
            void Storage(TSqlFragment? fragment, string word)
            {
                if (fragment is null) return;
                var keyword = editor.FindLast(close.Offset + close.Text.Length, fragment.StartOffset, Is(word));
                if (keyword is not null)
                    editor.Before(keyword.Offset, Choice("storage.breakBefore"), Indent("storage.listIndent"), anchorOffset: table.StartOffset);
            }
        }
        var changed = editor.Apply(cancellationToken);
        if (changed == source) return source;
        var after = parser.Parse(changed, dialect, cancellationToken);
        return after.ParseSucceeded && SqlSpacing.SameTokens(parsed, after) ? changed : source;
        string Choice(string key) => NativeRules.Get(options, "createTable." + key).Choice;
        IndentRule Indent(string key) => NativeRules.Get(options, "createTable." + key).Indent;
    }
    private static int End(TSqlFragment fragment) => fragment.StartOffset + fragment.FragmentLength;
    private static Func<TSqlParserToken, bool> Is(string word) => token => token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}
