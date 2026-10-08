namespace Copeer.UI;

/// <summary>Общие UI-хелперы.</summary>
public static class Ui
{
    /// <summary>Размещает всплывающую панель: у курсора или по центру экрана (настройка PanelPosition).</summary>
    public static void PlacePopup(Form f, Core.Settings s)
    {
        var p = Cursor.Position;
        var wa = Screen.FromPoint(p).WorkingArea;

        if (s.PanelPosition == "center")
        {
            f.Location = new Point(wa.Left + (wa.Width - f.Width) / 2, wa.Top + (wa.Height - f.Height) / 2);
            return;
        }

        var loc = new Point(p.X + 10, p.Y + 10);
        if (loc.X + f.Width > wa.Right) loc.X = wa.Right - f.Width - 4;
        if (loc.Y + f.Height > wa.Bottom) loc.Y = Math.Max(wa.Top, p.Y - f.Height - 10);
        f.Location = loc;
    }

    /// <summary>Повторная попытка забрать фокус — окно могло не активироваться из-за чужого активного окна.</summary>
    public static void RetryFocus(Form f)
    {
        var t = new System.Windows.Forms.Timer { Interval = 150 };
        t.Tick += (s, e) =>
        {
            t.Stop();
            t.Dispose();
            if (!f.IsDisposed && f.Visible && !f.ContainsFocus)
                f.Activate();
        };
        t.Start();
    }
}
