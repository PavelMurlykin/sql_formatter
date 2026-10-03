using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Benchmarks;

/// <summary>Reproducible, explicitly approximate native working profiles from the two supplied XML exports.</summary>
internal static class SqlCompleteWorkingProfileExport
{
    public static void Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected: XML-directory output-directory coverage.tsv");
        var ledger = File.ReadAllLines(args[2]).Skip(1).Select(l => l.Split('\t')).ToDictionary(r => r[0]);
        var serializer = new SqlFormatterConfigurationSerializer();
        Directory.CreateDirectory(args[1]);
        var report = new List<string> { "profile\tpath\tsource_value\tnative_setting\tnative_value\tdecision" };
        foreach (var (file, name) in new[] { ("1-AV_Profile.xml", "AV_Profile"), ("2-Right-aligned-EPM-AWB2.xml", "Right-aligned-EPM-AWB2") })
        {
            var xml = XDocument.Load(Path.Combine(args[0], file));
            var values = xml.Descendants("PropertyValue").ToDictionary(p => p.Parent!.Name.LocalName == "SubOptions"
                ? (string)p.Parent.Attribute("Name")! + "." + (string)p.Attribute("Name")! : (string)p.Attribute("Name")!, p => p.Value);
            var groups = values.Where(v => ledger[v.Key][2] == "covered").GroupBy(v => Root(ledger[v.Key][3].Split(':')[0]));
            var rules = FormattingOptions.Default.Rules;
            var root = JObject.Parse(serializer.SerializeV2(FormattingOptions.Default));
            root["general"] = JObject.FromObject(new { maxLineLength = 120, lineEnding = "crlf", finalNewLine = true });
            root["indent"] = JObject.FromObject(new { style = "spaces", size = 4 });
            foreach (var group in groups)
            {
                if (group.Key is null) throw new InvalidOperationException("No native rule for " + group.First().Key);
                var d = RuleCatalog.Default.Definitions[group.Key];
                var entries = group.ToArray();
                string? Member(string suffix) => entries.FirstOrDefault(v => v.Key.EndsWith("." + suffix, StringComparison.Ordinal)).Value;
                var first = entries[0].Value;
                RuleValue value = d.DefaultValue.Kind switch
                {
                    RuleValueKind.Boolean => RuleValue.FromBoolean(bool.Parse(first)),
                    RuleValueKind.Integer => RuleValue.FromInteger(int.Parse(first)),
                    RuleValueKind.Threshold => RuleValue.FromThreshold(new ThresholdRule(bool.Parse(Member("Checked")!), int.Parse(Member("Value")!))),
                    RuleValueKind.Indent => RuleValue.FromIndent(new IndentRule(bool.Parse(Member("Indent")!), int.Parse(Member("Size")!),
                        bool.Parse(Member("OnNewLineOnly") ?? "true"), "relativeSpaces", bool.Parse(Member("Transparent") ?? "false"))),
                    RuleValueKind.Choice => RuleValue.FromChoice(Choice(d, entries[0].Key, first)),
                    _ => throw new InvalidOperationException(d.Key)
                };
                rules = rules.With(d.Key, value);
                var converted = JObject.Parse(serializer.SerializeV2(FormattingOptions.Default.With(rules: FormattingOptions.Default.Rules.With(d.Key, value))))["rules"]![d.Key]!;
                foreach (var entry in entries)
                {
                    var uncertain = d.DefaultValue.Kind == RuleValueKind.Choice && !bool.TryParse(entry.Value, out _)
                        || entry.Key.EndsWith(".Style", StringComparison.Ordinal);
                    report.Add(string.Join("\t", name, entry.Key, entry.Value, ledger[entry.Key][3].Split(':')[0], converted.ToString(Formatting.None),
                        uncertain ? "native_policy_numeric_mode_not_certified" : "typed_value_with_native_boundary_semantics"));
                }
            }
            foreach (var excluded in values.Where(v => ledger[v.Key][2] == "not_applicable"))
                report.Add(string.Join("\t", name, excluded.Key, excluded.Value, "-", "-", "previous_user_approved_exclusion"));
            var rulesJson = JObject.Parse(serializer.SerializeV2(FormattingOptions.Default.With(rules: rules)))["rules"]!;
            root["rules"] = rulesJson;
            var json = root.ToString(Formatting.Indented) + "\n";
            _ = serializer.Deserialize(json); // Validate every emitted native value before writing.
            File.WriteAllText(Path.Combine(args[1], name + ".json"), json, new UTF8Encoding(false));
            Console.WriteLine($"{name}: {values.Count} source fields, {rules.Overrides.Count} native rules");
        }
        File.WriteAllLines(Path.Combine(args[1], "sql-complete-working-mapping.tsv"), report, new UTF8Encoding(false));
    }

    private static string? Root(string setting) => RuleCatalog.Default.Definitions.Keys.OrderByDescending(k => k.Length)
        .FirstOrDefault(k => setting == k || setting.StartsWith(k + ".", StringComparison.Ordinal));

    private static string Choice(RuleDescriptor descriptor, string path, string source)
    {
        if (bool.TryParse(source, out var enabled))
        {
            if (descriptor.Key == "stackedList.commaPlacement") return enabled ? "leading" : "trailing";
            if (descriptor.Key == "stackedList.spaceAfterLeadingComma") return enabled ? "remove" : "insert";
            if (descriptor.Choices.Contains("always") && descriptor.Choices.Contains("never")) return enabled ? "always" : "never";
            if (descriptor.Choices.Contains("on") && descriptor.Choices.Contains("off")) return enabled ? "on" : "off";
            if (descriptor.Choices.Contains("insert") && descriptor.Choices.Contains("remove")) return enabled ? "insert" : "remove";
        }
        // Numeric options are explicit working policies, not a generic/certified XML enum decoder.
        if (descriptor.Key.StartsWith("textCase.", StringComparison.Ordinal)) return source switch { "0" => "upper", "3" => "preserve", _ => throw new ArgumentException(path) };
        if (descriptor.Key.Contains("stackMode", StringComparison.Ordinal) || descriptor.Key.Contains("stackRowsMode", StringComparison.Ordinal))
            return source is "2" or "8" ? "auto" : "onePerLine";
        if (descriptor.Key.EndsWith("wrapCondition", StringComparison.Ordinal)) return source == "0" ? "none" : "both";
        if (descriptor.Key.EndsWith("wrapBeforeOperator", StringComparison.Ordinal)) return source == "2" ? "always" : "inherit";
        if (descriptor.Key.EndsWith("wrapAfterOperator", StringComparison.Ordinal)) return "never";
        if (descriptor.Key == "spacing.arithmeticOperators") return "insert";
        if (descriptor.Key == "misc.packageDelimiterBlankLineMode") return "after";
        throw new InvalidOperationException($"No explicit native policy: {path} => {descriptor.Key} ({source})");
    }
}
