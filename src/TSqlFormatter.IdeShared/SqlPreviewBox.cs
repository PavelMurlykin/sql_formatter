using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSqlFormatter.IdeShared;

/// <summary>Token-based highlighting also works for incomplete sample SQL; text and selection are retained.</summary>
internal sealed class SqlPreviewBox : RichTextBox
{
    private bool highlighting;
    private SettingsAppearance appearance = SettingsAppearance.Light;
    internal SqlPreviewBox(bool readOnly)
    {
        Dock = DockStyle.Fill; ReadOnly = readOnly; WordWrap = false; DetectUrls = false;
        AcceptsTab = true; ScrollBars = RichTextBoxScrollBars.Both;
        Font = new Font("Consolas", 10); BorderStyle = BorderStyle.None;
    }
    internal void ApplyAppearance(SettingsAppearance value) { appearance = value; Highlight(); }
    protected override void OnTextChanged(EventArgs e)
    {
        if (highlighting) return;
        Highlight(); base.OnTextChanged(e);
    }
    private void Highlight()
    {
        if (highlighting || IsDisposed) return;
        highlighting = true;
        int start = SelectionStart, length = SelectionLength;
        Point scroll = default;
        if (IsHandleCreated) { SendMessage(Handle, 0x04DD, IntPtr.Zero, ref scroll); SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); }
        try
        {
            SelectAll(); SelectionColor = appearance.Foreground;
            var tokens = new TSql160Parser(true).GetTokenStream(new System.IO.StringReader(Text), out _);
            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token.Text)) continue;
                if (token.Offset < 0 || token.Offset + token.Text.Length > TextLength) continue;
                string type = token.TokenType.ToString();
                Color color = type.Contains("Comment") ? (appearance.Dark ? Color.FromArgb(106, 153, 85) : Color.FromArgb(0, 128, 0))
                    : type.Contains("StringLiteral") ? (appearance.Dark ? Color.FromArgb(206, 145, 120) : Color.FromArgb(163, 21, 21))
                    : type.Contains("Integer") || type.Contains("Numeric") || type.Contains("Real") ? (appearance.Dark ? Color.FromArgb(181, 206, 168) : Color.FromArgb(9, 134, 88))
                    : token.TokenType == TSqlTokenType.Variable ? (appearance.Dark ? Color.FromArgb(156, 220, 254) : Color.FromArgb(111, 0, 138))
                    : IsKeyword(token) ? (appearance.Dark ? Color.FromArgb(86, 156, 214) : Color.Blue) : appearance.Foreground;
                Select(token.Offset, token.Text.Length); SelectionColor = color;
            }
        }
        finally
        {
            Select(Math.Min(start, TextLength), Math.Min(length, TextLength - Math.Min(start, TextLength)));
            if (IsHandleCreated) { SendMessage(Handle, 0x04DE, IntPtr.Zero, ref scroll); SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero); Invalidate(); }
            highlighting = false;
        }
    }
    private static bool IsKeyword(TSqlParserToken token) => token.TokenType != TSqlTokenType.Identifier &&
        token.TokenType != TSqlTokenType.QuotedIdentifier && token.TokenType != TSqlTokenType.WhiteSpace &&
        token.TokenType != TSqlTokenType.EndOfFile && token.Text.Length > 0 && char.IsLetter(token.Text[0]);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, ref Point lparam);
}
