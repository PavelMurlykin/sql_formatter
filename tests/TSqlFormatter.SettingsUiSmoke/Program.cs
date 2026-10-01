using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.IdeShared;

namespace TSqlFormatter.SettingsUiSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var editor = new FullSettingsControl(() => null);
        editor.LoadDraft(FormattingOptions.Default, new UserProfileStore(), true);
        using var window = new FormattingSettingsWindow(editor) { ShowInTaskbar = true };
        bool manual = !args.Contains("--verify");
        int exitCode = 0;
        if (!manual) window.Shown += async (_, _) =>
        {
            try { await Verify(editor, window); Console.WriteLine("Settings UI smoke passed."); }
            catch (Exception ex) { exitCode = 1; Console.Error.WriteLine(ex); }
            finally { window.Close(); }
        };
        Application.Run(window);
        return exitCode;
    }

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static async Task Verify(FullSettingsControl editor, Form window)
    {
        var search = Descendants(editor).OfType<TextBox>().Single(c => c.AccessibleName == "Поиск настроек");
        var tree = Descendants(editor).OfType<TreeView>().Single();
        var tabs = Descendants(editor).OfType<TabControl>().Single();
        var code = Descendants(editor).OfType<RichTextBox>().Single(c => !c.ReadOnly);
        var result = Descendants(editor).OfType<RichTextBox>().Single(c => c.ReadOnly);
        Button Button(string text) => Descendants(editor).OfType<Button>().Single(c => c.Text == text);
        await Task.Delay(700);
        Require(tree.Nodes.Count > 10 && result.Text.Contains("SELECT"), "Initial tree/preview unavailable");
        search.Text = "rules.execute.parameters.listIndent";
        Require(tree.SelectedNode?.Tag is SettingsField[] fields && fields.Length == 5, "Compound rule members must share a leaf");
        var offset = Descendants(editor).OfType<NumericUpDown>().Single();
        offset.Value = 2;
        Require((int)editor.Model.Get("rules.execute.parameters.listIndent.offset") == 2, "Immediate number edit not applied");
        code.Text = "select Id, Name from dbo.Items;";
        Require(result.Text == "", "Stale preview not cleared on typing");
        Button("Форматировать пример").PerformClick();
        await Task.Delay(700);
        Require(result.Text.Contains("SELECT"), "Custom SQL preview failed");
        search.Text = "rules.execute.parameters.stackList";
        Require(code.Text == "select Id, Name from dbo.Items;", "Selection replaced custom SQL");
        search.Text = "no-matching-setting";
        Require(tree.Nodes.Count == 0 && result.Text == "", "No-results state kept stale output");
        search.Text = "rules.execute.parameters.stackList";
        var choice = Descendants(editor).OfType<ComboBox>().Single(c => c.AccessibleName == "Размещать список вертикально");
        choice.SelectedIndex = 1;
        editor.ShowProfiles();
        var name = Descendants(editor).OfType<TextBox>().Single(c => c.AccessibleName == "Имя нового профиля");
        name.Text = "UI smoke";
        Button("Сохранить профиль").PerformClick();
        Require(editor.Profiles.Contains("UI smoke"), "Save profile failed");
        Button("Использовать по умолчанию").PerformClick();
        Require(editor.Profiles.DefaultProfileId == "user:UI smoke", "Default profile selection failed");
        Require(UserProfileStore.Deserialize(editor.Profiles.Serialize()).DefaultProfileId == "user:UI smoke", "Default profile persistence failed");
        tabs.SelectedIndex = 0;
        choice = Descendants(editor).OfType<ComboBox>().Single(c => c.AccessibleName == "Размещать список вертикально");
        choice.SelectedIndex = 2;
        Require(editor.Profiles.DefaultProfileId is null, "Editing must detach named default");
        code.Text = "SELECT FROM;";
        Button("Форматировать пример").PerformClick();
        await Task.Delay(700);
        Require(result.Text == "", "Invalid SQL produced formatted output");
        window.Size = window.MinimumSize;
        Require(search.Visible && code.Visible && Descendants(window).OfType<Button>().Single(b => b.Text == "Отмена").Visible,
            "Controls missing at minimum window size");
        window.DialogResult = DialogResult.Cancel;
    }
}
