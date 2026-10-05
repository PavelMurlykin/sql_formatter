using System;
using System.Globalization;
using System.Linq;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace TSqlFormatter.IdeShared;

internal static class IdeHostIntegration
{
    internal static void SetLanguage(EnvDTE.DTE? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (dte is not null) SettingsAppearance.Culture = CultureInfo.GetCultureInfo(dte.LocaleID);
    }
    internal static SettingsAppearance Appearance() => new(
        VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey),
        VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey),
        VSColorTheme.GetThemedColor(EnvironmentColors.ComboBoxBackgroundColorKey),
        VSColorTheme.GetThemedColor(EnvironmentColors.SystemHighlightColorKey));
    internal static string Shortcut(EnvDTE.DTE? dte, Guid commandSet)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (dte is null) return "";
        var command = dte.Commands.Item(commandSet.ToString("B"), 0x0200);
        return command.Bindings is object[] bindings ? string.Join("; ", bindings.Cast<string>().Select(b => b.Substring(b.IndexOf("::", StringComparison.Ordinal) + 2))) : "";
    }
    internal static void SetShortcut(EnvDTE.DTE? dte, Guid commandSet, string shortcut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (shortcut == Shortcut(dte, commandSet)) return;
        if (dte is null) throw new InvalidOperationException(SettingsAppearance.Text("Служба горячих клавиш IDE недоступна.", "IDE keyboard service is unavailable."));
        var command = dte.Commands.Item(commandSet.ToString("B"), 0x0200);
        // Keep the existing scope, including its localized spelling. New bindings use the editor scope.
        string scope = "Text Editor";
        if (command.Bindings is object[] existing && existing.Length > 0 && existing[0] is string binding)
            scope = binding.Substring(0, binding.IndexOf("::", StringComparison.Ordinal));
        else
        {
            var copy = dte.Commands.Item("Edit.Copy", 0).Bindings as object[];
            string? editorBinding = copy?.Cast<string>().FirstOrDefault(b => b.IndexOf("Ctrl+C", StringComparison.OrdinalIgnoreCase) >= 0);
            if (editorBinding is not null) scope = editorBinding.Substring(0, editorBinding.IndexOf("::", StringComparison.Ordinal));
        }
        string requested = scope + "::" + shortcut;
        if (shortcut.Length > 0)
            foreach (EnvDTE.Command other in dte.Commands)
                if (!(other.Guid == command.Guid && other.ID == command.ID) && other.Bindings is object[] otherBindings &&
                    otherBindings.Cast<string>().Any(b => string.Equals(b, requested, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(SettingsAppearance.Text("Сочетание уже назначено команде: ", "Shortcut is already assigned to: ") + other.Name);
        command.Bindings = shortcut.Length == 0 ? Array.Empty<object>() : new object[] { requested };
    }
}
