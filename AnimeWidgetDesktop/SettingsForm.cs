namespace AnimeWidgetDesktop;

public sealed class SettingsForm : Form
{
    private readonly TextBox _feedUrlBox = new();
    private readonly NumericUpDown _refreshMinutesBox = new();
    private readonly CheckBox _alwaysOnTopBox = new();

    public WidgetSettings Result { get; private set; }

    public SettingsForm(WidgetSettings current)
    {
        Result = new WidgetSettings
        {
            FeedUrl = current.FeedUrl,
            RefreshMinutes = current.RefreshMinutes,
            OpacityPercent = current.OpacityPercent,
            Theme = current.Theme,
            AccentColor = current.AccentColor,
            AlwaysOnTop = current.AlwaysOnTop,
            WindowX = current.WindowX,
            WindowY = current.WindowY,
            WindowWidth = current.WindowWidth,
            WindowHeight = current.WindowHeight
        };

        Text = "更新源设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Width = 640;
        Height = 300;
        BackColor = Color.FromArgb(25, 28, 40);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(16),
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(MakeLabel("更新源 URL"), 0, 0);
        _feedUrlBox.Dock = DockStyle.Fill;
        _feedUrlBox.Text = Result.FeedUrl;
        layout.Controls.Add(_feedUrlBox, 1, 0);

        var tip = new Label
        {
            Text = "支持 https 链接或本地 JSON 路径；留空则使用内置示例数据",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(160, 165, 180),
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.TopLeft
        };
        layout.Controls.Add(tip, 1, 1);

        layout.Controls.Add(MakeLabel("自动刷新(分钟)"), 0, 2);
        _refreshMinutesBox.Dock = DockStyle.Left;
        _refreshMinutesBox.Minimum = 1;
        _refreshMinutesBox.Maximum = 120;
        _refreshMinutesBox.Value = Math.Clamp(Result.RefreshMinutes, 1, 120);
        _refreshMinutesBox.Width = 110;
        layout.Controls.Add(_refreshMinutesBox, 1, 2);

        layout.Controls.Add(MakeLabel("置顶显示"), 0, 3);
        _alwaysOnTopBox.Text = "始终置顶";
        _alwaysOnTopBox.Checked = Result.AlwaysOnTop;
        _alwaysOnTopBox.AutoSize = true;
        _alwaysOnTopBox.ForeColor = ForeColor;
        layout.Controls.Add(_alwaysOnTopBox, 1, 3);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };

        var okButton = MakeButton("保存");
        okButton.Click += (_, _) => SaveAndClose(DialogResult.OK);
        var cancelButton = MakeButton("取消");
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(buttonPanel, 1, 4);
        Controls.Add(layout);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void SaveAndClose(DialogResult dialogResult)
    {
        Result.FeedUrl = _feedUrlBox.Text.Trim();
        Result.RefreshMinutes = (int)_refreshMinutesBox.Value;
        Result.AlwaysOnTop = _alwaysOnTopBox.Checked;
        DialogResult = dialogResult;
        Close();
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.Gainsboro,
        AutoSize = false
    };

    private Button MakeButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(92, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(80, 80, 100),
            ForeColor = Color.White,
            Margin = new Padding(8, 0, 0, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}
