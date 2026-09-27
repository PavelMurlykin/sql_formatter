using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.VisualStudio;

/// <summary>Non-persistent sample and live Core preview of the IDE options.</summary>
public sealed class PreviewOptionsPage : DialogPage
{
    internal Func<FormattingOptions>? OptionsProvider { get; set; }

    protected override IWin32Window Window => new PreviewControl(
        () => OptionsProvider?.Invoke() ?? FormattingOptions.Default);
}

internal sealed class PreviewControl : UserControl
{
    private const string DefaultSample =
        "select c.id, c.name from dbo.Customer c left join dbo.Orders o on o.CustomerId = c.id " +
        "where c.IsActive = 1 order by c.name;";

    private readonly Func<FormattingOptions> optionsProvider;
    private readonly TextBox sample = new();
    private readonly TextBox result = new();
    private readonly Label status = new();
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    private int revision;

    internal PreviewControl(Func<FormattingOptions> optionsProvider)
    {
        this.optionsProvider = optionsProvider;
        Size = new Size(650, 430);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        sample.Dock = DockStyle.Fill;
        sample.Multiline = true;
        sample.ScrollBars = ScrollBars.Both;
        sample.WordWrap = false;
        sample.MaxLength = 4096;
        sample.Font = new Font(FontFamily.GenericMonospace, 9);
        sample.Text = DefaultSample;
        sample.TextChanged += (_, _) => QueuePreview();

        result.Dock = DockStyle.Fill;
        result.Multiline = true;
        result.ReadOnly = true;
        result.ScrollBars = ScrollBars.Both;
        result.WordWrap = false;
        result.Font = new Font(FontFamily.GenericMonospace, 9);

        status.Dock = DockStyle.Fill;
        status.AutoSize = true;
        status.Text = "Preview uses IDE options; project configuration is not loaded.";
        layout.Controls.Add(new Label { Text = "SQL sample", AutoSize = true }, 0, 0);
        layout.Controls.Add(sample, 0, 1);
        layout.Controls.Add(new Label { Text = "Formatted preview", AutoSize = true }, 0, 2);
        layout.Controls.Add(result, 0, 3);
        layout.Controls.Add(status, 0, 4);
        Controls.Add(layout);

        debounce.Tick += (_, _) => RefreshPreview();
        VisibleChanged += (_, _) => { if (Visible) QueuePreview(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        QueuePreview();
    }

    private void QueuePreview()
    {
        if (IsDisposed) return;
        revision++;
        debounce.Stop();
        debounce.Start();
    }

    private void RefreshPreview()
    {
        debounce.Stop();
        int currentRevision = revision;
        string sql = sample.Text;
        FormattingOptions options;
        try
        {
            options = optionsProvider();
        }
        catch (Exception ex)
        {
            status.Text = $"Invalid IDE options: {ex.Message}";
            result.Clear();
            return;
        }

        status.Text = "Formatting preview…";
        _ = Task.Run(() => new ScriptDomSqlFormatter().Format(sql, options, new FormatRequest()))
            .ContinueWith(completed =>
            {
                if (IsDisposed || currentRevision != revision) return;
                if (completed.IsFaulted)
                {
                    result.Clear();
                    status.Text = $"Preview failed: {completed.Exception?.GetBaseException().Message}";
                    return;
                }

                FormatResult formatted = completed.Result;
                var error = formatted.Diagnostics.Count > 0 ? formatted.Diagnostics[0] : null;
                result.Text = error == null ? formatted.Text : string.Empty;
                status.Text = error == null ? "Preview ready (not saved to the SQL document)."
                    : $"{error.Code}: {error.Message}";
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) debounce.Dispose();
        base.Dispose(disposing);
    }
}
