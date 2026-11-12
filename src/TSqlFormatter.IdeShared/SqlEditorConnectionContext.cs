using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using TSqlFormatter.Configuration;

namespace TSqlFormatter.IdeShared;

internal static class SqlEditorConnectionContext
{
    internal static SqlEnvironmentContext Read(EnvDTE.DTE? dte, string documentMoniker)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (dte?.ActiveDocument is not { } active ||
                !string.Equals(active.FullName, documentMoniker, StringComparison.OrdinalIgnoreCase))
                return new(documentMoniker);
            // Read only the existing active window's names. Never open a connection
            // or inspect connection strings, credentials, or the last-used server.
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache", false))
                .FirstOrDefault(t => t != null);
            var factory = type?.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var info = Property(factory, "CurrentlyActiveWndConnectionInfo");
            string? server = Property(info, "ServerName") as string;
            string? database = Property(info, "DatabaseName") as string;
            if (database == null && Property(info, "AdvancedOptions") is IDictionary options)
                database = options["DATABASE"] as string;
            return new(documentMoniker, server, database);
        }
        catch (Exception) { return new(documentMoniker); }
    }
    private static object? Property(object? owner, string name)
    {
        if (owner == null) return null;
        var type = owner.GetType();
        var property = type.GetProperty(name) ?? type.GetInterfaces().Select(i => i.GetProperty(name)).FirstOrDefault(p => p != null);
        return property?.GetValue(owner);
    }
}
