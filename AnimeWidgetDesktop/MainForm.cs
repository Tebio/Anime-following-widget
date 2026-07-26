using System.Diagnostics;
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
    private readonly Button _refreshButton = new();
    private readonly Button _themeButton = new();
    private readonly Button _settingsButton = new();
    private readonly FlowLayoutPanel _accentPanel = new();
    private readonly Timer _autoRefreshTimer = new();

    private readonly Color _darkBack = Color.FromArgb(16, 18, 27);
    private readonly Color _lightBack = Color.FromArgb(245, 247, 250);
    private readonly Color _darkPanel = Color.FromArgb(255, 255, 255, 18);
    private readonly Color _lightPanel = Color.FromArgb(20, 25, 40, 10);

    public MainForm()
    {
        _settings = WidgetSettings.Load();

        Text = "Anime Widget";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = _darkBack;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(360, 540);
        ClientSize = new Size(_settings.WindowWidth, _settings.WindowHeight);
        Location = new Point(_settings.WindowX, _settings.WindowY);
        TopMost = _settings.AlwaysOnTop;
        DoubleBuffered = true;

        BuildUi();
        ApplyTheme();
        ApplyVisualSettings();

        _autoRefreshTimer.Tick += async (_, _) => await LoadFeedAsync(silent: true);
        _autoRefreshTimer.Interval = Math.Max(1, _settings.RefreshMinutes) * 60 * 1000;
        _autoRefreshTimer.Start();

        Shown += async (_, _) => await LoadFeedAsync(silent: false);
        Resize += (_, _) => SaveWindowBounds();
        Move += (_, _) => SaveWindowBounds();
        FormClosing += (_, _) => _settings.Save();
    }

    private void BuildUi()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 92;
        _header.Padding = new Padding(14, 10, 14, 10);
        _header.BackColor = Color.Transparent;
        _header.MouseDown += BeginDrag;
        Controls.Add(_header);

        var headerText = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        headerText.MouseDown += BeginDrag;
        _header.Controls.Add(headerText);

        _titleLabel.Text = "本周放送列表";
        _titleLabel.Dock = DockStyle.Top;
        _titleLabel.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
        _titleLabel.Height = 30;
        _titleLabel.MouseDown += BeginDrag;
        headerText.Controls.Add(_titleLabel);

        _updatedLabel.Text = "正在加载最新番剧更新…";
        _updatedLabel.Dock = DockStyle.Top;
        _updatedLabel.Height = 22;
        _updatedLabel.Padding = new Padding(2, 4, 0, 0);
        _updatedLabel.ForeColor = Color.FromArgb(220, 220, 220);
        _updatedLabel.MouseDown += BeginDrag;
        headerText.Controls.Add(_updatedLabel);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 210,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 0),
            BackColor = Color.Transparent
        };
        buttons.MouseDown += BeginDrag;
        _header.Controls.Add(buttons);

        buttons.Controls.Add(CreateHeaderButton("↻ 刷新", (_, _) => _ = LoadFeedAsync(false)));
        buttons.Controls.Add(CreateHeaderButton("◐ 主题", (_, _) => ToggleTheme()));
        buttons.Controls.Add(CreateHeaderButton("⚙ 设置", (_, _) => OpenSettings()));

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(14, 0, 14, 14);
        _content.BackColor = Color.Transparent;
        Controls.Add(_content);

        var controlsRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = 118,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        _content.Controls.Add(controlsRow);

        var opacityGroup = MakeGroup("透明度");
        opacityGroup.Width = 210;
        opacityGroup.Height = 100;
        opacityGroup.Location = new Point(0, 10);
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
            SaveWindowBounds();
        };
        opacityGroup.Controls.Add(_opacityTrack);

        var accentGroup = MakeGroup("强调色");
        accentGroup.Width = 186;
        accentGroup.Height = 100;
        accentGroup.Location = new Point(222, 10);
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
                Width = 32,
                Height = 28,
                Margin = new Padding(0, 0, 8, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml(hex),
                ForeColor = Color.White,
                Tag = hex
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) =>
            {
                _settings.AccentColor = hex;
                ApplyVisualSettings();
                _settings.Save();
            };
            _accentPanel.Controls.Add(button);
        }

        _listPanel.Dock = DockStyle.Fill;
        _listPanel.FlowDirection = FlowDirection.TopDown;
        _listPanel.WrapContents = false;
        _listPanel.AutoScroll = true;
        _listPanel.Padding = new Padding(0, 10, 0, 0);
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
            Height = 22,
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
            Width = 92,
            Height = 30,
            Margin = new Padding(0, 0, 0, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 255, 255, 28),
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
                : $"来源：{_settings.FeedUrl}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"加载失败，已切换到示例数据：{ex.Message}";
            var demo = await _feedService.LoadAsync(null, CancellationToken.None);
            _currentFeed = demo;
            RenderFeed(demo);
        }
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
    }

    private Control CreateItemCard(AnimeFeedItem item)
    {
        var card = new Panel
        {
            Width = ClientSize.Width - 44,
            Height = 78,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 10, 12, 10),
            Cursor = Cursors.Hand
        };

        var titleLabel = new Label
        {
            Text = item.Title,
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI Semibold", 10.8f, FontStyle.Bold),
            AutoEllipsis = true,
            Cursor = Cursors.Hand
        };

        var metaLabel = new Label
        {
            Text = BuildMetaText(item),
            Dock = DockStyle.Bottom,
            Height = 20,
            ForeColor = Color.FromArgb(215, 215, 215),
            Cursor = Cursors.Hand
        };

        var actionLabel = new Label
        {
            Text = "点击直接观看 ▶",
            Dock = DockStyle.Right,
            Width = 120,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };

        card.Controls.Add(actionLabel);
        card.Controls.Add(metaLabel);
        card.Controls.Add(titleLabel);
        card.Click += (_, _) => OpenUrl(item.WatchUrl, item.Title);
        titleLabel.Click += (_, _) => OpenUrl(item.WatchUrl, item.Title);
        metaLabel.Click += (_, _) => OpenUrl(item.WatchUrl, item.Title);
        actionLabel.Click += (_, _) => OpenUrl(item.WatchUrl, item.Title);

        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(55, 255, 255, 255));
            var rect = card.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
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
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
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
        var muted = dark ? Color.FromArgb(220, 220, 220) : Color.FromArgb(70, 80, 95);

        BackColor = back;
        ForeColor = text;
        _header.BackColor = back;
        _content.BackColor = back;
        _titleLabel.ForeColor = text;
        _updatedLabel.ForeColor = muted;
        _statusLabel.ForeColor = muted;

        foreach (Control control in Controls)
        {
            control.BackColor = control == _header || control == _content ? back : control.BackColor;
        }

        var headerButtons = _header.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (headerButtons is not null)
        {
            foreach (Control button in headerButtons.Controls)
            {
                button.BackColor = panel;
                button.ForeColor = text;
            }
        }

        _listPanel.BackColor = back;
        _opacityTrack.BackColor = back;

        foreach (Control group in _content.Controls.OfType<Panel>())
        {
            group.BackColor = back;
        }

        foreach (Control card in _listPanel.Controls)
        {
            card.BackColor = panel;
            foreach (Control child in card.Controls)
            {
                child.BackColor = panel;
                child.ForeColor = text;
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
        _settingsButton.ForeColor = Color.White;

        foreach (Button btn in _accentPanel.Controls.OfType<Button>())
        {
            btn.FlatAppearance.BorderSize = string.Equals((string?)btn.Tag, _settings.AccentColor, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            btn.FlatAppearance.BorderColor = Color.White;
        }

        _opacityTrack.Value = Math.Clamp(_settings.OpacityPercent, 55, 100);
        _autoRefreshTimer.Interval = Math.Max(1, _settings.RefreshMinutes) * 60 * 1000;
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
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

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
