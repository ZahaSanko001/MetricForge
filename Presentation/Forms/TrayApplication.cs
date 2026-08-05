using System.Drawing;
using System.Runtime.InteropServices;
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
            Text = "TaskbarProgress",
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
        var path = Path.Combine(AppContext.BaseDirectory, "Presentation", "Resources", "Icons", "icon.png");
        if (!File.Exists(path))
            return SystemIcons.Application;

        using var bitmap = new Bitmap(path);
        var iconHandle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(iconHandle);
            return new Icon(temporaryIcon, new Size(16, 16));
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

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

        menu.Items.Add(new ToolStripLabel("TaskbarProgress")
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
            Text = "TaskbarProgress Settings",
            ClientSize = new Size(430, 340),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9F),
            ShowInTaskbar = false
        };

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

        var lblHeight = CreateLabel("Bar thickness:", 25, 95);
        var numHeight = CreateNumber(250, 91, 1, 10, current.BarHeight, 1);

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

        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(160, 260),
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
            Location = new Point(250, 260),
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
                BarHeight = (int)numHeight.Value,
                UpdateIntervalMs = (int)numInterval.Value,
                NetworkPeakKbps = (double)numNetwork.Value
            });
            form.Close();
        };

        form.Controls.AddRange(new Control[]
        {
            title, subtitle, lblHeight, numHeight, lblInterval, numInterval,
            lblNetwork, numNetwork, hint, btnCancel, btnApply
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
