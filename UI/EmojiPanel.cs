using Copeer.Core;

namespace Copeer.UI;

/// <summary>
/// Всплывающая панель (Win+.): вкладки Эмодзи / Символы / GIF, поиск.
/// По умолчанию открыты эмодзи. Клик по элементу — вставка в прежнее окно (как Ctrl+V).
/// GIF — поиск через Tenor без своего API-ключа; превью анимированные.
/// </summary>
class EmojiPanel : Form
{
    enum Tab { Emoji, Symbols, Gif }

    readonly AppContext _c;
    readonly TextBox _search;
    readonly FlowLayoutPanel _tabs;
    readonly FlowLayoutPanel _chips;
    readonly FlowLayoutPanel _grid;
    readonly List<(Button Btn, EmojiData.Sym Sym)> _buttons = new();
    readonly Dictionary<EmojiData.Sym, Button> _symButtons = new();
    readonly List<Button> _orderedButtons = new(); // видимый порядок после фильтра — для навигации стрелками
    readonly List<Button> _chipButtons = new();
    readonly List<Button> _tabButtons = new();
    readonly List<Image> _emojiImages = new();  // цветные PNG (Twemoji) — освобождаются при закрытии
    readonly List<Control> _gifControls = new(); // превью и подсказки вкладки GIF
    readonly List<Image> _gifImages = new();
    readonly List<MemoryStream> _gifStreams = new(); // GDI+ читает кадры GIF лениво — поток живёт с Image
    readonly System.Windows.Forms.Timer _gifTimer = new() { Interval = 600, Enabled = false };
    readonly List<GifItem> _gifItems = new(); // накопленная выдача: страницы склеиваются
    string? _gifCursor;                       // курсор Tenor следующей страницы
    int _gifPage;                             // текущая страница пагинации
    bool _gifLoading;
    readonly Panel _pager = null!;
    readonly Button _gifPrev = null!;
    readonly Button _gifNextB = null!;
    readonly Label _gifPageLbl = null!;
    CancellationTokenSource? _gifCts;
    Tab _tab = Tab.Emoji;

    static readonly string EmojiDir = Path.Combine(Application.StartupPath, "emoji");
    readonly ToolTip _tip = new();
    string _cat = "Все";
    readonly System.Windows.Forms.Timer _life = new() { Interval = 250 };
    DateTime? _leftAt;

