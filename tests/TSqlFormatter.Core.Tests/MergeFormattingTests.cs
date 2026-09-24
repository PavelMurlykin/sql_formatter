using TSqlFormatter.Core.Formatting;
using Xunit;

namespace TSqlFormatter.Core.Tests;

public sealed class MergeFormattingTests
{
    private readonly ScriptDomSqlFormatter _formatter = new();

    [Fact]
    public void Formats_merge_update_insert_and_output()
    {
        const string source = "merge into dbo.Target as t using dbo.Source as s on t.Id=s.Id when matched then update set t.Name=s.Name when not matched then insert (Id,Name) values (s.Id,s.Name) output $action,inserted.Id;";
        var first = _formatter.Format(source, new FormattingOptions(), new FormatRequest());
        var second = _formatter.Format(first.Text, new FormattingOptions(), new FormatRequest());

        Assert.Equal("MERGE INTO dbo.Target AS t\nUSING dbo.Source AS s\nON t.Id = s.Id\nWHEN MATCHED THEN\n    UPDATE SET\n        t.Name = s.Name\nWHEN NOT MATCHED THEN\n    INSERT (Id, Name)\n    VALUES\n        (s.Id, s.Name)\nOUTPUT $action, inserted.Id;", first.Text);
        Assert.True(first.ParseSucceeded);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Formats_merge_delete_action()
    {
        var result = _formatter.Format(
            "merge into T as t using S as s on t.Id=s.Id when matched then delete;",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("MERGE INTO T AS t\nUSING S AS s\nON t.Id = s.Id\nWHEN MATCHED THEN\n    DELETE;", result.Text);
    }

    [Fact]
    public void Formats_not_matched_by_source_delete()
    {
        var result = _formatter.Format(
            "merge into T as t using S as s on t.Id=s.Id when not matched by source then delete;",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("MERGE INTO T AS t\nUSING S AS s\nON t.Id = s.Id\nWHEN NOT MATCHED BY SOURCE THEN\n    DELETE;", result.Text);
    }

    [Fact]
    public void Formats_merge_output_into()
    {
        var result = _formatter.Format(
            "merge into T as t using S as s on t.Id=s.Id when matched then delete output $action into dbo.Audit (Action);",
            new FormattingOptions(), new FormatRequest());

        Assert.Equal("MERGE INTO T AS t\nUSING S AS s\nON t.Id = s.Id\nWHEN MATCHED THEN\n    DELETE\nOUTPUT $action INTO dbo.Audit (Action);", result.Text);
    }

    [Fact]
    public void Preserves_keyword_spelling_when_requested()
    {
        var result = _formatter.Format(
            "merge into T as t using S as s on t.Id=s.Id when matched then delete;",
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal("merge into T as t\nusing S as s\non t.Id = s.Id\nwhen matched then\n    delete;", result.Text);
    }

    [Fact]
    public void Leaves_conditional_action_in_original_layout()
    {
        const string source = "merge into T as t using S as s on t.Id=s.Id when matched and s.Flag=1 then delete;";
        var result = _formatter.Format(source,
            new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Preserve)),
            new FormatRequest());

        Assert.Equal(source, result.Text);
    }
}
