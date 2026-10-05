using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TSqlFormatter.IdeShared;

internal sealed class SettingsAppearance
{
    internal static CultureInfo Culture { get; set; } = CultureInfo.CurrentUICulture;
    internal static bool Russian => Culture.TwoLetterISOLanguageName == "ru";
    internal static string Text(string ru, string en) => Russian ? ru : en;
    internal Color Background { get; }
    internal Color Foreground { get; }
    internal Color Input { get; }
    internal Color Accent { get; }
    internal bool Dark => Background.GetBrightness() < 0.5f;
    internal SettingsAppearance(Color background, Color foreground, Color input, Color accent)
    { Background = background; Foreground = foreground; Input = input; Accent = accent; }
    internal static SettingsAppearance Light => new(Color.White, Color.FromArgb(32, 32, 32), Color.FromArgb(248, 248, 248), Color.FromArgb(0, 122, 204));
    internal void Apply(Control control)
    {
        control.BackColor = control is TextBoxBase or ComboBox or NumericUpDown or TreeView ? Input : Background;
        if (control.AccessibleName == "SearchBox" || control.Parent?.AccessibleName == "SearchBox") control.BackColor = Input;
        control.ForeColor = Foreground;
        if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Accent; button.UseVisualStyleBackColor = false; }
        if (control is CheckBox check) { check.FlatStyle = FlatStyle.Flat; check.UseVisualStyleBackColor = false; }
        if (control is SettingsTabControl tab) { tab.ThemeBackground = Background; tab.ThemeForeground = Foreground; tab.Accent = Accent; }
        if (control is Form form && form.IsHandleCreated) ApplyCaption(form);
        foreach (Control child in control.Controls) Apply(child);
    }
    internal void ApplyCaption(Form form) { int dark = Dark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}

internal sealed class SettingsComboBox : ComboBox
{
    internal SettingsComboBox() { DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; ItemHeight = 23; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        Color background = (e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(0, 122, 204) : BackColor;
        using var brush = new SolidBrush(background); e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds,
            (e.State & DrawItemState.Selected) != 0 ? Color.White : ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        e.DrawFocusRectangle();
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is not (0x000F or 0x0318) || !IsHandleCreated) return;
        using var graphics = m.Msg == 0x0318 && m.WParam != IntPtr.Zero ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(Handle);
        var button = new Rectangle(ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 1, 1, SystemInformation.VerticalScrollBarWidth, ClientSize.Height - 2);
        using var background = new SolidBrush(BackColor); graphics.FillRectangle(background, button);
        int x = button.Left + button.Width / 2, y = button.Top + button.Height / 2;
        using var foreground = new SolidBrush(ForeColor); graphics.FillPolygon(foreground, new[] { new Point(x - 4, y - 2), new Point(x + 4, y - 2), new Point(x, y + 2) });
    }
}

internal sealed class SettingsTabControl : TabControl
{
    internal Color Accent { get; set; } = Color.FromArgb(0, 122, 204);
    internal Color ThemeBackground { get; set; } = Color.White;
    internal Color ThemeForeground { get; set; } = Color.Black;
    internal SettingsTabControl() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ThemeBackground);
        for (int i = 0; i < TabPages.Count; i++)
        {
            Rectangle bounds = GetTabRect(i);
            TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, bounds, ThemeForeground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (SelectedIndex == i) { using var pen = new Pen(Accent, 3); e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 2, bounds.Right, bounds.Bottom - 2); }
        }
    }
}
