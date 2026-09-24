using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class FormatterApiTests
{
    [Fact]
    public void FormatRequest_DefaultsToDocumentAutoAndStrict()
    {
        var request = new FormatRequest();

        Assert.Equal(FormatScope.Document, request.Scope);
        Assert.Null(request.Selection);
        Assert.Equal(SqlDialectVersion.Auto, request.Dialect);
        Assert.Equal(ParseFailureBehavior.Strict, request.ParseFailureBehavior);
    }

    [Fact]
    public void FormatRequest_RequiresSpanForSelectionScope()
    {
        Assert.Throws<ArgumentException>(() => new FormatRequest(scope: FormatScope.Selection));

        var span = new SqlTextSpan(5, 3);
        var request = new FormatRequest(FormatScope.Selection, span, SqlDialectVersion.Sql2022);

        Assert.Equal(span.StartOffset, request.Selection?.StartOffset);
        Assert.Equal(span.Length, request.Selection?.Length);
        Assert.Equal(SqlDialectVersion.Sql2022, request.Dialect);
    }

    [Fact]
    public void FormatRequest_RejectsUnknownEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FormatRequest(scope: (FormatScope)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FormatRequest(dialect: (SqlDialectVersion)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FormatRequest(parseFailureBehavior: (ParseFailureBehavior)99));
    }

    [Fact]
    public void FormattingOptions_HaveConservativeDefaultsAndSections()
    {
        var options = new FormattingOptions();

        Assert.Equal(100, options.General.MaxLineWidth);
        Assert.Equal(DocLineEnding.Lf, options.General.LineEnding);
        Assert.False(options.General.FinalNewline);
        Assert.Equal(4, options.Indent.Size);
        Assert.False(options.Indent.UseTabs);
        Assert.Equal(KeywordCase.Upper, options.Keywords.Case);
    }

    [Fact]
    public void FormattingOptions_UseSuppliedSections()
    {
        var general = new GeneralOptions(80, DocLineEnding.CrLf, true);
        var indent = new IndentOptions(2, true);
        var keywords = new KeywordOptions(KeywordCase.Upper);
        var options = new FormattingOptions(general, indent, keywords);

        Assert.Same(general, options.General);
        Assert.Same(indent, options.Indent);
        Assert.Same(keywords, options.Keywords);
    }

    [Fact]
    public void FormattingOptions_DefaultMatchesConstructorAndWithKeepsOtherSections()
    {
        var defaults = FormattingOptions.Default;
        var constructed = new FormattingOptions();
        Assert.Equal(constructed.General.MaxLineWidth, defaults.General.MaxLineWidth);
        Assert.Equal(constructed.Indent.Size, defaults.Indent.Size);
        Assert.Equal(constructed.Keywords.Case, defaults.Keywords.Case);
        Assert.Equal(constructed.Select.ColumnLayout, defaults.Select.ColumnLayout);
        Assert.Equal(constructed.Clauses.GroupByLayout, defaults.Clauses.GroupByLayout);

        var newGeneral = new GeneralOptions(maxLineWidth: 120);
        var modified = defaults.With(general: newGeneral);
        Assert.NotSame(defaults, modified);
        Assert.Same(newGeneral, modified.General);
        Assert.Same(defaults.Indent, modified.Indent);
        Assert.Same(defaults.Keywords, modified.Keywords);
        Assert.Same(defaults.Select, modified.Select);
        Assert.Same(defaults.Clauses, modified.Clauses);
        Assert.Equal(100, defaults.General.MaxLineWidth);
    }

    [Fact]
    public void FormattingOptions_RejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeneralOptions(maxLineWidth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeneralOptions(lineEnding: (DocLineEnding)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndentOptions(size: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeywordOptions((KeywordCase)99));
    }

    [Fact]
    public void FormatResult_StoresTextFlagsAndDefaultEmptyCollections()
    {
        var result = new FormatResult("SELECT 1;", changed: false, parseSucceeded: true);

        Assert.Equal("SELECT 1;", result.Text);
        Assert.False(result.Changed);
        Assert.True(result.ParseSucceeded);
        Assert.Empty(result.Edits);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FormatResult_CopiesEditAndDiagnosticCollections()
    {
        var edit = new TextEdit(new SqlTextSpan(0, 6), "SELECT");
        var diagnostic = new FormatterDiagnostic("SQL001", "Parse error", FormatterDiagnosticSeverity.Error,
            new SqlTextSpan(7, 1));
        var edits = new List<TextEdit> { edit };
        var diagnostics = new List<FormatterDiagnostic> { diagnostic };

        var result = new FormatResult("SELECT 1;", true, false, edits, diagnostics);
        edits.Clear();
        diagnostics.Clear();

        Assert.Same(edit, Assert.Single(result.Edits));
        Assert.Same(diagnostic, Assert.Single(result.Diagnostics));
        Assert.Equal(0, edit.Span.StartOffset);
        Assert.Equal("SELECT", edit.NewText);
        Assert.Equal("SQL001", diagnostic.Code);
        Assert.Equal(FormatterDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(7, diagnostic.Span?.StartOffset);
        Assert.Throws<NotSupportedException>(() => ((IList<TextEdit>)result.Edits)[0] = edit);
    }

    [Fact]
    public void FormatResult_RejectsInvalidEntries()
    {
        Assert.Throws<ArgumentNullException>(() => new FormatResult(null!, false, false));
        Assert.Throws<ArgumentException>(() => new FormatResult("", false, false, new TextEdit[] { null! }));
        Assert.Throws<ArgumentException>(() => new FormatResult("", false, false,
            diagnostics: new FormatterDiagnostic[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new TextEdit(default, null!));
        Assert.Throws<ArgumentException>(() => new FormatterDiagnostic("", "message", FormatterDiagnosticSeverity.Error));
        Assert.Throws<ArgumentException>(() => new FormatterDiagnostic("SQL001", "", FormatterDiagnosticSeverity.Error));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FormatterDiagnostic("SQL001", "message",
            (FormatterDiagnosticSeverity)99));
    }

    [Fact]
    public void ISqlFormatter_ContractCanBeImplementedWithoutHostDependencies()
    {
        ISqlFormatter formatter = new StubFormatter();

        var result = formatter.Format("SELECT 1;", new FormattingOptions(), new FormatRequest());

        Assert.Equal("SELECT 1;", result.Text);
    }

    private sealed class StubFormatter : ISqlFormatter
    {
        public FormatResult Format(string source, FormattingOptions options, FormatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new FormatResult(source, false, true);
        }
    }
}
