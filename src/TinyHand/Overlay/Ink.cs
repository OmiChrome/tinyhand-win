using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace TinyHand.Overlay;

/// <summary>Strokes drawn with the pointer, in overlay pixels. Lives on the render thread.</summary>
internal sealed class Ink : IDisposable
{
    private sealed class Stroke : IDisposable
    {
        public readonly List<Vector2> Points = new();
        public required Color4 Color;
        public required float Width;
        public ID2D1PathGeometry? Geometry;
        public ID2D1GeometryRealization? Frozen;
        public double? EndedAt;
        public bool Dirty = true;

        public void Dispose()
        {
            Geometry?.Dispose();
            Frozen?.Dispose();
        }
    }

    private const double FadeDuration = 0.6;
    private readonly List<Stroke> strokes = new();
    private Stroke? current;

    public bool IsDrawing => current != null;
    public int Count => strokes.Count;
    public Color4 Color { get; set; } = new(0.53f, 0.33f, 0.95f, 1f);

    /// <summary>True when something changed since the last draw (new points, fades, removals).</summary>
    public bool NeedsRedraw(double now)
    {
        if (strokes.Any(s => s.Dirty))
        {
            return true;
        }
        if (Preferences.FadeSeconds <= 0)
        {
            return false;
        }
        double delay = Math.Max(0.5, Preferences.FadeSeconds);
        return strokes.Any(s => s.EndedAt is { } end && now >= end + delay);
    }

    public void Begin(Vector2 point, float width)
    {
        var s = new Stroke { Color = Color, Width = width };
        s.Points.Add(point);
        strokes.Add(s);
        current = s;
    }

    public void Extend(Vector2 point, float minStep)
    {
        if (current == null)
        {
            return;
        }
        if (Vector2.Distance(point, current.Points[^1]) < minStep)
        {
            return;
        }
        current.Points.Add(point);
        current.Dirty = true;
    }

    public void End(double now)
    {
        if (current == null)
        {
            return;
        }
        current.EndedAt = now;
        current.Dirty = true;
        current = null;
    }

    public void Clear()
    {
        foreach (var s in strokes)
        {
            s.Dispose();
        }
        strokes.Clear();
        current = null;
        Cleared = true;
    }

    /// <summary>Set by Clear so the renderer knows to repaint even if nothing is left to draw.</summary>
    public bool Cleared { get; set; }

    /// <summary>Drop device resources (device lost / recreated); geometry is rebuilt on next draw.</summary>
    public void ReleaseDeviceResources()
    {
        foreach (var s in strokes)
        {
            s.Dispose();
            s.Geometry = null;
            s.Frozen = null;
            s.Dirty = true;
        }
    }

    public void Draw(ID2D1DeviceContext1 ctx, ID2D1Factory1 factory, ID2D1StrokeStyle style, ID2D1SolidColorBrush brush,
        float scale, double now)
    {
        bool keep = Preferences.FadeSeconds <= 0;
        double delay = Math.Max(0.5, Preferences.FadeSeconds);
        var shadowOffset = Matrix3x2.CreateTranslation(1 * scale, 1.5f * scale);

        for (int i = strokes.Count - 1; i >= 0; i--)
        {
            var s = strokes[i];
            if (!keep && s.EndedAt is { } end && now > end + delay + FadeDuration)
            {
                s.Dispose();
                strokes.RemoveAt(i);
            }
        }

        foreach (var s in strokes)
        {
            float opacity = 1;
            if (!keep && s.EndedAt is { } end && now > end + delay)
            {
                double t = Math.Clamp((now - end - delay) / FadeDuration, 0, 1);
                opacity = (float)(1 - t * t); // ease in
            }
            if (s.Dirty || s.Geometry == null)
            {
                Rebuild(s, ctx, factory, style);
            }
            s.Dirty = false;

            var old = ctx.Transform;
            brush.Color = new Color4(0, 0, 0, 0.18f * opacity);
            ctx.Transform = shadowOffset * old;
            Paint(ctx, s, brush, style);
            ctx.Transform = old;
            brush.Color = new Color4(s.Color.R, s.Color.G, s.Color.B, opacity);
            Paint(ctx, s, brush, style);
        }
    }

    private static void Paint(ID2D1DeviceContext1 ctx, Stroke s, ID2D1SolidColorBrush brush, ID2D1StrokeStyle style)
    {
        if (s.Frozen != null)
        {
            ctx.DrawGeometryRealization(s.Frozen, brush);
        }
        else if (s.Geometry != null)
        {
            ctx.DrawGeometry(s.Geometry, brush, s.Width, style);
        }
    }

    /// <summary>Same smoothing as the macOS version: quadratic curves through the midpoints.</summary>
    private void Rebuild(Stroke s, ID2D1DeviceContext1 ctx, ID2D1Factory1 factory, ID2D1StrokeStyle style)
    {
        s.Geometry?.Dispose();
        s.Frozen?.Dispose();
        s.Frozen = null;
        var geometry = factory.CreatePathGeometry();
        using (var sink = geometry.Open())
        {
            var pts = s.Points;
            sink.BeginFigure(pts[0], FigureBegin.Hollow);
            if (pts.Count == 1)
            {
                sink.AddLine(pts[0]);
            }
            for (int i = 1; i < pts.Count; i++)
            {
                var prev = pts[i - 1];
                var mid = (prev + pts[i]) / 2;
                sink.AddQuadraticBezier(new QuadraticBezierSegment { Point1 = prev, Point2 = mid });
            }
            if (pts.Count > 1)
            {
                sink.AddLine(pts[^1]);
            }
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        s.Geometry = geometry;
        if (s != current)
        {
            // Finished: cache the tessellation so old strokes cost almost nothing per frame.
            s.Frozen = ctx.CreateStrokedGeometryRealization(geometry, 0.25f, s.Width, style);
        }
    }

    public void Dispose() => Clear();
}
