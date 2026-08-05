namespace TaskbarProgress.Infrastructure.Renderers;

using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Displays three click-through metric bars above the taskbar. Painting
/// directly into the taskbar DC is unreliable because Explorer repaints it.
/// </summary>
public sealed class DwmBarRenderer : IBarRenderer
{
    private readonly ILogger<DwmBarRenderer> _logger;
    private BarOverlay? _overlay;
    private int _barWidth = 6;
    private static readonly Color BorderColor = Color.FromArgb(251, 191, 36); // #fbbf24

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public DwmBarRenderer(ILogger<DwmBarRenderer> logger)
    {
        _logger = logger;
    }

    public void Initialize(int barHeight)
    {
        _barWidth = Math.Clamp(barHeight * 2, 4, 8);

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var rect))
        {
            _logger.LogWarning("Could not find the Windows taskbar");
            return;
        }

        const int overlayHeight = 24;
        const int overlayWidth = 36;
        var x = rect.Left + 8;
        var y = rect.Top > 0 ? rect.Top - overlayHeight - 2 : rect.Bottom + 2;
        var bounds = new Rectangle(x, y, overlayWidth, overlayHeight);

        if (_overlay == null || _overlay.IsDisposed)
        {
            _overlay = new BarOverlay();
            _overlay.Show();
        }

        _overlay.Bounds = bounds;
        _overlay._barWidth = _barWidth;
        _overlay.Invalidate();
        _logger.LogInformation("Taskbar overlay initialized at {Bounds}", bounds);
    }

    public void Render(SystemMetrics metrics, ProgressBarConfig config)
    {
        var overlay = _overlay;
        if (overlay == null || overlay.IsDisposed)
            return;

        var values = new[]
        {
            Normalize(metrics.CpuPercent),
            Normalize(metrics.MemoryPercent),
            Normalize(metrics.NetworkKbps / Math.Max(config.NetworkPeakKbps, 1) * 100.0)
        };
        var colors = new[]
        {
            GetColor(values[0], config.Colors),
            GetColor(values[1], config.Colors),
            GetColor(values[2], config.Colors)
        };

        try
        {
            overlay.BeginInvoke(() =>
            {
                overlay._values = values;
                overlay._colors = colors;
                overlay.Invalidate();
            });
        }
        catch (InvalidOperationException)
        {
            // The overlay can be closing while the render loop is stopping.
        }
    }

    public void Clear()
    {
        var overlay = _overlay;
        if (overlay == null || overlay.IsDisposed)
            return;

        try
        {
            overlay.BeginInvoke(() =>
            {
                overlay._values = new double[3];
                overlay.Invalidate();
            });
        }
        catch (InvalidOperationException)
        {
            // The overlay is already closing.
        }
    }

    public void UpdateConfiguration(ProgressBarConfig config)
    {
        Initialize(config.BarHeight);
    }

    private static double Normalize(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static Color GetColor(double value, ProgressBarColors colors) => value switch
    {
        < 50 => Color.FromArgb(colors.Low.R, colors.Low.G, colors.Low.B),
        < 80 => Color.FromArgb(colors.Medium.R, colors.Medium.G, colors.Medium.B),
        _ => Color.FromArgb(colors.High.R, colors.High.G, colors.High.B)
    };

    private sealed class BarOverlay : Form
    {
        internal double[] _values = new double[3];
        internal Color[] _colors = { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen };
        internal int _barWidth = 6;

        public BarOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(TransparencyKey);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var barGap = 4;
            var barHeight = ClientSize.Height - 2;
            for (var i = 0; i < 3; i++)
            {
                var x = i * (_barWidth + barGap);
                var filledHeight = (int)(barHeight * (_values[i] / 100.0));
                var y = ClientSize.Height - filledHeight - 1;

                using var brush = new SolidBrush(_colors[i]);
                if (filledHeight > 0)
                    e.Graphics.FillRectangle(brush, x + 1, y, Math.Max(1, _barWidth - 1), filledHeight);

                using var border = new Pen(BorderColor, 1);
                e.Graphics.DrawRectangle(border, x, 1, _barWidth - 1, barHeight - 1);
            }
        }
    }
}
