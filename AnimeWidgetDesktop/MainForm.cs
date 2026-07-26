using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AnimeWidgetDesktop;

public sealed class MainForm : Form
{
    private readonly FeedService _feedService = new();
    private WidgetSettings _settings;
    private AnimeFeed _currentFeed = new();
    private CancellationTokenSource? _loadCts;

    private readonly Panel _header = new();
    private readonly Panel _content = new();
    private readonly FlowLayoutPanel _listPanel = new();
    private readonly Label _titleLabel = new();
    private readonly Label _updatedLabel = new();
    private readonly Label _statusLabel = new();
    private readonly TrackBar _opacityTrack = new();
    private readonly FlowLayoutPanel _accentPanel = new();
    private readonly FlowLayoutPanel _headerButtons = new();
    private readonly System.Windows.Forms.Timer _autoRefreshTimer = new();
    private readonly Button _closeButton = new();

    private readonly Color _darkBack = Color.FromArgb(16, 18, 27);
    private readonly Color _lightBack = Color.FromArgb(245, 247, 250);
    private readonly Color _darkPanel = Color.FromArgb(28, 32, 48);
    private readonly Color _lightPanel = Color.FromArgb(255, 255, 255);

    public MainForm()
    {
        _settings = WidgetSettings.Load();

        Text = "Anime Widget";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = _darkBack;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(360, 520);
        ClientSize = new Size(_settings.WindowWidth, _settings.WindowHeight);
        Location = new Point(_settings.WindowX, _settings.WindowY);
        TopMost = _settings.AlwaysOnTop;
        DoubleBuffered = true;
        ShowInTaskbar = true;

        BuildUi();
        ApplyTheme();
        ApplyVisualSettings();
        ApplyRoundedCorners();

        _autoRefreshTimer.Tick += async (_, _) => await LoadFeedAsync(silent: true);
        _autoRefreshTimer.Interval = Math.Max(1, _settings.RefreshMinutes) * 60 * 1000;
        _autoRefreshTimer.Start();

        Shown += async (_, _) => await LoadFeedAsync(silent: false);
        Resize += OnFormResize;
        Move += (_, _) => SaveWindowBounds();
        FormClosing += (_, _) =>
        {
            _loadCts?.Cancel();
            _settings.Save();
        };
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        SaveWindowBounds();
        ApplyRoundedCorners();
        AdjustCardWidths();
    }

    private void ApplyRoundedCorners()
    {
        const int radius = 16;
        var path = new GraphicsPath();
        var rect = ClientRectangle;
        if (rect.Width < 2 || rect.Height < 2) return;

        path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    private void BuildUi()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 88;
        _header.Padding = new Padding(14, 10, 10, 10);
        _header.BackColor = Color.Transparent;
        _header.MouseDown += BeginDrag;
        Controls.Add(_header);

        _closeButton.Text = "✕";
        _closeButton.Width = 32;
        _closeButton.Height = 28;
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.Cursor = Cursors.Hand;
        _closeButton.Dock = DockStyle.Right;
        _closeButton.Click += (_, _) => Close();
        _header.Controls.Add(_closeButton);

        var headerText = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        headerText.MouseDown += BeginDrag;
        _header.Controls.Add(headerText);

        _titleLabel.Text = "本周放送列表";
        _titleLabel.Dock = DockStyle.Top;
        _titleLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
        _titleLabel.Height = 28;
        _titleLabel.MouseDown += BeginDrag;
        headerText.Controls.Add(_titleLabel);

        _updatedLabel.Text = "正在加载最新番剧更新…";
        _updatedLabel.Dock = DockStyle.Top;
        _updatedLabel.Height = 22;
        _updatedLabel.Padding = new Padding(2, 4, 0, 0);
        _updatedLabel.ForeColor = Color.FromArgb(220, 220, 220);
        _updatedLabel.MouseDown += BeginDrag;
        headerText.Controls.Add(_updatedLabel);

        _headerButtons.Dock = DockStyle.Right;
        _headerButtons.Width = 200;
        _headerButtons.FlowDirection = FlowDirection.TopDown;
        _headerButtons.WrapContents = false;
        _headerButtons.Padding = new Padding(0, 0, 4, 0);
        _headerButtons.BackColor = Color.Transparent;
        _headerButtons.MouseDown += BeginDrag;
        _header.Controls.Add(_headerButtons);

        _headerButtons.Controls.Add(CreateHeaderButton("↻ 刷新", (_, _) => _ = LoadFeedAsync(false)));
        _headerButtons.Controls.Add(CreateHeaderButton("◐ 主题", (_, _) => ToggleTheme()));
        _headerButtons.Controls.Add(CreateHeaderButton("⚙ 设置", (_, _) => OpenSettings()));

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(14, 0, 14, 12);
        _content.BackColor = Color.Transparent;
        Controls.Add(_content);

        var controlsRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = 108,
            BackColor = Color.Transparent
        };
        _content.Controls.Add(controlsRow);

