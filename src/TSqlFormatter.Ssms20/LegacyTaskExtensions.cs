using System;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.VisualStudio.Threading;

// SDK 15 has JoinableTask but predates the SDK 17 FileAndForget extension.
internal static class LegacyTaskExtensions
{
    internal static async void FileAndForget(this JoinableTask task, string eventName)
    {
        try { await task.Task; }
        catch (Exception exception) { ActivityLog.LogError(eventName, exception.ToString()); }
    }
}