    public EmojiPanel(AppContext c)
    {
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Back;
        KeyPreview = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(432, 470);

        var top = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Back, Padding = new Padding(10, 8, 10, 4) };
        _search = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10.5f)
        };
        _search.TextChanged += (s, e) => ApplyFilter();
        _search.KeyDown += (s, e) =>
        {
            // Down из поиска — к первой кнопке сетки; Enter — искать GIF сразу
            if (e.KeyCode == Keys.Down && _orderedButtons.Count > 0)
            {
                e.Handled = e.SuppressKeyPress = true;
                _orderedButtons[0].Focus();
            }
            else if (e.KeyCode == Keys.Enter && _tab == Tab.Gif)
            {
                e.Handled = e.SuppressKeyPress = true;
                _gifTimer.Stop();
                RunGifSearch(_search.Text.Trim());
            }
        };
        top.Controls.Add(_search);

        // вкладки: Эмодзи | Символы | GIF
        _tabs = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Theme.Back,
            Padding = new Padding(8, 4, 8, 2),
            WrapContents = false
        };
        foreach (var (name, tab) in new[] { ("Эмодзи", Tab.Emoji), ("Символы", Tab.Symbols), ("GIF", Tab.Gif) })
        {
            var b = new Button
            {
                Text = name,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Chip,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9.5f),
                Margin = new Padding(2, 2, 2, 2),
                Padding = new Padding(10, 4, 10, 4),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            var captured = tab;
            b.Click += (s, e) => SetTab(captured);
            _tabs.Controls.Add(b);
            _tabButtons.Add(b);
        }

        _chips = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,          // высота по содержимому: категории переносятся на 2-ю строку, ничего не обрезается
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Back,
            Padding = new Padding(8, 2, 8, 2),
            WrapContents = true
        };
        foreach (var cat in EmojiData.Categories)
        {
            var b = new Button
            {
                Text = cat,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Chip,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(2, 4, 2, 2),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.Click += (s, e) => { _cat = cat; MarkChips(); ApplyFilter(); };
            _chips.Controls.Add(b);
            _chipButtons.Add(b);
        }

        _grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Back,
            Padding = new Padding(6)
        };
        _grid.HandleCreated += (s, e) => Native.ScrollbarTheme(_grid.Handle);
        foreach (var sym in EmojiData.All)
        {
            var b = new Button
            {
                Width = 38,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Margin = new Padding(1),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;

            // GDI-шрифт не рисует цветные эмодзи — используем готовые PNG (Twemoji),
            // а для символов без ассета (тире, стрелки…) остаётся шрифтовой глиф
            var imgPath = EmojiImagePath(sym.Ch);
            if (imgPath != null)
            {
                try
                {
                    using var src = new Bitmap(imgPath);
                    var img = new Bitmap(src, 28, 28);
                    _emojiImages.Add(img);
                    b.Image = img;
                    b.ImageAlign = ContentAlignment.MiddleCenter;
                }
                catch { }
            }
            if (b.Image == null)
            {
                b.Text = sym.Ch;
                b.Font = new Font(sym.Cat == EmojiData.SymbolsCat ? "Segoe UI" : "Segoe UI Emoji", 15f);
            }

            b.Click += (s, e) =>
            {
                _c.AddRecentSymbol(sym.Ch);
                Paster.PasteText(_c, sym.Ch);
            };
            _tip.SetToolTip(b, sym.Name);
            _grid.Controls.Add(b);
            _buttons.Add((b, sym));
            _symButtons[sym] = b;
        }

        // пагинация вкладки GIF — внизу окна
        _pager = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Theme.Panel, Visible = false, Padding = new Padding(6, 5, 6, 5) };
        var pagerBar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, WrapContents = false };
        _gifPrev = new Button
        {
            Text = "‹", Width = 34, Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Button, ForeColor = Theme.Text,
            Cursor = Cursors.Hand, TabStop = false, Enabled = false,
            Margin = new Padding(2, 0, 2, 0)
        };
        _gifPrev.FlatAppearance.BorderSize = 0;
        _gifPrev.Click += (s, e) => { if (!_gifLoading && _gifPage > 0) { _gifPage--; RenderGifPage(); } };
        _gifPageLbl = new Label
        {
            Text = "", AutoSize = true,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 6, 8, 0)
        };
        _gifNextB = new Button
        {
            Text = "›", Width = 34, Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Button, ForeColor = Theme.Text,
            Cursor = Cursors.Hand, TabStop = false, Enabled = false,
            Margin = new Padding(2, 0, 2, 0)
        };
        _gifNextB.FlatAppearance.BorderSize = 0;
        _gifNextB.Click += (s, e) => { if (!_gifLoading) _ = GifGoNextAsync(); };
        pagerBar.Controls.AddRange(new Control[] { _gifPrev, _gifPageLbl, _gifNextB });
        _pager.Controls.Add(pagerBar);

        Controls.Add(_grid);
        Controls.Add(_pager);
        Controls.Add(_chips);
        Controls.Add(_tabs);
        Controls.Add(top);

        // правый клик — закрыть (для режима «пока не закроют»)
        MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) HideLive(); };
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) { HideLive(); return; }
            // стрелки между кнопками сетки, когда фокус на одной из них
            if (ActiveControl is Button && _grid.Contains(ActiveControl))
                MoveGridFocus(e);
        };
        _life.Tick += (s, e) => TickLife();
        _gifTimer.Tick += (s, e) => RunGifSearch(_search.Text.Trim());
        MarkChips();
        MarkTabs();
    }

    // видимость панели — по настройке PanelHideMode (см. Ui.PanelShouldHide)
    void TickLife()
    {
        if (Ui.PanelShouldHide(this, _c.S, _leftAt, out var left))
        {
            HideLive();
            return;
        }
        _leftAt = left;
    }

    void HideLive()
    {
        _leftAt = null;
        _life.Stop();
        Hide();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _life.Stop();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } // CS_DROPSHADOW
    }

    public void Open()
    {
        _search.Text = "";
        _cat = "Все";
        MarkChips();
        SetTab(Tab.Emoji); // по умолчанию открыты эмодзи

        Ui.PlacePopup(this, _c.S);

        _leftAt = null;
        Show();
        Activate();
        _search.Focus();
        Ui.RetryFocus(this);
        _life.Start();
    }

    void SetTab(Tab tab)
    {
        bool changed = tab != _tab;
        _tab = tab;
        MarkTabs();
        _chips.Visible = tab == Tab.Emoji;
        _pager.Visible = tab == Tab.Gif;
        if (changed)
        {
            ClearGif();
            if (tab == Tab.Gif)
            {
                // спрятать кнопки эмодзи/символов — сетка теперь только для GIF,
                // иначе они остаются висеть перед превью (ApplyFilter тут не вызывается)
                HideSymbolButtons();
                // сразу показываем топ (или поиск, если запрос уже набран)
                RunGifSearch(_search.Text.Trim());
                return;
            }
        }
        _search.PlaceholderText = tab switch
        {
            Tab.Emoji => "поиск по-русски: сердце, стрелка…",
            Tab.Symbols => "поиск: тире, номер, галочка…",
            _ => "поиск GIF: cat, кот танцует…"
        };
        ApplyFilter();
    }

    void MarkTabs()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool active = i == (int)_tab;
            _tabButtons[i].BackColor = active ? Theme.Accent : Theme.Chip;
            _tabButtons[i].ForeColor = active ? Color.White : Theme.Text;
        }
    }

    void MoveGridFocus(KeyEventArgs e)
    {
        if (_orderedButtons.Count == 0) return;
        int idx = ActiveControl is Button b ? _orderedButtons.IndexOf(b) : -1;
        if (idx < 0) { _orderedButtons[0].Focus(); return; }

        int columns = Math.Max(1, (_grid.ClientSize.Width - _grid.Padding.Horizontal) / 40);
        int target = e.KeyCode switch
        {
            Keys.Left => idx - 1,
            Keys.Right => idx + 1,
            Keys.Up => idx - columns,
            Keys.Down => idx + columns,
            Keys.Home => 0,
            Keys.End => _orderedButtons.Count - 1,
            _ => idx
        };
        if (target >= 0 && target < _orderedButtons.Count)
        {
            e.Handled = e.SuppressKeyPress = true;
            _orderedButtons[target].Focus();
        }
    }

    void MarkChips()
    {
        for (int i = 0; i < _chipButtons.Count; i++)
            _chipButtons[i].BackColor = EmojiData.Categories[i] == _cat ? Theme.Accent : Theme.Chip;
    }

    // кнопки эмодзи/символов прячутся — на вкладке GIF сетка только для превью
    void HideSymbolButtons()
    {
        foreach (var (btn, _) in _buttons) btn.Visible = false;
        _orderedButtons.Clear(); // стрелки на вкладке GIF не нужны
    }

    void ApplyFilter()
    {
        if (_tab == Tab.Gif)
        {
            // поиск GIF — с задержкой, чтобы не дёргать сеть на каждую букву
            HideSymbolButtons();
            _gifTimer.Stop();
            _gifTimer.Start();
            return;
        }

        ClearGif();
        _grid.SuspendLayout();
        var q = _search.Text.Trim();
        var ordered = new List<EmojiData.Sym>();

        // недавние — вверху вкладки эмодзи, пока нет запроса и выбрана категория «Все»
        if (q.Length == 0 && _tab == Tab.Emoji && _cat == "Все")
            foreach (var ch in _c.S.RecentSymbols)
            {
                var f = EmojiData.Find(ch);
                if (f != null && f.Cat != EmojiData.SymbolsCat && !ordered.Contains(f)) ordered.Add(f);
            }

        foreach (var s in EmojiData.All)
        {
            if (_tab == Tab.Emoji)
            {
                if (s.Cat == EmojiData.SymbolsCat) continue;
                if (_cat != "Все" && s.Cat != _cat) continue;
            }
            else if (s.Cat != EmojiData.SymbolsCat) continue;
            if (q.Length > 0 && !s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ordered.Contains(s)) ordered.Add(s);
        }

        var visible = new HashSet<EmojiData.Sym>(ordered);
        for (int i = 0; i < ordered.Count; i++)
        {
            var pair = _buttons.FirstOrDefault(b => b.Sym == ordered[i]);
            if (pair.Btn != null) _grid.Controls.SetChildIndex(pair.Btn, i);
        }
        foreach (var (btn, sym) in _buttons) btn.Visible = visible.Contains(sym);

        _orderedButtons.Clear();
        foreach (var s in ordered)
            if (_symButtons.TryGetValue(s, out var b)) _orderedButtons.Add(b);

        _grid.ResumeLayout();
    }

    // ——— вкладка GIF ———

    // пустой запрос = топ (featured): стартовое содержимое вкладки без поиска
    async void RunGifSearch(string q)
    {
        _gifCts?.Cancel();
        _gifCts?.Dispose();
        _gifCts = new CancellationTokenSource();
        var ct = _gifCts.Token;
        _gifLoading = true;

        _gifItems.Clear();
        _gifCursor = null;
        _gifPage = 0;
        ClearGif();
        AddGifHint("Ищу…");
        UpdatePager();
        try
        {
            var page = q.Length == 0
                ? await GifSearch.FeaturedAsync(ct: ct)
                : await GifSearch.SearchAsync(q, ct: ct);
            if (_tab != Tab.Gif || ct.IsCancellationRequested) return;
            _gifItems.AddRange(page.Items);
            _gifCursor = page.Next;
            RenderGifPage();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Error("gif search", e);
            if (_tab == Tab.Gif && !ct.IsCancellationRequested)
            {
                ClearGif();
                AddGifHint("Ошибка поиска: " + e.Message);
            }
        }
        finally
        {
            _gifLoading = false;
            UpdatePager();
        }
    }

    // следующая страница: сначала локальные страницы, потом догрузка по курсору Tenor
    async Task GifGoNextAsync()
    {
        if (_gifLoading) return;
        int from = (_gifPage + 1) * GifSearch.PageSize;
        if (from < _gifItems.Count)
        {
            _gifPage++;
            RenderGifPage();
            return;
        }
        if (_gifCursor == null) return;

        _gifLoading = true;
        _gifCts?.Cancel();
        _gifCts?.Dispose();
        _gifCts = new CancellationTokenSource();
        var ct = _gifCts.Token;
        UpdatePager();
        try
        {
            var q = _search.Text.Trim();
            var page = q.Length == 0
                ? await GifSearch.FeaturedAsync(_gifCursor, ct)
                : await GifSearch.SearchAsync(q, _gifCursor, ct);
            if (_tab != Tab.Gif || ct.IsCancellationRequested) return;
            _gifItems.AddRange(page.Items);
            _gifCursor = page.Next;
            if (from < _gifItems.Count) _gifPage++;
            RenderGifPage();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log.Error("gif next page", e); }
        finally
        {
            _gifLoading = false;
            UpdatePager();
        }
    }

    void RenderGifPage()
    {
        ClearGif();
        var ct = _gifCts?.Token ?? CancellationToken.None;

        int from = _gifPage * GifSearch.PageSize;
        int to = Math.Min(from + GifSearch.PageSize, _gifItems.Count);
        if (_gifItems.Count == 0)
        {
            AddGifHint(_gifLoading ? "Ищу…" : "Ничего не найдено");
        }
        else
        {
            for (int i = from; i < to; i++) AddGifPreview(_gifItems[i], ct);
        }
        UpdatePager();
    }

    void UpdatePager()
    {
        int pages = Math.Max(1, (_gifItems.Count + GifSearch.PageSize - 1) / GifSearch.PageSize);
        if (pages < _gifPage + 1) pages = _gifPage + 1;
        _gifPageLbl.Text = _gifLoading && _gifItems.Count == 0 ? "…" : $"стр {_gifPage + 1} из {pages}";
        _gifPrev.Enabled = _gifPage > 0 && !_gifLoading;
        int from = (_gifPage + 1) * GifSearch.PageSize;
        _gifNextB.Enabled = !_gifLoading && (from < _gifItems.Count || _gifCursor != null);
    }

    void AddGifPreview(GifItem item, CancellationToken ct)
    {
        var pb = new PictureBox
        {
            Size = new Size(96, 72),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Panel,
            Margin = new Padding(2),
            Cursor = Cursors.Hand
        };
        pb.Click += (s, e) => _ = InsertGifAsync(item);
        _tip.SetToolTip(pb, "вставить GIF");
        _grid.Controls.Add(pb);
        _gifControls.Add(pb);
        _ = LoadPreviewAsync(pb, item, ct);
    }

    async Task LoadPreviewAsync(PictureBox pb, GifItem item, CancellationToken ct)
    {
        try
        {
            var bytes = await GifSearch.GetBytesAsync(item.PreviewUrl, ct);
            if (ct.IsCancellationRequested || pb.IsDisposed) return;
            var ms = new MemoryStream(bytes);
            var img = Image.FromStream(ms);
            _gifStreams.Add(ms);
            _gifImages.Add(img);
            pb.Image = img; // PictureBox анимирует GIF сам
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log.Error("gif preview", e); }
    }

    async Task InsertGifAsync(GifItem item)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var file = await GifSearch.DownloadAsync(item);
            if (file == null) return;

            // HTML с локальной картинкой: Word/браузеры/мессенджеры вставят анимацию;
            // текстом кладём ссылку — куда HTML не доходит
            var data = new DataObject();
            data.SetText(item.FullUrl);
            data.SetData(DataFormats.Html,
                HtmlClipboard.Wrap($"<img src=\"{HtmlClipboard.FileUrl(file)}\">"));
            Paster.PasteDataObject(_c, data);
        }
        finally { Cursor = Cursors.Default; }
    }

    void AddGifHint(string text)
    {
        var lbl = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Margin = new Padding(10, 8, 10, 8),
            Font = new Font("Segoe UI", 9f)
        };
        _grid.Controls.Add(lbl);
        _gifControls.Add(lbl);
    }

    void ClearGif()
    {
        _gifTimer.Stop();
        foreach (var ctl in _gifControls)
        {
            _grid.Controls.Remove(ctl);
            ctl.Dispose();
        }
        _gifControls.Clear();
        foreach (var img in _gifImages) img.Dispose();
        _gifImages.Clear();
        foreach (var st in _gifStreams) st.Dispose();
        _gifStreams.Clear();
    }

    // twemoji-файл эмодзи: кодпоинты в hex через '-', без FE0F; нет файла — рисуем шрифтом
    static string? EmojiImagePath(string ch)
    {
        var parts = new List<string>();
        foreach (var r in ch.EnumerateRunes())
            if (r.Value != 0xFE0F) parts.Add(r.Value.ToString("x"));
        if (parts.Count == 0) return null;
        var p = Path.Combine(EmojiDir, string.Join("-", parts) + ".png");
        return File.Exists(p) ? p : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
            _life.Dispose();
            _gifTimer.Dispose();
            _gifCts?.Dispose();
            ClearGif();
            foreach (var img in _emojiImages) img.Dispose();
            _emojiImages.Clear();
        }
        base.Dispose(disposing);
    }
}
