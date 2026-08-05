using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TaskbarProgress.Core.Services;

namespace TaskbarProgress.Presentation.Forms;

public class TrayApplication : ApplicationContext
{
    private static readonly Color SurfaceColor = Color.FromArgb(26, 26, 26); // #1A1A1A
    private static readonly Color SecondaryColor = Color.FromArgb(251, 191, 36); // #fbbf24
    private static readonly Color TextColor = Color.FromArgb(245, 245, 245);
    private static readonly Color MutedTextColor = Color.FromArgb(170, 170, 170);

    private readonly NotifyIcon _trayIcon;
    private readonly ProgressBarOrchestrator _orchestrator;
    private ToolStripMenuItem? _startStopItem;
    private ToolStripMenuItem? _statusItem;
    private bool _isRunning;

    public TrayApplication(ProgressBarOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "MetricForge",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _trayIcon.DoubleClick += (s, e) => ToggleRunning();
        Application.ApplicationExit += (s, e) =>
        {
            _orchestrator.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        };

        ToggleRunning();
    }

    private static Icon LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Presentation", "Resources", "Icons", "icon.ico");
        if (!File.Exists(path))
            return SystemIcons.Application;

        return new Icon(path, new Size(32, 32));
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = new Font("Segoe UI", 9F)
        };

        menu.Items.Add(new ToolStripLabel("MetricForge")
        {
            ForeColor = SecondaryColor,
            Font = new Font("Segoe UI Semibold", 9F)
        });
        menu.Items.Add(new ToolStripSeparator());

        _startStopItem = new ToolStripMenuItem("Pause indicators");
        _startStopItem.Click += (s, e) => ToggleRunning();
        menu.Items.Add(_startStopItem);

        _statusItem = new ToolStripMenuItem("Status: Starting")
        {
            Enabled = false
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (s, e) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Application.Exit());
        return menu;
    }

    private void ToggleRunning()
    {
        _isRunning = !_isRunning;

        if (_startStopItem != null)
            _startStopItem.Text = _isRunning ? "Pause indicators" : "Resume indicators";
        if (_statusItem != null)
            _statusItem.Text = _isRunning ? "Status: Running" : "Status: Paused";

        if (_isRunning) _orchestrator.Start();
        else _orchestrator.Stop();
    }

    private void ShowSettings()
    {
        var current = _orchestrator.CurrentConfig;
        using var form = new Form
        {
            Text = "MetricForge Settings",
            ClientSize = new Size(430, 395),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.None,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9F),
            ShowInTaskbar = false
        };
        form.Region = CreateRoundedRegion(form.ClientRectangle, 16);
        form.Resize += (s, e) => form.Region = CreateRoundedRegion(form.ClientRectangle, 16);

        var title = new Label
        {
            Text = "Taskbar indicators",
            Location = new Point(24, 20),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = SecondaryColor
        };
        var subtitle = new Label
        {
            Text = "Configure the CPU, RAM, and network indicators.",
            Location = new Point(25, 50),
            AutoSize = true,
            ForeColor = MutedTextColor
        };

        var windowClose = new Button
        {
            Text = "×",
            Location = new Point(388, 12),
            Size = new Size(28, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceColor,
            ForeColor = MutedTextColor,
            Font = new Font("Segoe UI", 12F),
            UseVisualStyleBackColor = false,
            TabStop = false
        };
        windowClose.FlatAppearance.BorderSize = 0;
        windowClose.Click += (s, e) => form.Close();

        var lblHeight = CreateLabel("Bar size:", 25, 95);
        var numHeight = CreateNumber(250, 91, 8, 15, current.BarSize, 1);

        var lblInterval = CreateLabel("Update interval (ms):", 25, 140);
        var numInterval = CreateNumber(250, 136, 100, 10000, current.UpdateIntervalMs, 100);

        var lblNetwork = CreateLabel("Network peak (Kbps):", 25, 185);
        var numNetwork = CreateNumber(250, 181, 1, 10000000,
            Math.Clamp((decimal)current.NetworkPeakKbps, 1, 10000000), 1000);

        var hint = new Label
        {
            Text = "Unit: Kbps",
            Location = new Point(250, 208),
            AutoSize = true,
            ForeColor = MutedTextColor,
            Font = new Font("Segoe UI", 8F)
        };

        var lblOpacity = CreateLabel("Bar opacity:", 25, 230);
        var opacityValue = new Label
        {
            Text = $"{current.BarOpacity}%",
            Location = new Point(350, 230),
            AutoSize = true,
            ForeColor = MutedTextColor
        };
        var opacitySlider = new TrackBar
        {
            Location = new Point(245, 250),
            Size = new Size(130, 35),
            Minimum = 10,
            Maximum = 100,
            TickFrequency = 10,
            Value = Math.Clamp(current.BarOpacity, 10, 100),
            BackColor = SurfaceColor
        };
        opacitySlider.ValueChanged += (s, e) => opacityValue.Text = $"{opacitySlider.Value}%";

        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(160, 320),
            Size = new Size(80, 34),
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        btnCancel.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
        btnCancel.Click += (s, e) => form.Close();

        var btnApply = new Button
        {
            Text = "Apply",
            Location = new Point(250, 320),
            Size = new Size(120, 34),
            BackColor = SecondaryColor,
            ForeColor = SurfaceColor,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        btnApply.FlatAppearance.BorderSize = 0;
        btnApply.Click += (s, e) =>
        {
            _orchestrator.UpdateConfig(new Core.Models.ProgressBarConfig
            {
                BarSize = (int)numHeight.Value,
                BarOpacity = opacitySlider.Value,
                UpdateIntervalMs = (int)numInterval.Value,
                NetworkPeakKbps = (double)numNetwork.Value
            });
            form.Close();
        };

        form.Controls.AddRange(new Control[]
        {
            title, subtitle, windowClose, lblHeight, numHeight, lblInterval, numInterval,
            lblNetwork, numNetwork, hint, lblOpacity, opacityValue, opacitySlider,
            btnCancel, btnApply
        });

        form.AcceptButton = btnApply;
        form.CancelButton = btnCancel;
        form.ShowDialog();
    }

    private static Label CreateLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = TextColor
    };

    private static NumericUpDown CreateNumber(int x, int y, decimal min, decimal max,
        decimal value, decimal increment) => new()
    {
        Location = new Point(x, y),
        Width = 120,
        Minimum = min,
        Maximum = max,
        Increment = increment,
        Value = Math.Clamp(value, min, max),
        BackColor = Color.FromArgb(40, 40, 40),
        ForeColor = TextColor,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Region CreateRoundedRegion(Rectangle bounds, int radius)
    {
        using var path = CreateRoundedPath(bounds, radius);
        return new Region(path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => SurfaceColor;
        public override Color MenuBorder => Color.FromArgb(70, 70, 70);
        public override Color MenuItemSelected => Color.FromArgb(55, 55, 55);
        public override Color MenuItemBorder => SecondaryColor;
        public override Color SeparatorDark => Color.FromArgb(65, 65, 65);
        public override Color SeparatorLight => SurfaceColor;
    }
}
