using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TinyHand;

/// <summary>Turns a PNG of a hand (e.g. from ChatGPT) into a TinyHand pointer: checks it, then puts it on a stick.</summary>
internal static class HandMaker
{
    private const string Placeholder = "[describe your hand, e.g. knitted wool in mustard yellow]";

    public const string PromptTemplate = """
        Make a pointer image for TinyHand, an app that shows a toy hand on screen.

        Draw one cartoon right hand in this style: [describe your hand, e.g. knitted wool in mustard yellow]

        Rules:
        - We see the BACK of the hand. The palm faces away from us. The knuckles and the smooth back of the hand face the viewer.
        - The index finger points straight up. The other three fingers are folded down, so we only see their knuckles, never their fingertips or fingernails.
        - The raised index finger sits at the far left of the knuckle row, right next to the thumb. Three folded knuckles sit to its right. It must never look like a middle finger.
        - The thumb is tucked against the left side of the hand. Nothing crosses the front of the fist: no thumb, no fingertips, no fingernails, no palm.
        - The fingertip is the highest point of the image. Nothing above or beside it.
        - The hand ends at the bottom in a wrist cuff or a clean flat wrist, facing straight down, centred.
        - Upright, not tilted. Whole hand visible, nothing cropped.
        - Transparent background. No shadow, no floor, no outline glow, no text, no stick, no arm.
        - Soft 3D toy look, bright and readable at small sizes.
        - Portrait image, hand filling most of the height with a little empty space around it.
        """;

    public static string Prompt(string idea)
    {
        var trimmed = idea.Trim();
        return PromptTemplate.Replace(Placeholder, trimmed.Length == 0 ? "a soft vinyl toy in a bright colour" : trimmed);
    }

    public sealed class Problem(string message) : Exception(message);

    public sealed record Result(BitmapSource Image, byte[] Png, uint Ink);

