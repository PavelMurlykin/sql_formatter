using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
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
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool english = args.Contains("--english"), dark = args.Contains("--dark");
        SettingsAppearance.Culture = CultureInfo.GetCultureInfo(english ? "en-US" : "ru-RU");
        using var editor = new FullSettingsControl(() => null);
        editor.LoadDraft(FormattingOptions.Default, new UserProfileStore(), true);
        var theme = dark ? new SettingsAppearance(Color.FromArgb(30, 30, 30), Color.FromArgb(220, 220, 220), Color.FromArgb(45, 45, 48), Color.FromArgb(0, 122, 204)) : SettingsAppearance.Light;
        using var window = new FormattingSettingsWindow(editor);
        theme.Apply(window); editor.ApplyAppearance(theme);
        bool manual = !args.Contains("--verify"); int exitCode = 0;
        if (!manual)
        {
            window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-2500, -2000);
            window.Shown += async (_, _) =>
            {
                try { await Verify(editor, window, english, dark); Console.WriteLine("Settings UI smoke passed: " + (english ? "en" : "ru") + "/" + (dark ? "dark" : "light")); }
                catch (Exception ex) { exitCode = 1; Console.Error.WriteLine(ex); }
                finally { window.Close(); }
            };
        }
        Application.Run(window); return exitCode;
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Verify(FullSettingsControl editor, Form window, bool english, bool dark)
    {
        var controls = Descendants(editor).ToArray();
        var search = controls.OfType<TextBox>().Single(c => c.AccessibleName == "SettingsSearch");
        var tree = controls.OfType<TreeView>().Single(); var tabs = controls.OfType<TabControl>().Single();
        var code = controls.OfType<RichTextBox>().Single(c => !c.ReadOnly); var result = controls.OfType<RichTextBox>().Single(c => c.ReadOnly);
        Button FormatButton() => Descendants(editor).OfType<Button>().Single(c => c.Text == (english ? "Format example" : "Форматировать пример"));
        Require(tabs.TabCount == 2 && tabs.TabPages[0].Text == (english ? "General" : "Общие"), "General tab must be first and localized");
        if (english) Require(!controls.Any(c => c.Text.Any(ch => ch >= 'А' && ch <= 'я')), "Russian text leaked into English settings");
        Require(code.BackColor.GetBrightness() < 0.5f == dark, "SQL preview theme mismatch");
        tabs.SelectedIndex = 1;
        for (int attempt = 0; attempt < 50 && result.Text.Length == 0; attempt++) await Task.Delay(100);
        Require(tree.Nodes.Count > 10 && result.Text.Contains("SELECT"), "Initial tree/preview unavailable: nodes=" + tree.Nodes.Count + "; output=" + result.Text + "; labels=" + string.Join(" | ", Descendants(editor).OfType<Label>().Select(l => l.Text)));
        Require(tree.SelectedNode?.Tag is SettingsField[] general && general.Any(f => f.Id == "indent.style") && general.Any(f => f.Id == "indent.size"), "Indent style and size must share a general page");
        var indent = Descendants(editor).OfType<NumericUpDown>().Single(c => c.AccessibleName == "indent.size");
        Require(indent.Maximum == 10, "Indent size must be capped at ten spaces");
        search.Text = "rules.execute.parameters.listIndent";
        Require(tree.SelectedNode?.Tag is SettingsField[] fields && fields.Length > 5 && fields.Count(f => f.RuleKey == "execute.parameters.listIndent") == 5, "Navigation must group related rules");
        var offset = Descendants(editor).OfType<NumericUpDown>().Single(c => c.AccessibleName == "rules.execute.parameters.listIndent.offset");
        offset.Value = 2; Require((int)editor.Model.Get(offset.AccessibleName) == 2, "Immediate numeric edit failed");
        code.Text = "select Id, N'Test' from dbo.Items; -- comment";
        Require(result.Text == "", "Typing retained stale preview");
        int selectionStart = code.SelectionStart; code.Select(0, 6); Color keyword = code.SelectionColor;
        code.Select(code.Text.IndexOf("N'Test'", StringComparison.Ordinal), 7); Color literal = code.SelectionColor;
        Require(keyword != literal && keyword != code.ForeColor, "SQL syntax colors not applied"); code.Select(selectionStart, 0);
        FormatButton().PerformClick(); await Task.Delay(800); Require(result.Text.Contains("SELECT"), "Custom SQL preview failed");
        search.Text = "rules.execute.parameters.stackList"; Require(code.Text.Contains("N'Test'"), "Page change replaced custom SQL");
        search.Text = "no-matching-setting"; Require(tree.Nodes.Count == 0 && result.Text == "", "No-results state retained stale output");
        search.Text = "rules.execute.parameters.stackList";
        var choice = Descendants(editor).OfType<ComboBox>().Single(c => c.AccessibleName == "rules.execute.parameters.stackList"); choice.SelectedIndex = 1;
        editor.ShowProfiles();
        var name = controls.OfType<TextBox>().Single(c => c.AccessibleName == "ActiveProfileName"); name.Text = "UI smoke";
        var hotkey = controls.OfType<TextBox>().Single(c => c.AccessibleName == "FormatShortcut");
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(hotkey, new object[] { new KeyEventArgs(Keys.Control | Keys.Alt | Keys.F) });
        Require(editor.Shortcut == "Ctrl+Alt+F", "Shortcut field did not capture the requested keys");
        int saves = 0; string? savedJson = null; editor.SaveRequested += () => { saves++; savedJson = editor.Model.Export(); };
        Require(editor.Save(), "Save failed"); Require(saves == 1 && window.Visible, "Save must persist without closing");
        Require(editor.Profiles.Contains("UI smoke") && editor.Profiles.DefaultProfileId == "user:UI smoke", "Save must update the active named profile");
        Require(UserProfileStore.Deserialize(editor.Profiles.Serialize()).DefaultProfileId == "user:UI smoke", "Active profile not persisted");
        var selector = controls.OfType<ComboBox>().Single(c => c.AccessibleName == "GeneralActiveProfile"); selector.SelectedIndex = 2;
        var secondSelector = controls.OfType<ComboBox>().Single(c => c.AccessibleName == "FormattingActiveProfile");
        Require(selector.SelectedIndex == secondSelector.SelectedIndex && editor.Profiles.DefaultProfileId == "builtin:Compact", "Active profile selectors are not synchronized");
        selector.SelectedIndex = selector.Items.Count - 1; Require(editor.Profiles.DefaultProfileId == "user:UI smoke", "Named profile cannot be reselected");
        tabs.SelectedIndex = 1; search.Clear(); code.Text = "select Id, Name from dbo.Items where Id=1;"; FormatButton().PerformClick(); await Task.Delay(800);
        Capture(window, english, dark, "formatting");
        window.Size = window.MinimumSize;
        Require(search.Visible && code.Visible && tree.Visible, "Controls missing at minimum window size");
        window.Size = new Size(1280, 860); tabs.SelectedIndex = 0; Capture(window, english, dark, "general");
        tabs.SelectedIndex = 1; code.Text = "SELECT FROM;"; FormatButton().PerformClick(); await Task.Delay(800); Require(result.Text == "", "Invalid SQL produced formatted output");
        search.Text = "indent.size"; indent = Descendants(editor).OfType<NumericUpDown>().Single(c => c.AccessibleName == "indent.size"); indent.Value = 3;
        Require(saves == 1 && savedJson != editor.Model.Export(), "Unsaved changes must not update the saved baseline");
        window.DialogResult = DialogResult.Cancel;
    }
    private static void Capture(Form window, bool english, bool dark, string tab)
    {
        string directory = Path.GetFullPath("artifacts/settings-ui"); Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
        bitmap.Save(Path.Combine(directory, (english ? "en" : "ru") + "-" + (dark ? "dark" : "light") + "-" + tab + ".png"));
    }
}