        var opacityGroup = MakeGroup("透明度");
        opacityGroup.Width = 200;
        opacityGroup.Height = 92;
        opacityGroup.Location = new Point(0, 8);
        controlsRow.Controls.Add(opacityGroup);

        _opacityTrack.Dock = DockStyle.Fill;
        _opacityTrack.Minimum = 55;
        _opacityTrack.Maximum = 100;
        _opacityTrack.TickFrequency = 5;
        _opacityTrack.Value = Math.Clamp(_settings.OpacityPercent, 55, 100);
        _opacityTrack.Scroll += (_, _) =>
        {
            _settings.OpacityPercent = _opacityTrack.Value;
            ApplyVisualSettings();
            _settings.Save();
        };
        opacityGroup.Controls.Add(_opacityTrack);

        var accentGroup = MakeGroup("强调色");
        accentGroup.Width = 180;
        accentGroup.Height = 92;
        accentGroup.Location = new Point(212, 8);
        controlsRow.Controls.Add(accentGroup);

        _accentPanel.Dock = DockStyle.Fill;
        _accentPanel.FlowDirection = FlowDirection.LeftToRight;
        _accentPanel.WrapContents = true;
        _accentPanel.Padding = new Padding(0, 4, 0, 0);
        _accentPanel.BackColor = Color.Transparent;
        accentGroup.Controls.Add(_accentPanel);

        foreach (var (name, hex) in new[]
        {
            ("紫", "#8b5cf6"),
            ("青", "#06b6d4"),
            ("绿", "#22c55e"),
            ("粉", "#fb7185"),
            ("橙", "#f59e0b")
        })
        {
            var button = new Button
            {
                Text = name,
                Width = 30,
                Height = 26,
                Margin = new Padding(0, 0, 6, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml(hex),
                ForeColor = Color.White,
                Tag = hex,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) =>
            {
                _settings.AccentColor = hex;
                ApplyVisualSettings();
                ApplyTheme();
                _settings.Save();
            };
            _accentPanel.Controls.Add(button);
        }

        _listPanel.Dock = DockStyle.Fill;
        _listPanel.FlowDirection = FlowDirection.TopDown;
        _listPanel.WrapContents = false;
        _listPanel.AutoScroll = true;
        _listPanel.Padding = new Padding(0, 8, 0, 0);
        _listPanel.BackColor = Color.Transparent;
        _content.Controls.Add(_listPanel);

        _statusLabel.Dock = DockStyle.Bottom;
        _statusLabel.Height = 22;
        _statusLabel.Padding = new Padding(2, 2, 0, 0);
        _statusLabel.ForeColor = Color.FromArgb(210, 210, 210);
        _statusLabel.Text = "就绪";
        _content.Controls.Add(_statusLabel);
    }

