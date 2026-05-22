using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace v2rayN.AutoSwitchCompanion;

public sealed class FloatingUsageWindow : Form
{
    private const int Diameter = 174;
    private const int SurfaceInset = 5;
    private const int RenderScale = 3;
    private const int WaveLength = 118;
    private const int WsExLayered = 0x00080000;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtTransparent = -1;

    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private FloatingUsageState _state = FloatingUsageState.Loading;
    private Point _dragOffset;
    private Point _dragStart;
    private bool _dragging;
    private bool _dragMoved;
    private float _wavePhase;

    public event EventHandler? ManualRefreshRequested;

    public FloatingUsageWindow()
    {
        Text = "Cloudflare \u4eca\u65e5\u7528\u91cf";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(Diameter, Diameter);
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = Color.Black;
        Cursor = Cursors.SizeAll;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(
            Math.Max(workingArea.Left, workingArea.Right - Width - 30),
            Math.Max(workingArea.Top, workingArea.Bottom - Height - 58));

        _animationTimer.Interval = 33;
        _animationTimer.Tick += (_, _) =>
        {
            _wavePhase = (_wavePhase + 2.1f) % WaveLength;
            RenderLayeredWindow();
        };
        _animationTimer.Start();
    }

    public void UpdateUsage(FloatingUsageState state)
    {
        _state = state;
        RenderLayeredWindow();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RenderLayeredWindow();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RenderLayeredWindow();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragMoved = false;
        _dragStart = e.Location;
        _dragOffset = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        if (Math.Abs(e.X - _dragStart.X) > 4 || Math.Abs(e.Y - _dragStart.Y) > 4)
        {
            _dragMoved = true;
        }

        Location = new Point(Left + e.X - _dragOffset.X, Top + e.Y - _dragOffset.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        if (!_dragMoved)
        {
            ManualRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Rendering is pushed through UpdateLayeredWindow for real per-pixel alpha.
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Avoid WinForms painting a rectangular background behind the layered bitmap.
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            var screenPoint = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            var localPoint = PointToClient(screenPoint);
            var center = new PointF(Width / 2f, Height / 2f);
            var radius = (Math.Min(Width, Height) / 2f) - SurfaceInset;
            var dx = localPoint.X - center.X;
            var dy = localPoint.Y - center.Y;

            if ((dx * dx) + (dy * dy) > radius * radius)
            {
                m.Result = HtTransparent;
                return;
            }

            m.Result = HtClient;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _animationTimer.Stop();
        _animationTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void RenderLayeredWindow()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var rawBitmap = new Bitmap(Width * RenderScale, Height * RenderScale, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(rawBitmap))
        {
            graphics.ScaleTransform(RenderScale, RenderScale);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            var surfaceRect = new RectangleF(
                SurfaceInset,
                SurfaceInset,
                Width - SurfaceInset * 2 - 1,
                Height - SurfaceInset * 2 - 1);
            using var surfacePath = new GraphicsPath();
            surfacePath.AddEllipse(surfaceRect);

            var accent = GetWaterColor(_state);
            DrawGlassMaterial(graphics, surfaceRect, surfacePath, accent);
            DrawLiquid(graphics, surfaceRect, surfacePath, accent);
            DrawRefraction(graphics, surfaceRect, surfacePath, accent);
            DrawText(graphics);
        }

        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawImage(rawBitmap, new Rectangle(0, 0, Width, Height), 0, 0, rawBitmap.Width, rawBitmap.Height, GraphicsUnit.Pixel);
        }
        FeatherOuterAlpha(bitmap);

        ApplyLayeredBitmap(bitmap);
    }

    private static void FeatherOuterAlpha(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var byteCount = Math.Abs(data.Stride) * data.Height;
            var pixels = new byte[byteCount];
            Marshal.Copy(data.Scan0, pixels, 0, byteCount);

            var centerX = (bitmap.Width - 1) / 2f;
            var centerY = (bitmap.Height - 1) / 2f;
            var radius = Math.Min(bitmap.Width, bitmap.Height) / 2f - SurfaceInset - 0.4f;
            const float feather = 3.8f;

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var index = row + x * 4;
                    var dx = x - centerX;
                    var dy = y - centerY;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);

                    if (distance >= radius)
                    {
                        pixels[index] = 0;
                        pixels[index + 1] = 0;
                        pixels[index + 2] = 0;
                        pixels[index + 3] = 0;
                        continue;
                    }

                    if (distance <= radius - feather)
                    {
                        continue;
                    }

                    var factor = Math.Clamp((radius - distance) / feather, 0f, 1f);
                    factor = factor * factor * (3f - 2f * factor);
                    pixels[index] = (byte)Math.Round(pixels[index] * factor);
                    pixels[index + 1] = (byte)Math.Round(pixels[index + 1] * factor);
                    pixels[index + 2] = (byte)Math.Round(pixels[index + 2] * factor);
                    pixels[index + 3] = (byte)Math.Round(pixels[index + 3] * factor);
                }
            }

            Marshal.Copy(pixels, 0, data.Scan0, byteCount);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private void ApplyLayeredBitmap(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;

        try
        {
            bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memoryDc, bitmapHandle);

            var size = new NativeSize(Width, Height);
            var source = new NativePoint(0, 0);
            var position = new NativePoint(Left, Top);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(Handle, screenDc, ref position, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                SelectObject(memoryDc, oldBitmap);
            }

            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void DrawGlassMaterial(Graphics graphics, RectangleF rect, GraphicsPath path, Color accent)
    {
        using var baseBrush = new LinearGradientBrush(
            rect,
            Color.FromArgb(248, 253, 254, 255),
            Color.FromArgb(238, 231, 237, 247),
            LinearGradientMode.Vertical);
        graphics.FillPath(baseBrush, path);

        using var depthBrush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(rect.Left + rect.Width * 0.38f, rect.Top + rect.Height * 0.24f),
            CenterColor = Color.FromArgb(255, 255, 255, 255),
            SurroundColors = [Color.FromArgb(235, 222, 230, 242)]
        };
        graphics.FillPath(depthBrush, path);
    }

    private void DrawLiquid(Graphics graphics, RectangleF rect, GraphicsPath clipPath, Color accent)
    {
        var state = graphics.Save();
        graphics.SetClip(clipPath, CombineMode.Replace);

        if (_state.HasMatchingRule && !_state.IsError)
        {
            DrawWave(graphics, rect, accent, _state.Level, 3.0f, 0, 236);
            DrawWave(graphics, rect, accent, _state.Level, 4.2f, 42, 182);
        }
        else
        {
            using var idleBrush = new LinearGradientBrush(
                rect,
                Color.FromArgb(46, accent),
                Color.FromArgb(112, accent),
                LinearGradientMode.Vertical);
            graphics.FillEllipse(idleBrush, new RectangleF(rect.Left, rect.Top + rect.Height * 0.56f, rect.Width, rect.Height * 0.54f));
        }

        graphics.Restore(state);
    }

    private void DrawWave(Graphics graphics, RectangleF rect, Color accent, float level, float amplitude, float offset, int alpha)
    {
        var waterTop = rect.Bottom - rect.Height * Math.Clamp(level, 0, 1);
        using var path = new GraphicsPath();
        path.StartFigure();
        path.AddLine(rect.Left - 5, rect.Bottom + 5, rect.Left - 5, waterTop);

        var previous = new PointF(rect.Left - 5, waterTop);
        for (float x = rect.Left - 5; x <= rect.Right + 5; x += 3)
        {
            var y = waterTop
                + MathF.Sin((x + _wavePhase + offset) / WaveLength * MathF.Tau) * amplitude
                + MathF.Sin((x * 1.67f - _wavePhase) / (WaveLength * 0.82f) * MathF.Tau) * 1.0f;
            var next = new PointF(x, y);
            path.AddLine(previous, next);
            previous = next;
        }

        path.AddLine(previous, new PointF(rect.Right + 5, rect.Bottom + 5));
        path.CloseFigure();

        using var fill = new LinearGradientBrush(
            rect,
            Color.FromArgb(Math.Min(255, alpha + 14), ControlPaint.Light(accent, 0.18f)),
            Color.FromArgb(Math.Min(255, alpha + 6), accent),
            LinearGradientMode.Vertical);
        graphics.FillPath(fill, path);

        using var crest = new Pen(Color.FromArgb(82, 255, 255, 255), 0.9f);
        graphics.DrawPath(crest, path);
    }

    private static void DrawRefraction(Graphics graphics, RectangleF rect, GraphicsPath clipPath, Color accent)
    {
        var state = graphics.Save();
        graphics.SetClip(clipPath, CombineMode.Replace);

        using var sheenPath = new GraphicsPath();
        sheenPath.AddBezier(
            new PointF(rect.Left + rect.Width * 0.18f, rect.Top + rect.Height * 0.19f),
            new PointF(rect.Left + rect.Width * 0.38f, rect.Top + rect.Height * 0.04f),
            new PointF(rect.Left + rect.Width * 0.70f, rect.Top + rect.Height * 0.08f),
            new PointF(rect.Left + rect.Width * 0.84f, rect.Top + rect.Height * 0.32f));
        using var sheenPen = new Pen(Color.FromArgb(154, 255, 255, 255), 1.1f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawPath(sheenPen, sheenPath);

        using var prismBrush = new LinearGradientBrush(
            new RectangleF(rect.Left + rect.Width * 0.18f, rect.Top, rect.Width * 0.64f, rect.Height),
            Color.FromArgb(0, accent),
            Color.FromArgb(72, 255, 255, 255),
            18f);
        using var prismPath = new GraphicsPath();
        prismPath.AddEllipse(rect.Left + rect.Width * 0.14f, rect.Top + rect.Height * 0.07f, rect.Width * 0.78f, rect.Height * 0.42f);
        graphics.FillPath(prismBrush, prismPath);

        graphics.Restore(state);
    }

    private void DrawText(Graphics graphics)
    {
        var nameText = _state.HasMatchingRule && !string.IsNullOrWhiteSpace(_state.DisplayName)
            ? _state.DisplayName
            : _state.Message;
        if (string.IsNullOrWhiteSpace(nameText))
        {
            nameText = _state.HasMatchingRule ? "\u672a\u914d\u7f6e\u540d\u79f0" : "\u672a\u5339\u914d\u5206\u7ec4";
        }

        var remainingText = _state.HasMatchingRule && !_state.IsError
            ? $"{_state.RemainingRequests:N0}"
            : nameText;
        var delayText = _state.HasMatchingRule && !_state.IsError
            ? (string.IsNullOrWhiteSpace(_state.DelayDisplay) ? "-" : _state.DelayDisplay)
            : string.Empty;
        var speedText = _state.HasMatchingRule && !_state.IsError
            ? (string.IsNullOrWhiteSpace(_state.SpeedDisplay) ? "-" : _state.SpeedDisplay)
            : string.Empty;

        using var nameFont = CreateFont(9.8f, FontStyle.Regular);
        using var numberFont = CreateFont(15.4f, FontStyle.Bold);
        using var smallFont = CreateFont(8.2f, FontStyle.Regular);
        using var primaryBrush = new SolidBrush(Color.FromArgb(235, 18, 23, 31));
        using var secondaryBrush = new SolidBrush(Color.FromArgb(184, 62, 70, 84));
        using var textShadow = new SolidBrush(Color.FromArgb(72, 255, 255, 255));

        DrawCenteredString(graphics, FitText(graphics, nameText, nameFont, Width - 34), nameFont, textShadow, 49.7f, 18);
        DrawCenteredString(graphics, FitText(graphics, nameText, nameFont, Width - 34), nameFont, secondaryBrush, 49, 18);
        DrawCenteredString(graphics, FitText(graphics, remainingText, numberFont, Width - 30), numberFont, textShadow, 72.7f, 27);
        DrawCenteredString(graphics, FitText(graphics, remainingText, numberFont, Width - 30), numberFont, primaryBrush, 72, 27);

        if (!string.IsNullOrWhiteSpace(delayText))
        {
            DrawCenteredString(graphics, FitText(graphics, delayText, smallFont, Width - 40), smallFont, secondaryBrush, 99, 15);
        }

        if (!string.IsNullOrWhiteSpace(speedText))
        {
            DrawCenteredString(graphics, FitText(graphics, speedText, smallFont, Width - 40), smallFont, secondaryBrush, 116, 15);
        }
    }

    private static Font CreateFont(float size, FontStyle style)
    {
        try
        {
            return new Font("Segoe UI Variable Text", size, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
    }

    private static void DrawCenteredString(Graphics graphics, string text, Font font, Brush brush, float y, float height)
    {
        var rect = new RectangleF(12, y, Diameter - 24, height);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, rect, format);
    }

    private static string FitText(Graphics graphics, string text, Font font, int maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || graphics.MeasureString(text, font).Width <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        var candidate = text;
        while (candidate.Length > 1)
        {
            candidate = candidate[..^1];
            if (graphics.MeasureString(candidate + ellipsis, font).Width <= maxWidth)
            {
                return candidate + ellipsis;
            }
        }

        return ellipsis;
    }

    private static Color GetWaterColor(FloatingUsageState state)
    {
        if (!state.HasMatchingRule || state.IsError)
        {
            return Color.FromArgb(142, 142, 147);
        }

        if (state.Requests < CompanionSettings.FloatingUsageBlueBelowRequests)
        {
            return Color.FromArgb(10, 132, 255);
        }

        if (state.Requests >= CompanionSettings.FloatingUsageYellowFromRequests
            && state.Requests <= CompanionSettings.FloatingUsageRedFromRequests - 1)
        {
            return Color.FromArgb(255, 214, 10);
        }

        return Color.FromArgb(255, 69, 58);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hWnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public NativeSize(int width, int height)
        {
            Cx = width;
            Cy = height;
        }

        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
