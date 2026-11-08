using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TinyHand;

/// <summary>
/// A hand-on-a-stick image plus where its parts are, measured from the alpha channel.
/// All vertical positions are fractions of the image height from the top.
/// </summary>
internal sealed class HandAsset
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Premultiplied BGRA, top-down rows. The overlay drops it once the GPU has a copy.</summary>
    public byte[]? Pixels { get; set; }
    public string? FilePath { get; init; }

    /// <summary>Thumbnails only need ~128 px, so decode hand images at a quarter of their 1536 px height.</summary>
    private const int ThumbnailDecodeHeight = 384;

    public double Aspect { get; init; }
    public double HandTop { get; init; }
    public double Tip { get; init; }
    public double Wrist { get; init; }
    public double RodEnd { get; init; }
    public double TipX { get; init; }
    public double RodX { get; init; }
    public double HandMinX { get; init; }
    public double HandMaxX { get; init; }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, ImageSource> Thumbnails = new();

    /// <summary>Full-resolution hand with pixels, for the overlay. Not cached: the GPU keeps the copy.</summary>
    public static HandAsset? Load(string name) => Decode(name, null);

    /// <summary>Reload pixels dropped after upload (e.g. after the graphics device was reset).</summary>
    public byte[]? ReloadPixels()
    {
        if (FilePath == null)
        {
            return null;
        }
        try
        {
            return ReadPbgra(FilePath, null).Pixels;
        }
        catch (Exception e)
        {
            Log.Write($"reload {FilePath} failed: {e.Message}");
            return null;
        }
    }

    public static void Forget(string name)
    {
        lock (Gate)
        {
            Thumbnails.Remove(name);
        }
    }

    public static ImageSource? CachedThumbnail(string name)
    {
        lock (Gate)
        {
            return Thumbnails.GetValueOrDefault(name);
        }
    }

    public static ImageSource? Thumbnail(string name)
    {
        if (CachedThumbnail(name) is { } hit)
        {
            return hit;
        }
        var thumb = Decode(name, ThumbnailDecodeHeight)?.MakeThumbnail();
        if (thumb != null)
        {
            lock (Gate)
            {
                Thumbnails[name] = thumb;
            }
        }
        return thumb;
    }

    /// <summary>Build thumbnails in the background; <paramref name="done"/> runs on the calling dispatcher.</summary>
    public static void PreloadThumbnails(IEnumerable<string> names, Action? done = null)
    {
        var missing = names.Where(n => CachedThumbnail(n) == null).ToList();
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        if (missing.Count == 0)
        {
            done?.Invoke();
            return;
        }
        Task.Run(() =>
        {
            foreach (var name in missing)
            {
                try
                {
                    var thumb = Decode(name, ThumbnailDecodeHeight)?.MakeThumbnail();
                    if (thumb != null)
                    {
                        lock (Gate)
                        {
                            Thumbnails.TryAdd(name, thumb);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Write($"thumbnail {name} failed: {e.Message}");
                }
            }
            // Decoding left large temporary buffers behind; give the memory back.
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            if (done != null)
            {
                dispatcher.BeginInvoke(done);
            }
        });
    }

    private ImageSource MakeThumbnail()
    {
        double h = Height;
        double w = Width;
        double handW = (HandMaxX - HandMinX) * w;
        double handH = (Wrist - HandTop) * h;
        double side = Math.Max(handW, handH) * 1.06;
        double cx = (HandMinX + HandMaxX) / 2 * w;
        double cy = (HandTop + Wrist) / 2 * h;
        var rect = Rect.Intersect(new Rect(cx - side / 2, cy - side / 2, side, side), new Rect(0, 0, w, h));

        var full = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, Pixels!, Width * 4);
        var cropped = new CroppedBitmap(full, new Int32Rect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
        const double px = 128;
        var scaled = new TransformedBitmap(cropped, new ScaleTransform(px / cropped.PixelWidth, px / cropped.PixelHeight));
        // Render once so the thumbnail doesn't keep the full-size pixels alive.
        var small = new WriteableBitmap(scaled);
        small.Freeze();
        return small;
    }

    public static string? FindFile(string name)
    {
        var custom = CustomHands.ImagePath(name);
        if (File.Exists(custom))
        {
            return custom;
        }
        return Paths.Asset($"{name}.png");
    }

    private static HandAsset? Decode(string name, int? decodeHeight)
    {
        var path = FindFile(name);
        if (path == null)
        {
            Log.Write($"hand image {name} not found");
            return null;
        }
        try
        {
            var (w, h, pixels) = ReadPbgra(path, decodeHeight);
            return Measure(w, h, pixels, path);
        }
        catch (Exception e)
        {
            Log.Write($"hand image {name} unreadable: {e.Message}");
            return null;
        }
    }

    public static (int Width, int Height, byte[] Pixels) ReadPbgra(string path, int? decodeHeight)
    {
        using var stream = File.OpenRead(path);
        BitmapSource frame;
        if (decodeHeight is { } dh)
        {
            // WIC scales while decoding, so the full-size image never exists in memory.
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.DecodePixelHeight = dh;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            frame = image;
        }
        else
        {
            frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        var pixels = new byte[w * h * 4];
        converted.CopyPixels(pixels, w * 4, 0);
        return (w, h, pixels);
    }

    private static HandAsset? Measure(int w, int h, byte[] pixels, string? path)
    {
        bool Solid(int x, int y) => pixels[(y * w + x) * 4 + 3] > 128;

        var counts = new int[h];
        for (int y = 0; y < h; y++)
        {
            int n = 0;
            for (int x = 0; x < w; x++)
            {
                if (Solid(x, y))
                {
                    n++;
                }
            }
            counts[y] = n;
        }

        double Center(int y)
        {
            int lo = -1, hi = -1;
            for (int x = 0; x < w; x++)
            {
                if (Solid(x, y))
                {
                    if (lo < 0)
                    {
                        lo = x;
                    }
                    hi = x;
                }
            }
            return lo < 0 ? 0.5 : (lo + hi) / 2.0 / w;
        }

        int top = Array.FindIndex(counts, c => c > 0);
        if (top < 0)
        {
            return null;
        }
        int searchEnd = Math.Min(h, top + (int)(0.45 * h));
        int maxW = 0;
        for (int y = top; y < searchEnd; y++)
        {
            maxW = Math.Max(maxW, counts[y]);
        }
        if (maxW == 0)
        {
            return null;
        }
        int fist = -1;
        for (int y = top; y < h; y++)
        {
            if (counts[y] > 0.6 * maxW)
            {
                fist = y;
                break;
            }
        }
        if (fist < 0)
        {
            return null;
        }
        int wrist = -1;
        for (int y = fist; y < h; y++)
        {
            if (counts[y] < 0.35 * maxW)
            {
                wrist = y;
                break;
            }
        }
        if (wrist < 0)
        {
            return null;
        }
        int rodW = counts[Math.Min(h - 1, wrist + h / 50)];
        int lastRow = Array.FindLastIndex(counts, c => c > 0);
        if (lastRow < 0)
        {
            lastRow = h - 1;
        }
        int from = Math.Min(h - 1, wrist + h / 33);
        int rodEnd = lastRow;
        for (int y = from; y < h; y++)
        {
            if (counts[y] > 1.8 * rodW)
            {
                rodEnd = y;
                break;
            }
        }

        int minX = w, maxX = 0;
        for (int y = top; y < wrist; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (Solid(x, y))
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }
        }

        double fh = h;
        return new HandAsset
        {
            Width = w,
            Height = h,
            Pixels = pixels,
            FilePath = path,
            Aspect = w / fh,
            HandTop = Math.Max(0, top - 0.004 * fh) / fh,
            Tip = top / fh,
            Wrist = wrist / fh,
            RodEnd = rodEnd / fh,
            TipX = Center(Math.Min(h - 1, top + h / 200)),
            RodX = Center((wrist + rodEnd) / 2),
            HandMinX = minX / (double)w,
            HandMaxX = maxX / (double)w,
        };
    }
}
