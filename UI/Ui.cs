using Copeer.Core;

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

    /// <summary>Кастомный select: выпадающий список рисуется в цветах темы с подсветкой пунктов.</summary>
    public static void ThemedCombo(ComboBox cb)
    {
        cb.DrawMode = DrawMode.OwnerDrawFixed;
        cb.ItemHeight = cb.Font.Height + 4;
        cb.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            bool hl = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(hl ? Theme.SelRow : Theme.Field))
                e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, e.Bounds,
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
    }

    /// <summary>
    /// Перетащить безрамочное окно за клиентскую область контрола (фоны панелей/строк).
    /// Кликабельные дочерние контролы (кнопки, поля) не мешают — MouseDown до них не доходит.
    /// </summary>
    public static void MakeDraggable(Form f, params Control[] grabbers)
    {
        bool drag = false;
        Point cursor = Point.Empty, loc = Point.Empty;
        foreach (var c in grabbers)
        {
            c.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                drag = true;
                cursor = Cursor.Position;
                loc = f.Location;
            };
            c.MouseMove += (s, e) =>
            {
                if (!drag) return;
                f.Location = new Point(loc.X + Cursor.Position.X - cursor.X, loc.Y + Cursor.Position.Y - cursor.Y);
            };
            c.MouseUp += (s, e) => drag = false;
        }
    }

    /// <summary>
    /// Жизнь всплывающих панелей (Win+V, Win+.) по настройке PanelHideMode:
    /// «focus» — скрыть, как только панель потеряла и фокус, и курсор;
    /// «timer» — после потери прожить PanelHideSec секунд (0 — сразу);
    /// «never» — не скрывать саму (Esc / правый клик / Отмена).
    /// </summary>
    public static bool PanelShouldHide(Form f, Core.Settings s, DateTime? leftAt, out DateTime? newLeftAt)
    {
        newLeftAt = null;
        switch (s.PanelHideMode)
        {
            case "never":
                return false;
            case "focus":
                return !f.ContainsFocus && !f.Bounds.Contains(Cursor.Position);
            default: // timer
                if (f.ContainsFocus || f.Bounds.Contains(Cursor.Position)) return false;
                int t = s.PanelHideSec;
                if (t <= 0) return true;
                newLeftAt = leftAt ?? DateTime.UtcNow;
                return (DateTime.UtcNow - newLeftAt.Value).TotalSeconds >= t;
        }
    }
}
