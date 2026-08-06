namespace TaskbarProgress.Infrastructure.Renderers;

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Displays CPU and RAM as horizontal bars, and network as a formatted
/// speed readout (Kbps/Mbps/Gbps) instead of a bar — a percentage-of-peak
/// bar is inherently arbitrary for network throughput since there's no
/// natural "100%" the way there is for CPU/RAM. Docked directly over the
/// taskbar strip; re-asserts HWND_TOPMOST against Explorer as before.
/// </summary>
public sealed class DwmBarRenderer : IBarRenderer
{
    private readonly ILogger<DwmBarRenderer> _logger;
    private BarOverlay? _overlay;
    private int _barThickness = 10;

    public DwmBarRenderer(ILogger<DwmBarRenderer> logger)
    {
        _logger = logger;
    }

    public void Initialize(int barSize)
    {
        _barThickness = Math.Clamp(barSize, 8, 15);

        if (_overlay == null || _overlay.IsDisposed)
        {
            _overlay = new BarOverlay();
            _overlay.Show();
        }

        _overlay.SetBarThickness(_barThickness);
        _overlay.RepositionOverTaskbar();
        _overlay.Redraw();
        _logger.LogInformation("Taskbar overlay initialized (requested bar thickness {BarThickness})", _barThickness);
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
            // Still computed for the network row — no longer drives a bar's
            // fill height, but still picks the speed text's color so it
            // still flashes toward red as usage nears the configured peak.
            Normalize(metrics.NetworkKbps / Math.Max(config.NetworkPeakKbps, 1) * 100.0)
        };
        var colors = new[]
        {
            GetColor(values[0], config.Colors),
            GetColor(values[1], config.Colors),
            GetColor(values[2], config.Colors)
        };
        var opacity = Math.Clamp(config.BarOpacity, 10, 100) / 100.0;
        var themeOverride = config.ThemeOverride;
        var showLabels = config.ShowLabels;
        var showValues = config.ShowValues;
        var networkKbps = Math.Max(metrics.NetworkKbps, 0);
        var networkDownloadKbps = Math.Max(metrics.NetworkDownloadKbps, 0);
        var networkUploadKbps = Math.Max(metrics.NetworkUploadKbps, 0);

