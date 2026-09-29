using System.Globalization;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Core.Tests;

public sealed class SqlCompleteParityInventoryTests
{
    private static readonly IReadOnlyDictionary<string, int> CategoryCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Select"] = 81, ["Subquery"] = 96, ["UnionExceptIntersect"] = 4,
            ["Case"] = 16, ["Insert"] = 38, ["Update"] = 45, ["Delete"] = 41,
            ["Merge"] = 109, ["Declare"] = 16, ["Code"] = 27,
            ["ProcedureFunction"] = 24, ["View"] = 17, ["CreateTable"] = 14,
            ["Trigger"] = 21, ["Execute"] = 4, ["Labels"] = 3,
            ["Spacing"] = 10, ["TextCase"] = 7, ["StackedList"] = 2, ["Misc"] = 2
        };

    [Fact]
    public void Profile_snapshot_retains_all_names_types_nesting_and_both_sets_of_values()
    {
        var snapshotHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(FilePath("profile-values.tsv"))));
        Assert.Equal("51FC02E8E1AD6CB0487AA4B419BC543560DD2A4D93A7F73B03ED904581B66829",
            snapshotHash);
        var lines = ReadLines("profile-values.tsv");
        Assert.Equal("Path\tKind\tType\tAV\tEPM", lines[0]);
        var rows = lines.Skip(1).Select(line => line.Split('\t')).ToArray();
        Assert.Equal(977, rows.Length);
        Assert.All(rows, row => Assert.Equal(5, row.Length));

        var paths = rows.Select(row => row[0]).ToArray();
        Assert.Equal(977, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(paths.OrderBy(path => path, StringComparer.Ordinal), paths);

        var options = rows.Select(row => Parent(row[0])).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(577, options.Length);
        var bundles = rows.Where(row => row[1] == "SubOptions")
            .Select(row => Parent(row[0])).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(203, bundles.Length);
        Assert.All(rows, row =>
        {
            Assert.Contains(row[1], new[] { "PropertyValue", "SubOptions" });
            Assert.Equal(row[1] == "SubOptions", row[0].Contains('.'));
            Assert.Contains(row[2], new[] { "Boolean", "Integer" });
            Assert.NotEqual("-", row[4]);
            Assert.True(row[3] != "-" || row[4] != "-");
            foreach (var value in row.Skip(3).Where(value => value != "-"))
            {
                if (row[2] == "Boolean") Assert.Contains(value, new[] { "true", "false" });
                else Assert.True(int.TryParse(value, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out _), $"{row[0]} has invalid integer '{value}'.");
            }
        });

        var actualCategories = options.GroupBy(option => option.Split('_')[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(CategoryCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualCategories.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(190, rows.Count(row => row[3] != "-" && row[4] != "-" && row[3] != row[4]));
        var extra = rows.Where(row => row[3] == "-").Select(row => row[0]).ToArray();
        Assert.Equal(5, extra.Length);
        Assert.All(extra, path => Assert.EndsWith("_IndentJoinKeyword.Style", path));
    }

    [Fact]
    public void Coverage_ledger_has_one_explicit_assignment_per_profile_value()
    {
        var paths = ReadLines("profile-values.tsv").Skip(1)
            .Select(line => line.Split('\t')[0]).ToArray();
        var lines = ReadLines("coverage.tsv");
        Assert.Equal("Path\tStage\tStatus\tNativeSetting\tEvidence\tSemantics", lines[0]);
        var rows = lines.Skip(1).Select(line => line.Split('\t')).ToArray();
        Assert.Equal(paths.Length, rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            Assert.Equal(6, row.Length);
            Assert.Equal(paths[index], row[0]);
            Assert.Matches(@"^SC-(0[4-9]|1[0-9]|2[0-4])$", row[1]);
            Assert.Contains(row[2], new[] { "pending_semantics", "in_progress", "covered", "blocked" });
            Assert.All(row.Skip(3), value => Assert.NotEmpty(value));
            if (row[2] == "covered")
            {
                Assert.All(row.Skip(3), value => Assert.NotEqual("-", value));
            }
        }
    }

    [Fact]
    public void Control_corpus_has_parseable_sql_for_every_profile_category()
    {
        var lines = ReadLines("corpus.tsv");
        Assert.Equal("Category\tSql", lines[0]);
        var rows = lines.Skip(1).Select(line => line.Split('\t')).ToArray();
        Assert.Equal(20, rows.Length);
        Assert.All(rows, row => Assert.Equal(2, row.Length));
        Assert.Equal(CategoryCounts.Keys.OrderBy(value => value, StringComparer.Ordinal),
            rows.Select(row => row[0]).OrderBy(value => value, StringComparer.Ordinal));

        var parser = new ScriptDomSqlParser();
        foreach (var row in rows)
        {
            var sql = row[1].Replace("\\n", "\n", StringComparison.Ordinal);
            var result = parser.Parse(sql, SqlDialectVersion.Auto);
            Assert.True(result.ParseSucceeded, $"{row[0]}: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
        }
    }

    private static string Parent(string path) => path.Split('.')[0];

    private static string FilePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "SqlCompleteParity", name);

    private static string[] ReadLines(string name) => File.ReadAllLines(FilePath(name));
}