    private Panel MakeGroup(string title)
    {
        var panel = new Panel
        {
            BackColor = Color.Transparent,
            BorderStyle = BorderStyle.FixedSingle
        };

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Color.FromArgb(230, 230, 230),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Padding = new Padding(4, 2, 0, 0)
        };
        panel.Controls.Add(titleLabel);
        return panel;
    }

    private Button CreateHeaderButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            Width = 88,
            Height = 28,
            Margin = new Padding(0, 0, 0, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 255, 255, 255),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += handler;
        return button;
    }

    private async Task LoadFeedAsync(bool silent)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        try
        {
            if (!silent)
            {
                _statusLabel.Text = "正在刷新最新更新…";
            }

            var feed = await _feedService.LoadAsync(_settings.FeedUrl, _loadCts.Token);
            _currentFeed = feed;
            RenderFeed(feed);

            var title = string.IsNullOrWhiteSpace(feed.Title) ? "本周放送列表" : feed.Title;
            _titleLabel.Text = title;
            _updatedLabel.Text = feed.UpdatedAt is null
                ? $"共 {feed.Items.Count} 条更新"
                : $"共 {feed.Items.Count} 条更新 · 最近同步 {feed.UpdatedAt:yyyy-MM-dd HH:mm}";
            _statusLabel.Text = string.IsNullOrWhiteSpace(_settings.FeedUrl)
                ? "使用示例数据"
                : TruncateUrl(_settings.FeedUrl);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"加载失败，已切换到示例数据：{ex.Message}";
            var demo = FeedService.BuildDemoFeed();
            _currentFeed = demo;
            RenderFeed(demo);
        }
    }

    private static string TruncateUrl(string url)
    {
        if (url.Length <= 48) return $"来源：{url}";
        return $"来源：{url[..22]}…{url[^18..]}";
    }

    private void RenderFeed(AnimeFeed feed)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => RenderFeed(feed)));
            return;
        }

        _listPanel.SuspendLayout();
        _listPanel.Controls.Clear();

        foreach (var item in feed.Items)
        {
            _listPanel.Controls.Add(CreateItemCard(item));
        }

        _listPanel.ResumeLayout();
        ApplyTheme();
        ApplyVisualSettings();
    }

    private void AdjustCardWidths()
    {
        var w = Math.Max(280, ClientSize.Width - 44);
        foreach (Control c in _listPanel.Controls)
        {
            c.Width = w;
        }
    }

    private Control CreateItemCard(AnimeFeedItem item)
    {
        var accent = ColorTranslator.FromHtml(_settings.AccentColor);
        var card = new Panel
        {
            Width = Math.Max(280, ClientSize.Width - 44),
            Height = 76,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(14, 10, 12, 10),
            Cursor = Cursors.Hand,
            Tag = item
        };

        var titleLabel = new Label
        {
            Text = item.Title,
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            AutoEllipsis = true,
            Cursor = Cursors.Hand
        };

        var metaLabel = new Label
        {
            Text = BuildMetaText(item),
            Dock = DockStyle.Bottom,
            Height = 20,
            ForeColor = Color.FromArgb(180, 185, 200),
            Cursor = Cursors.Hand
        };

        var actionLabel = new Label
        {
            Text = "观看 ▶",
            Dock = DockStyle.Right,
            Width = 64,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = accent,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9f)
        };

        void open() => OpenUrl(item.WatchUrl, item.Title);
        card.Click += (_, _) => open();
        titleLabel.Click += (_, _) => open();
        metaLabel.Click += (_, _) => open();
        actionLabel.Click += (_, _) => open();

        card.Controls.Add(actionLabel);
        card.Controls.Add(metaLabel);
        card.Controls.Add(titleLabel);

        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = card.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            using var borderPen = new Pen(Color.FromArgb(50, 255, 255, 255));
            e.Graphics.DrawRectangle(borderPen, rect);

            using var accentBrush = new SolidBrush(accent);
            e.Graphics.FillRectangle(accentBrush, 0, 0, 4, card.Height);
        };

        return card;
    }

    private static string BuildMetaText(AnimeFeedItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Badge)) parts.Add(item.Badge);
        if (!string.IsNullOrWhiteSpace(item.Platform)) parts.Add(item.Platform);
        if (!string.IsNullOrWhiteSpace(item.Episode)) parts.Add(item.Episode);
        if (!string.IsNullOrWhiteSpace(item.Time)) parts.Add(item.Time);
        if (!string.IsNullOrWhiteSpace(item.Notes)) parts.Add(item.Notes);
        return string.Join(" · ", parts);
    }

    private static void OpenUrl(string? url, string title)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show($"{title}\n\n这个条目还没有配置播放地址。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法打开链接", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleTheme()
    {
        _settings.Theme = _settings.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        ApplyTheme();
        ApplyVisualSettings();
        _settings.Save();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _settings = form.Result;
            ApplyTheme();
            ApplyVisualSettings();
            _autoRefreshTimer.Interval = Math.Max(1, _settings.RefreshMinutes) * 60 * 1000;
            TopMost = _settings.AlwaysOnTop;
            _settings.Save();
            _ = LoadFeedAsync(false);
        }
    }

    private void ApplyTheme()
    {
        var dark = _settings.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase);
        var back = dark ? _darkBack : _lightBack;
        var panel = dark ? _darkPanel : _lightPanel;
        var text = dark ? Color.White : Color.FromArgb(25, 30, 42);
        var muted = dark ? Color.FromArgb(200, 205, 220) : Color.FromArgb(70, 80, 95);

        BackColor = back;
        ForeColor = text;
        _header.BackColor = back;
        _content.BackColor = back;
        _titleLabel.ForeColor = text;
        _updatedLabel.ForeColor = muted;
        _statusLabel.ForeColor = muted;
        _listPanel.BackColor = back;
        _opacityTrack.BackColor = back;

        _closeButton.BackColor = back;
        _closeButton.ForeColor = muted;

        foreach (Control button in _headerButtons.Controls)
        {
            button.BackColor = panel;
            button.ForeColor = text;
        }

        foreach (Control group in _content.Controls.OfType<Panel>())
        {
            group.BackColor = back;
            foreach (Control child in group.Controls.OfType<Label>())
            {
                child.ForeColor = muted;
            }
        }

        foreach (Control card in _listPanel.Controls)
        {
            card.BackColor = panel;
            foreach (Control child in card.Controls)
            {
                child.BackColor = panel;
                if (child is Label lbl && lbl.Text == "观看 ▶")
                {
                    lbl.ForeColor = ColorTranslator.FromHtml(_settings.AccentColor);
                }
                else
                {
                    child.ForeColor = text;
                }
            }
        }

        foreach (Button btn in _accentPanel.Controls.OfType<Button>())
        {
            btn.ForeColor = Color.White;
        }
    }

    private void ApplyVisualSettings()
    {
        Opacity = Math.Clamp(_settings.OpacityPercent, 55, 100) / 100.0;
        var accent = ColorTranslator.FromHtml(_settings.AccentColor);

        _titleLabel.ForeColor = accent;

        foreach (Button btn in _accentPanel.Controls.OfType<Button>())
        {
            var selected = string.Equals((string?)btn.Tag, _settings.AccentColor, StringComparison.OrdinalIgnoreCase);
            btn.FlatAppearance.BorderSize = selected ? 2 : 0;
            btn.FlatAppearance.BorderColor = Color.White;
        }

        _opacityTrack.Value = Math.Clamp(_settings.OpacityPercent, 55, 100);
        _autoRefreshTimer.Interval = Math.Max(1, _settings.RefreshMinutes) * 60 * 1000;

        foreach (Control card in _listPanel.Controls)
        {
            card.Invalidate();
        }

        Invalidate(true);
    }

    private void SaveWindowBounds()
    {
        if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowX = Left;
            _settings.WindowY = Top;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.Save();
        }
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
}
