namespace Copeer.Core;

/// <summary>
/// Скрытое окно: принимает WM_HOTKEY (RegisterHotKey) и уведомления буфера обмена
/// (AddClipboardFormatListener). Никогда не показывается.
/// </summary>
public class HotkeyHost : Form
{
    public Action<int>? Hotkey;
    public Action? ClipboardChanged;

    public HotkeyHost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
        Opacity = 0;
        Text = "Copeer host";
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.AddClipboardFormatListener(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.RemoveClipboardFormatListener(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            Hotkey?.Invoke(m.WParam.ToInt32());
            return;
        }
        if (m.Msg == Native.WM_CLIPBOARDUPDATE)
        {
            // откладываем чтение буфера, чтобы не держать его открытым внутри уведомления
            BeginInvoke(() => { try { ClipboardChanged?.Invoke(); } catch { } });
            return;
        }
        base.WndProc(ref m);
    }
}