    private sealed class Pixels(int width, int height, byte[] bgra)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        /// <summary>Straight (not premultiplied) BGRA.</summary>
        public byte[] Bgra { get; } = bgra;
        public byte Alpha(int x, int y) => Bgra[(y * Width + x) * 4 + 3];
    }

    private readonly record struct Box(int MinX, int MinY, int MaxX, int MaxY);

    public static Result Make(string path)
    {
        BitmapSource frame;
        try
        {
            using var stream = File.OpenRead(path);
            frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch
        {
            throw new Problem("That file isn't an image TinyHand can read.");
        }
        var p = Read(frame);
        Check(p);
        var composed = Compose(p) ?? throw new Problem("TinyHand couldn't put that hand on a stick.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return new Result(composed, ms.ToArray(), Ink(p));
    }

    private static Pixels Read(BitmapSource image)
    {
        var f = image.Format;
        bool hasAlpha = f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64
                        || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float || f == PixelFormats.Indexed8
                        || f == PixelFormats.Indexed4 || f == PixelFormats.Indexed2 || f == PixelFormats.Indexed1;
        if (!hasAlpha)
        {
            throw new Problem("The image needs a transparent background. Save it as a PNG with transparency.");
        }
        int w = image.PixelWidth;
        int h = image.PixelHeight;
        if (w < 64 || h < 64)
        {
            throw new Problem("The image is too small. Use one at least 512 pixels tall.");
        }
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var data = new byte[w * h * 4];
        bgra.CopyPixels(data, w * 4, 0);
        return new Pixels(w, h, data);
    }

    private static void Check(Pixels p)
    {
        int w = p.Width, h = p.Height;
        int edge = 0, edgeSolid = 0;
        for (int x = 0; x < w; x++)
        {
            edge += 2;
            edgeSolid += (p.Alpha(x, 0) > 32 ? 1 : 0) + (p.Alpha(x, h - 1) > 32 ? 1 : 0);
        }
        for (int y = 0; y < h; y++)
        {
            edge += 2;
            edgeSolid += (p.Alpha(0, y) > 32 ? 1 : 0) + (p.Alpha(w - 1, y) > 32 ? 1 : 0);
        }
        if ((double)edgeSolid / edge > 0.02)
        {
            throw new Problem("The background isn't transparent, or the hand touches the edge of the image.");
        }

        // One connected shape (coarse grid flood fill).
        int step = Math.Max(1, Math.Min(w, h) / 256);
        int gw = w / step, gh = h / step;
        var grid = new bool[gw * gh];
        for (int gy = 0; gy < gh; gy++)
        {
            for (int gx = 0; gx < gw; gx++)
            {
                grid[gy * gw + gx] = p.Alpha(gx * step, gy * step) > 128;
            }
        }
        var seen = new bool[gw * gh];
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (int start = 0; start < grid.Length; start++)
        {
            if (!grid[start] || seen[start])
            {
                continue;
            }
            stack.Push(start);
            seen[start] = true;
            int n = 0;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                n++;
                int x = i % gw, y = i / gw;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= gw || ny >= gh)
                    {
                        continue;
                    }
                    int j = ny * gw + nx;
                    if (grid[j] && !seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }
            sizes.Add(n);
        }
        if (sizes.Count == 0)
        {
            throw new Problem("The image is empty.");
        }
        if ((double)sizes.Max() / sizes.Sum() < 0.97)
        {
            throw new Problem("The image has more than one thing in it. Use a single hand.");
        }

        var box = Bounds(p) ?? throw new Problem("The image is empty.");
        int bw = box.MaxX - box.MinX + 1;
        int bh = box.MaxY - box.MinY + 1;
        double aspect = (double)bh / bw;
        if (aspect < 1.05 || aspect > 2.8)
        {
            throw new Problem("The hand should stand upright, taller than it is wide.");
        }
        if ((double)bh / h < 0.5)
        {
            throw new Problem("The hand is too small in the image. Crop it closer.");
        }

        int topBand = Math.Max(2, bh * 8 / 100);
        int fingerMin = w, fingerMax = 0;
        for (int y = box.MinY; y < box.MinY + topBand; y++)
        {
            for (int x = box.MinX; x <= box.MaxX; x++)
            {
                if (p.Alpha(x, y) > 128)
                {
                    fingerMin = Math.Min(fingerMin, x);
                    fingerMax = Math.Max(fingerMax, x);
                }
            }
        }
        if ((double)(fingerMax - fingerMin + 1) / bw > 0.4)
        {
            throw new Problem("The top of the image should be a single finger pointing up.");
        }

        if ((double)BottomSpan(p, box).Width / bw < 0.2)
        {
            throw new Problem("The hand should end in a flat wrist or cuff at the bottom.");
        }

        int soft = 0, any = 0;
        for (int y = 0; y < h; y += 2)
        {
            for (int x = 0; x < w; x += 2)
            {
                byte a = p.Alpha(x, y);
                if (a > 20)
                {
                    any++;
                    if (a < 200)
                    {
                        soft++;
                    }
                }
            }
        }
        if ((double)soft / Math.Max(1, any) > 0.08)
        {
            throw new Problem("The hand has a soft shadow or glow around it. Ask for no shadow.");
        }
    }

    private static Box? Bounds(Pixels p)
    {
        int minX = p.Width, minY = p.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < p.Height; y++)
        {
            for (int x = 0; x < p.Width; x++)
            {
                if (p.Alpha(x, y) > 128)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }
        return maxX < 0 ? null : new Box(minX, minY, maxX, maxY);
    }

    private static (int Width, double Center) BottomSpan(Pixels p, Box box)
    {
        int bh = box.MaxY - box.MinY + 1;
        int lo = p.Width, hi = -1;
        for (int y = Math.Max(box.MinY, box.MaxY - Math.Max(2, bh * 4 / 100)); y <= box.MaxY; y++)
        {
            for (int x = box.MinX; x <= box.MaxX; x++)
            {
                if (p.Alpha(x, y) > 128)
                {
                    lo = Math.Min(lo, x);
                    hi = Math.Max(hi, x);
                }
            }
        }
        return hi < 0 ? (0, (box.MinX + box.MaxX) / 2.0) : (hi - lo + 1, (lo + hi) / 2.0);
    }

    /// <summary>Same layout as macOS: 1024×1536 canvas, hand top at 150, a dark rounded rod below.</summary>
    private static BitmapSource? Compose(Pixels p)
    {
        if (Bounds(p) is not { } box)
        {
            return null;
        }
        const int canvasW = 1024, canvasH = 1536;
        const double handTop = 150, handHeight = 560;
        double bw = box.MaxX - box.MinX + 1;
        double bh = box.MaxY - box.MinY + 1;
        double scale = Math.Min(handHeight / bh, 700 / bw);
        double drawW = p.Width * scale;
        double drawH = p.Height * scale;
        double wristX = (BottomSpan(p, box).Center + 0.5) * scale;
        double originX = canvasW / 2.0 - wristX;
        double originTop = handTop - box.MinY * scale;
        double handBottom = handTop + bh * scale;

        double rodW = Math.Min(76, Math.Max(54, bw * scale * 0.18));
        double rodTop = handBottom - 30;
        const double rodBottom = 1462;
        var rod = new Rect(canvasW / 2.0 - rodW / 2, rodTop, rodW, rodBottom - rodTop);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x00, 0x00, 0x00), 0),
                new GradientStop(Color.FromRgb(0x12, 0x12, 0x14), 0.22),
                new GradientStop(Color.FromRgb(0x3A, 0x3A, 0x3E), 0.45),
                new GradientStop(Color.FromRgb(0x12, 0x12, 0x14), 0.75),
                new GradientStop(Color.FromRgb(0x00, 0x00, 0x00), 1),
            },
        };
        var source = BitmapSource.Create(p.Width, p.Height, 96, 96, PixelFormats.Bgra32, null, p.Bgra, p.Width * 4);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(gradient, null, rod, rodW / 2, rodW / 2);
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect(originX, originTop, drawW, drawH));
        }
        var bitmap = new RenderTargetBitmap(canvasW, canvasH, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Pick a vivid, mid-brightness colour from the hand for its ink.</summary>
    private static uint Ink(Pixels p)
    {
        (double Score, double R, double G, double B) best = (-1, 0.53, 0.33, 0.95);
        var buckets = new Dictionary<int, (int N, double R, double G, double B)>();
        for (int y = 0; y < p.Height; y += 3)
        {
            for (int x = 0; x < p.Width; x += 3)
            {
                int i = (y * p.Width + x) * 4;
                if (p.Bgra[i + 3] <= 200)
                {
                    continue;
                }
                double r = p.Bgra[i + 2] / 255.0, g = p.Bgra[i + 1] / 255.0, b = p.Bgra[i] / 255.0;
                int key = (int)(r * 5) * 36 + (int)(g * 5) * 6 + (int)(b * 5);
                var e = buckets.GetValueOrDefault(key);
                buckets[key] = (e.N + 1, e.R + r, e.G + g, e.B + b);
            }
        }
        foreach (var e in buckets.Values)
        {
            double r = e.R / e.N, g = e.G / e.N, b = e.B / e.N;
            double hi = Math.Max(r, Math.Max(g, b)), lo = Math.Min(r, Math.Min(g, b));
            double sat = hi == 0 ? 0 : (hi - lo) / hi;
            double lum = 0.3 * r + 0.59 * g + 0.11 * b;
            double score = e.N * (0.25 + sat) * (lum > 0.12 && lum < 0.85 ? 1 : 0.15);
            if (score > best.Score)
            {
                best = (score, r, g, b);
            }
        }
        double rr = best.R, gg = best.G, bb = best.B;
        double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
        if (max - min < 0.08)
        {
            (rr, gg, bb) = (0.42, 0.45, 0.5);
        }
        else if (max > 0.82)
        {
            rr *= 0.82 / max;
            gg *= 0.82 / max;
            bb *= 0.82 / max;
        }
        return (uint)Math.Min(255, rr * 255) << 16 | (uint)Math.Min(255, gg * 255) << 8 | (uint)Math.Min(255, bb * 255);
    }
}