        try
        {
            overlay.BeginInvoke(() =>
                overlay.SetTargets(values, colors, opacity, themeOverride, showLabels, showValues,
                    networkKbps, networkDownloadKbps, networkUploadKbps));
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
            overlay.BeginInvoke(() => overlay.SetTargets(
                new double[3], overlay.Colors, overlay.OpacityLevel, overlay.Theme,
                overlay.LabelsVisible, overlay.ValuesVisible, 0, 0, 0));
        }
        catch (InvalidOperationException)
        {
            // The overlay is already closing.
        }
    }

    public void UpdateConfiguration(ProgressBarConfig config)
    {
        Initialize(config.BarSize);
    }

    private static double Normalize(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static Color GetColor(double value, ProgressBarColors colors) => value switch
    {
        < 50 => Color.FromArgb(255, colors.Low.R, colors.Low.G, colors.Low.B),
        < 80 => Color.FromArgb(255, colors.Medium.R, colors.Medium.G, colors.Medium.B),
        _ => Color.FromArgb(255, colors.High.R, colors.High.G, colors.High.B)
    };

    private sealed class BarOverlay : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ABS_AUTOHIDE = 0x0000001;

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int LeftOffset = 12;
        private const int TaskbarVerticalMargin = 4;

        private const int OverlayWidth = 232;
        private const int LayoutPadding = 5;
        private const int CardGap = 4;
        // Kept for the legacy drawing branch below; the compact layout uses cards instead.
        private const int LabelWidth = 30;
        private const int BarLength = 90;
        private const int ValueWidth = 32;
        private const int SectionGap = 6;
        private const int RowGap = 3;
        private const int MinBarThickness = 6;
        private const int NetworkCardWidth = 88;
        private const int NetworkRowIndex = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        private int _requestedBarThickness = 10;
        private int _effectiveBarThickness = 10;
        private double[] _targets = new double[3];
        private double[] _values = new double[3];
        private Color[] _colors = { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen };
        private double _opacity = 1.0;
        private ThemePreference _theme = ThemePreference.Auto;
        private bool _showLabels = true;
        private bool _showValues = true;
        private double _networkTarget;
        private double _networkDisplay; // eased toward _networkTarget, same as the bars
        private double _networkDownloadTarget;
        private double _networkDownloadDisplay;
        private double _networkUploadTarget;
        private double _networkUploadDisplay;
        private readonly string[] _labels = { "CPU", "RAM", "NET" };
        private readonly System.Windows.Forms.Timer _animTimer;
        private readonly System.Windows.Forms.Timer _watchdogTimer;
        private bool _hiddenForAutoHide;

        public Color[] Colors => _colors;
        public double OpacityLevel => _opacity;
        public ThemePreference Theme => _theme;
        public bool LabelsVisible => _showLabels;
        public bool ValuesVisible => _showValues;

        public BarOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, _) => AnimationTick();
            _animTimer.Start();

            _watchdogTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _watchdogTimer.Tick += (_, _) => RepositionOverTaskbar();
            _watchdogTimer.Start();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                cp.ExStyle |= WS_EX_TRANSPARENT;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void SetBarThickness(int thickness) => _requestedBarThickness = thickness;

        public void SetTargets(double[] values, Color[] colors, double opacity,
            ThemePreference theme, bool showLabels, bool showValues, double networkKbps,
            double networkDownloadKbps, double networkUploadKbps)
        {
            _targets = values;
            _colors = colors;
            _opacity = opacity;
            _theme = theme;
            _showLabels = showLabels;
            _showValues = showValues;
            _networkTarget = networkKbps;
            _networkDownloadTarget = networkDownloadKbps;
            _networkUploadTarget = networkUploadKbps;
        }

        private static int HeightFor(int barThickness) =>
            Math.Max(26, LayoutPadding * 2 + barThickness + 18);

        public void RepositionOverTaskbar()
        {
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd == IntPtr.Zero || !GetWindowRect(taskbarHwnd, out var taskbarRect))
                return;

            if (IsTaskbarAutoHiddenAndCollapsed())
            {
                if (Visible) Hide();
                _hiddenForAutoHide = true;
                return;
            }

            if (_hiddenForAutoHide)
            {
                Show();
                _hiddenForAutoHide = false;
            }

            var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            var availableHeight = taskbarHeight - TaskbarVerticalMargin * 2;

            var maxThicknessThatFits = Math.Max(MinBarThickness, availableHeight - LayoutPadding * 2 - 22);
            _effectiveBarThickness = Math.Clamp(
                Math.Min(_requestedBarThickness, maxThicknessThatFits), MinBarThickness, _requestedBarThickness);

            var width = OverlayWidth;
            var height = HeightFor(_effectiveBarThickness);
            var x = taskbarRect.Left + LeftOffset;
            var y = taskbarRect.Top + (taskbarHeight - height) / 2;

            var target = new Rectangle(x, y, width, height);
            if (Bounds != target)
                Bounds = target;

            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private static bool IsTaskbarAutoHiddenAndCollapsed()
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            var state = SHAppBarMessage(0x00000004 /* ABM_GETSTATE */, ref data).ToInt32();
            return (state & ABS_AUTOHIDE) != 0;
        }

        private void AnimationTick()
        {
            var changed = false;
            for (var i = 0; i < 3; i++)
            {
                var delta = _targets[i] - _values[i];
                if (Math.Abs(delta) < 0.15)
                {
                    if (_values[i] != _targets[i])
                    {
                        _values[i] = _targets[i];
                        changed = true;
                    }
                    continue;
                }
                _values[i] += delta * 0.25;
                changed = true;
            }

            // Same easing as the bars, but with a magnitude-relative snap
            // threshold — 0.15 makes sense for a 0-100 percentage, not for
            // a Kbps value that might be in the tens of thousands.
            var netDelta = _networkTarget - _networkDisplay;
            var netEpsilon = Math.Max(1.0, Math.Abs(_networkTarget) * 0.005);
            if (Math.Abs(netDelta) < netEpsilon)
            {
                if (_networkDisplay != _networkTarget)
                {
                    _networkDisplay = _networkTarget;
                    changed = true;
                }
            }
            else
            {
                _networkDisplay += netDelta * 0.25;
                changed = true;
            }

            changed |= EaseNetwork(ref _networkDownloadDisplay, _networkDownloadTarget);
            changed |= EaseNetwork(ref _networkUploadDisplay, _networkUploadTarget);

            if (changed)
                Redraw();
        }

        private static bool EaseNetwork(ref double display, double target)
        {
            var delta = target - display;
            var epsilon = Math.Max(1.0, Math.Abs(target) * 0.005);
            if (Math.Abs(delta) < epsilon)
            {
                if (display == target)
                    return false;
                display = target;
                return true;
            }

            display += delta * 0.25;
            return true;
        }

        // The overlay is transparent, so the high-contrast dark typography
        // remains the most legible choice on both light and dark taskbars.
        private bool IsDarkMode() => true;

        private static bool ReadSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int i && i == 0;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Auto-scales Kbps into whatever unit reads most naturally —
        /// avoids ever showing "2400 Kbps" when "2.4 Mbps" is clearer.
        /// </summary>
        private static string FormatSpeed(double kbps)
        {
            kbps = Math.Max(0, kbps);

            if (kbps < 10)
                return $"{kbps:0.0} Kbps";
            if (kbps < 1000)
                return $"{kbps:0} Kbps";

            var mbps = kbps / 1000.0;
            if (mbps < 1000)
                return $"{mbps:0.0} Mbps";

            var gbps = mbps / 1000.0;
            return $"{gbps:0.00} Gbps";
        }

        public void Redraw()
        {
            if (IsDisposed)
                return;

            var barThickness = _effectiveBarThickness;
            var width = OverlayWidth;
            var height = HeightFor(barThickness);
            if (Bounds.Width != width || Bounds.Height != height)
                Bounds = new Rectangle(Left, Top, width, height);

            var darkMode = IsDarkMode();
            var strokeColor = darkMode ? Color.FromArgb(210, 255, 255, 255) : Color.FromArgb(200, 0, 0, 0);
            var textColor = Color.FromArgb(245, 255, 255, 255);
            var shadowColor = Color.FromArgb(90, 0, 0, 0);

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                DrawCompactLayout(g, width, height, barThickness, darkMode, strokeColor, textColor, shadowColor);
                if (LegacyLayoutEnabled())
                {

                var radius = Math.Max(2, barThickness / 3);
                var globalAlpha = (int)(255 * _opacity);
                var fontSize = barThickness <= 8 ? 6.5f : 7f;
                using var labelFont = new Font("Segoe UI", fontSize, FontStyle.Bold);
                using var valueFont = new Font("Segoe UI", fontSize, FontStyle.Regular);
                using var speedFont = new Font("Segoe UI", fontSize, FontStyle.Bold);

                for (var i = 0; i < 3; i++)
                {
                    var rowY = LayoutPadding + i * (barThickness + RowGap);
                    var rowRect = new Rectangle(LayoutPadding, rowY, width - LayoutPadding * 2, barThickness);
                    var labelRect = new Rectangle(rowRect.X, rowRect.Y, LabelWidth, barThickness);

                    if (_showLabels)
                    {
                        using var textBrush = new SolidBrush(textColor);
                        using var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near };
                        g.DrawString(_labels[i], labelFont, textBrush, labelRect, fmt);
                    }

                    if (i == NetworkRowIndex)
                    {
                        // No bar — just the formatted speed, right-aligned
                        // across the space the bar+value column used to
                        // occupy, colored by percent-of-peak like the bars.
                        if (_showValues)
                        {
                            var speedRect = new Rectangle(
                                labelRect.Right + SectionGap, rowRect.Y,
                                BarLength + SectionGap + ValueWidth, barThickness);

                            using var speedBrush = new SolidBrush(Color.FromArgb(globalAlpha, _colors[i]));
                            using var speedFmt = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far };
                            g.DrawString(FormatSpeed(_networkDisplay), speedFont, speedBrush, speedRect, speedFmt);
                        }
                        continue;
                    }

                    var trackRect = new Rectangle(labelRect.Right + SectionGap, rowRect.Y, BarLength, barThickness);
                    var valueRect = new Rectangle(trackRect.Right + SectionGap, rowRect.Y, ValueWidth, barThickness);

                    using (var shadowBrush = new SolidBrush(shadowColor))
                        FillRoundedRect(g, shadowBrush, Offset(trackRect, 0, 1), radius + 1);

                    using (var trackBrush = new SolidBrush(Color.FromArgb((int)(70 * _opacity), 255, 255, 255)))
                        FillRoundedRect(g, trackBrush, trackRect, radius);
                    using (var trackStroke = new Pen(Color.FromArgb((int)(strokeColor.A * 0.5), strokeColor), 1f))
                        DrawRoundedRect(g, trackStroke, trackRect, radius);

                    var filledWidth = (int)(BarLength * (_values[i] / 100.0));
                    if (filledWidth > 1)
                    {
                        var fillRect = new Rectangle(trackRect.X, trackRect.Y, filledWidth, barThickness);
                        var baseColor = Saturate(_colors[i], 1.15);
                        var leadColor = Lighten(baseColor, 0.35);

                        using (var gradientBrush = new LinearGradientBrush(
                            fillRect, Color.FromArgb(globalAlpha, baseColor), Color.FromArgb(globalAlpha, leadColor),
                            LinearGradientMode.Horizontal))
                        {
                            FillRoundedRect(g, gradientBrush, fillRect, radius);
                        }
                    }

                    using (var strokePen = new Pen(strokeColor, 1.25f))
                        DrawRoundedRect(g, strokePen, trackRect, radius);

                    if (_showValues)
                    {
                        using var valueBrush = new SolidBrush(textColor);
                        using var valueFmt = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near };
                        g.DrawString($"{_values[i]:0}%", valueFont, valueBrush, valueRect, valueFmt);
                    }
                }
                }
            }

            PremultiplyAlpha(bitmap);
            DrawToScreen(bitmap);
        }

        private void DrawCompactLayout(Graphics g, int width, int height, int barThickness, bool darkMode,
            Color strokeColor, Color textColor, Color shadowColor)
        {
            // Keep the radius just inside half the height so the helper
            // always produces a capsule instead of falling back to a box.
            var radius = Math.Max(2, barThickness / 2 - 1);
            var globalAlpha = (int)(255 * _opacity);
            var fontSize = barThickness <= 8 ? 6.5f : 7f;
            using var labelFont = new Font("Segoe UI", fontSize, FontStyle.Bold);
            using var valueFont = new Font("Segoe UI", fontSize, FontStyle.Regular);
            using var speedFont = new Font("Segoe UI", barThickness <= 8 ? 8f : 8.5f, FontStyle.Bold);

            var cardHeight = height - LayoutPadding * 2;
            var networkX = width - LayoutPadding - NetworkCardWidth;
            var metricWidth = (networkX - LayoutPadding - CardGap) / 2;
            if (!_showLabels && !_showValues)
            {
                DrawStackedMetricBars(g,
                    new Rectangle(LayoutPadding, LayoutPadding,
                        networkX - LayoutPadding - CardGap, cardHeight),
                    barThickness, radius, globalAlpha);
            }
            else
            {
                DrawMetricCard(g, new Rectangle(LayoutPadding, LayoutPadding, metricWidth, cardHeight), 0,
                    barThickness, radius, globalAlpha, textColor, strokeColor, shadowColor, labelFont, valueFont, darkMode);
                DrawMetricCard(g, new Rectangle(LayoutPadding + metricWidth + CardGap, LayoutPadding, metricWidth, cardHeight), 1,
                    barThickness, radius, globalAlpha, textColor, strokeColor, shadowColor, labelFont, valueFont, darkMode);
            }
            DrawNetworkCard(g, new Rectangle(networkX, LayoutPadding, NetworkCardWidth, cardHeight),
                textColor, strokeColor, shadowColor, speedFont, globalAlpha, darkMode);

            using var dividerPen = new Pen(Color.FromArgb((int)(strokeColor.A * 0.28), strokeColor), 1f);
            var secondDividerX = networkX - CardGap / 2;
            if (_showLabels || _showValues)
            {
                var firstDividerX = LayoutPadding + metricWidth + CardGap / 2;
                g.DrawLine(dividerPen, firstDividerX, LayoutPadding + 3, firstDividerX, height - LayoutPadding - 3);
            }
            g.DrawLine(dividerPen, secondDividerX, LayoutPadding + 3, secondDividerX, height - LayoutPadding - 3);
        }

        private static bool LegacyLayoutEnabled() =>
            Environment.GetEnvironmentVariable("METRICFORGE_LEGACY_LAYOUT") == "1";

        private void DrawStackedMetricBars(Graphics g, Rectangle column, int barThickness, int radius, int globalAlpha)
        {
            var gap = 3;
            var rowHeight = (column.Height - gap) / 2;
            var trackWidth = column.Width - 8;
            for (var index = 0; index < 2; index++)
            {
                var track = new Rectangle(column.X + 4, column.Y + index * (rowHeight + gap) +
                    Math.Max(0, (rowHeight - barThickness) / 2), trackWidth, Math.Min(barThickness, rowHeight));
                var trackRadius = Math.Max(2, Math.Min(radius, track.Height / 2 - 1));
                using (var trackBrush = new SolidBrush(Color.FromArgb((int)(65 * _opacity), 255, 255, 255)))
                    FillRoundedRect(g, trackBrush, track, trackRadius);

                var filledWidth = Math.Max(1, (int)(track.Width * (_values[index] / 100.0)));
                var fill = new Rectangle(track.X, track.Y, Math.Min(track.Width, filledWidth), track.Height);
                var baseColor = Saturate(_colors[index], 1.15);
                using var gradient = new LinearGradientBrush(fill, Color.FromArgb(globalAlpha, baseColor),
                    Color.FromArgb(globalAlpha, Lighten(baseColor, 0.35)), LinearGradientMode.Horizontal);
                FillRoundedRect(g, gradient, fill, trackRadius);
            }
        }

        private void DrawMetricCard(Graphics g, Rectangle card, int index, int barThickness, int radius,
            int globalAlpha, Color textColor, Color strokeColor, Color shadowColor, Font labelFont, Font valueFont,
            bool darkMode)
        {
            // Keep the header and bar coordinates fixed when labels are hidden;
            // the toggle should remove text, not reflow the metrics.
            const int headerHeight = 11;
            var labelHeight = headerHeight;
            var headerRect = new Rectangle(card.X + 5, card.Y + 2, card.Width - 10, labelHeight);
            if (_showLabels)
            {
                using var labelBrush = new SolidBrush(textColor);
                using var labelFmt = new StringFormat { LineAlignment = StringAlignment.Center };
                DrawReadableText(g, _labels[index], labelFont, labelBrush, headerRect, labelFmt, darkMode);
            }

            if (_showValues)
            {
                using var valueBrush = new SolidBrush(textColor);
                using var valueFmt = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far };
                DrawReadableText(g, $"{_values[index]:0}%", valueFont, valueBrush, headerRect, valueFmt, darkMode);
            }

            var track = new Rectangle(card.X + 5, card.Y + labelHeight + 3, card.Width - 10, barThickness);
            using (var trackBrush = new SolidBrush(Color.FromArgb((int)(65 * _opacity), 255, 255, 255)))
                FillRoundedRect(g, trackBrush, track, radius);
            var filledWidth = Math.Max(1, (int)(track.Width * (_values[index] / 100.0)));
            var fill = new Rectangle(track.X, track.Y, Math.Min(track.Width, filledWidth), track.Height);
            var baseColor = Saturate(_colors[index], 1.15);
            using (var gradient = new LinearGradientBrush(fill, Color.FromArgb(globalAlpha, baseColor),
                       Color.FromArgb(globalAlpha, Lighten(baseColor, 0.35)), LinearGradientMode.Horizontal))
                FillRoundedRect(g, gradient, fill, radius);

        }

        private void DrawNetworkCard(Graphics g, Rectangle card, Color textColor, Color strokeColor,
            Color shadowColor, Font speedFont, int globalAlpha, bool darkMode)
        {
            var speedText = FormatSpeed(_networkDisplay);
            var speedWidth = (int)Math.Ceiling(g.MeasureString(speedText, speedFont).Width);
            var groupWidth = 10 + 4 + speedWidth;
            var groupX = card.X + Math.Max(0, (card.Width - groupWidth) / 2);
            DrawNetworkSphere(g, new Rectangle(groupX, card.Y + (card.Height - 10) / 2, 10, 10),
                _networkDownloadDisplay > 0, _networkUploadDisplay > 0, darkMode);
            using var speedBrush = new SolidBrush(Color.FromArgb(globalAlpha, textColor));
            using var speedFmt = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center };
            DrawReadableText(g, speedText, speedFont, speedBrush,
                new Rectangle(groupX + 14, card.Y, speedWidth, card.Height), speedFmt, darkMode);
        }

        private void DrawNetworkSphere(Graphics g, Rectangle bounds, bool hasDownload, bool hasUpload, bool darkMode)
        {
            var medium = EnsureThemeContrast(_colors[1], darkMode);
            var high = EnsureThemeContrast(_colors[2], darkMode);
            using var sphereBrush = new SolidBrush(Color.FromArgb(hasDownload ? 255 : 70, medium));
            g.FillEllipse(sphereBrush, bounds);
            using var uploadBrush = new SolidBrush(Color.FromArgb(hasUpload ? 255 : 70, high));
            g.FillPie(uploadBrush, bounds, 0, 180);
            using var outline = new Pen(Color.FromArgb(150, 255, 255, 255), 1f);
            g.DrawEllipse(outline, bounds);

            using var highlight = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
            g.FillEllipse(highlight, new Rectangle(bounds.X + 2, bounds.Y + 2, 2, 2));
        }

        private static void DrawReadableText(Graphics g, string text, Font font, Brush brush,
            Rectangle bounds, StringFormat format, bool darkMode)
        {
            if (!darkMode)
            {
                g.DrawString(text, font, brush, bounds, format);
                return;
            }

            var halo = darkMode ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(170, 255, 255, 255);
            using var haloBrush = new SolidBrush(halo);
            foreach (var (x, y) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                g.DrawString(text, font, haloBrush, Offset(bounds, x, y), format);
            g.DrawString(text, font, brush, bounds, format);
        }

        private static Color EnsureThemeContrast(Color color, bool darkMode)
        {
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
            if (darkMode && luminance < 0.5)
                return Lighten(color, 0.65);
            if (!darkMode && luminance > 0.7)
                return Darken(color, 0.45);
            return color;
        }

        private static Color Darken(Color color, double amount)
        {
            int Adjust(byte value) => (int)Math.Clamp(value * (1 - amount), 0, 255);
            return Color.FromArgb(255, Adjust(color.R), Adjust(color.G), Adjust(color.B));
        }

        private static void DrawCardSurface(Graphics g, Rectangle card, int radius, Color strokeColor, Color shadowColor)
        {
            using (var shadowBrush = new SolidBrush(shadowColor))
                FillRoundedRect(g, shadowBrush, Offset(card, 0, 1), radius);
            using (var cardBrush = new SolidBrush(Color.FromArgb(35, 255, 255, 255)))
                FillRoundedRect(g, cardBrush, card, radius);
            using var cardPen = new Pen(Color.FromArgb((int)(strokeColor.A * 0.55), strokeColor), 1f);
            DrawRoundedRect(g, cardPen, card, radius);
        }

        private static Rectangle Offset(Rectangle r, int dx, int dy) =>
            new(r.X + dx, r.Y + dy, r.Width, r.Height);

        private static Color Lighten(Color c, double amount)
        {
            int Adj(byte v) => (int)Math.Clamp(v + (255 - v) * amount, 0, 255);
            return Color.FromArgb(255, Adj(c.R), Adj(c.G), Adj(c.B));
        }

        private static Color Saturate(Color c, double factor)
        {
            var hsl = RgbToHsl(c);
            var s = Math.Clamp(hsl.s * factor, 0, 1);
            return HslToRgb(hsl.h, s, hsl.l);
        }

        private static (double h, double s, double l) RgbToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var l = (max + min) / 2.0;
            if (max == min) return (0, 0, l);

            var d = max - min;
            var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h;
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
            return (h, s, l);
        }

        private static Color HslToRgb(double h, double s, double l)
        {
            if (s == 0)
            {
                var v = (byte)(l * 255);
                return Color.FromArgb(255, v, v, v);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double HueToRgb(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 1.0 / 2) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }

            var r = (byte)(HueToRgb(h + 1.0 / 3) * 255);
            var g = (byte)(HueToRgb(h) * 255);
            var b = (byte)(HueToRgb(h - 1.0 / 3) * 255);
            return Color.FromArgb(255, r, g, b);
        }

        private static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using var path = RoundedRectPath(rect, radius);
            g.FillPath(brush, path);
        }

        private static void DrawRoundedRect(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using var path = RoundedRectPath(rect, radius);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRectPath(Rectangle rect, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            if (diameter <= 0 || rect.Width < diameter || rect.Height < diameter)
            {
                path.AddRectangle(rect);
                return path;
            }

            var arc = new Rectangle(rect.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void PremultiplyAlpha(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var byteCount = Math.Abs(data.Stride) * bmp.Height;
                var buffer = new byte[byteCount];
                Marshal.Copy(data.Scan0, buffer, 0, byteCount);

                for (var i = 0; i < buffer.Length; i += 4)
                {
                    var a = buffer[i + 3];
                    buffer[i] = (byte)(buffer[i] * a / 255);
                    buffer[i + 1] = (byte)(buffer[i + 1] * a / 255);
                    buffer[i + 2] = (byte)(buffer[i + 2] * a / 255);
                }

                Marshal.Copy(buffer, 0, data.Scan0, byteCount);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private void DrawToScreen(Bitmap bitmap)
        {
            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            var oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
                var pointSource = new POINT { X = 0, Y = 0 };
                var topPos = new POINT { X = Left, Y = Top };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Dispose();
                _watchdogTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
