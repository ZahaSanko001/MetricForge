using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using TaskbarProgress.Core.Models;
using TaskbarProgress.Core.Services;
using TaskbarProgress.Infrastructure.Renderers; // ThemePreference

namespace TaskbarProgress.Presentation.Forms;

public class TrayApplication : ApplicationContext
{
    private static readonly Color SurfaceColor = Color.FromArgb(27, 27, 27); // #1B1B1B
    private static readonly Color SecondaryColor = Color.FromArgb(186, 236, 23); // #BAEC17
    private static readonly Color SecondaryHoverColor = Color.FromArgb(205, 245, 72);
    private static readonly Color DangerColor = Color.FromArgb(233, 77, 12); // #E94D0C
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
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MetricForge.Icon.ico");
        if (stream == null)
            return SystemIcons.Application;

        using (stream)
            return new Icon(stream, new Size(32, 32));
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
        var exitItem = new ToolStripMenuItem("Exit") { ForeColor = DangerColor };
        exitItem.Click += (s, e) => Application.Exit();
        menu.Items.Add(exitItem);
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
        using var form = new SettingsForm
        {
            Text = "MetricForge Settings",
            ClientSize = new Size(430, 540),
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = SurfaceColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9F),
            ShowInTaskbar = false
        };

        PositionAsFlyout(form);

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

        // Borderless forms have no title bar to drag from, so the form
        // itself and the header text act as the drag handle instead.
        form.EnableDragFrom(form);
        form.EnableDragFrom(title);
        form.EnableDragFrom(subtitle);

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
        windowClose.FlatAppearance.MouseOverBackColor = DangerColor;
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
            AutoSize = false,
            Minimum = 10,
            Maximum = 100,
            TickFrequency = 10,
            Value = Math.Clamp(current.BarOpacity, 10, 100),
            BackColor = SurfaceColor
        };
        opacitySlider.ValueChanged += (s, e) => opacityValue.Text = $"{opacitySlider.Value}%";

        var chkShowLabels = new CheckBox
        {
            Text = "Show CPU / RAM / NET labels",
            Location = new Point(25, 350),
            AutoSize = true,
            ForeColor = TextColor,
            Checked = current.ShowLabels
        };

        var chkShowValues = new CheckBox
        {
            Text = "Show percentages",
            Location = new Point(25, 375),
            AutoSize = true,
            ForeColor = TextColor,
            Checked = current.ShowValues
        };

        var lblColors = CreateLabel("Threshold colors:", 25, 412);
        var swatchLow = CreateColorSwatch(200, 406, ToColor(current.Colors.Low));
        var swatchMedium = CreateColorSwatch(250, 406, ToColor(current.Colors.Medium));
        var swatchHigh = CreateColorSwatch(300, 406, ToColor(current.Colors.High));
        var capLow = CreateCaption("Low", swatchLow.Location.X, 432, swatchLow.Width);
        var capMedium = CreateCaption("Med", swatchMedium.Location.X, 432, swatchMedium.Width);
        var capHigh = CreateCaption("High", swatchHigh.Location.X, 432, swatchHigh.Width);

        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(160, 470),
            Size = new Size(80, 34),
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        btnCancel.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
        btnCancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 70);
        btnCancel.Click += (s, e) => form.Close();

        var btnApply = new Button
        {
            Text = "Apply",
            Location = new Point(250, 470),
            Size = new Size(120, 34),
            BackColor = SecondaryColor,
            ForeColor = SurfaceColor,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        btnApply.FlatAppearance.BorderSize = 0;
        btnApply.FlatAppearance.MouseOverBackColor = SecondaryHoverColor;
        btnApply.Click += (s, e) =>
        {
            _orchestrator.UpdateConfig(new Core.Models.ProgressBarConfig
            {
                BarSize = (int)numHeight.Value,
                BarOpacity = opacitySlider.Value,
                UpdateIntervalMs = (int)numInterval.Value,
                NetworkPeakKbps = (double)numNetwork.Value,
                ThemeOverride = ThemePreference.Dark,
                ShowLabels = chkShowLabels.Checked,
                ShowValues = chkShowValues.Checked,
                Colors = new Core.Models.ProgressBarColors
                {
                    Low = ToRgb(swatchLow.BackColor),
                    Medium = ToRgb(swatchMedium.BackColor),
                    High = ToRgb(swatchHigh.BackColor)
                }
            });
            form.Close();
        };

        form.Controls.AddRange(new Control[]
        {
            title, subtitle, windowClose, lblHeight, numHeight, lblInterval, numInterval,
            lblNetwork, numNetwork, hint, lblOpacity, opacityValue, opacitySlider,
            chkShowLabels, chkShowValues,
            lblColors, swatchLow, swatchMedium, swatchHigh, capLow, capMedium, capHigh,
            btnCancel, btnApply
        });

        form.AcceptButton = btnApply;
        form.CancelButton = btnCancel;
        form.ShowDialog();
    }

    private static Color ToColor((byte R, byte G, byte B) color) =>
        Color.FromArgb(color.R, color.G, color.B);

    private static (byte R, byte G, byte B) ToRgb(Color color) =>
        ((byte)color.R, (byte)color.G, (byte)color.B);

    private static Label CreateLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = TextColor
    };

    private static Label CreateCaption(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 14),
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = MutedTextColor,
        Font = new Font("Segoe UI", 7.5F)
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

    /// <summary>
    /// A clickable color swatch used for the threshold-color pickers.
    /// Opens the stock ColorDialog and updates its own background on pick,
    /// so the swatch itself doubles as the "current value" display.
    /// </summary>
    private static Panel CreateColorSwatch(int x, int y, Color initial)
    {
        var swatch = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(36, 24),
            BackColor = initial,
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand
        };

        swatch.Click += (s, e) =>
        {
            using var dialog = new ColorDialog
            {
                Color = swatch.BackColor,
                AllowFullOpen = true,
                FullOpen = false
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                swatch.BackColor = dialog.Color;
        };

        return swatch;
    }

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

    /// <summary>
    /// Plain Form doesn't give a borderless window a drag handle or a drop
    /// shadow. CS_DROPSHADOW is a native window-class style (cheap, GPU-
    /// composited by DWM — no per-frame drawing cost), and EnableDragFrom
    /// wires up manual click-and-drag since there's no title bar to grab.
    /// </summary>
    private sealed class SettingsForm : Form
    {
        private const int CS_DROPSHADOW = 0x00020000;

        private Point _dragAnchor;
        private bool _dragging;

        public SettingsForm()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        public void EnableDragFrom(Control control)
        {
            control.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragging = true;
                _dragAnchor = e.Location;
            };
            control.MouseMove += (s, e) =>
            {
                if (!_dragging) return;
                Location += new Size(e.Location.X - _dragAnchor.X, e.Location.Y - _dragAnchor.Y);
            };
            control.MouseUp += (s, e) => _dragging = false;
        }
    }

    /// <summary>
    /// Anchors the settings window to the bottom-right corner of the working
    /// area, just above the taskbar — the same corner Windows' own flyouts
    /// (volume, network, notification center) use. Uses Screen.FromPoint on
    /// the cursor rather than Screen.PrimaryScreen so it lands on whichever
    /// monitor the tray icon was actually clicked from in a multi-monitor
    /// setup, since there's no public API to get a specific NotifyIcon's
    /// exact screen rect.
    /// </summary>
    private static void PositionAsFlyout(Form form)
    {
        const int margin = 12;

        var screen = Screen.FromPoint(Cursor.Position);
        var workingArea = screen.WorkingArea;

        var x = workingArea.Right - form.Width - margin;
        var y = workingArea.Bottom - form.Height - margin;

        // Guard against a working area shorter than the form (small/scaled
        // displays) rather than letting it render off the top edge.
        y = Math.Max(workingArea.Top + margin, y);

        form.Location = new Point(x, y);
    }
}
