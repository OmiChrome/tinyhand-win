using System.Runtime.InteropServices;
using TinyHand.Native;

namespace TinyHand;

/// <summary>Plays the short tap sound. WAVs are loaded once into unmanaged memory for PlaySound.</summary>
internal sealed class TapSound
{
    private static readonly Dictionary<TapSoundChoice, IntPtr> Loaded = new();
    private static readonly object Gate = new();

    private volatile int choice;

    public volatile bool Enabled = true;

    public TapSound(TapSoundChoice choice)
    {
        this.choice = (int)choice;
    }

    public TapSoundChoice Choice
    {
        get => (TapSoundChoice)choice;
        set => choice = (int)value;
    }

    /// <summary>Load the WAV now so the first tap isn't late.</summary>
    public void Warm() => Get(Choice);

    public void Play()
    {
        if (!Enabled)
        {
            return;
        }
        var data = Get(Choice);
        if (data != IntPtr.Zero)
        {
            // Off the caller's thread: opening the audio device can take a moment the first time.
            ThreadPool.UnsafeQueueUserWorkItem(_ => Win32.PlaySound(data, IntPtr.Zero,
                Win32.SND_MEMORY | Win32.SND_ASYNC | Win32.SND_NODEFAULT), null);
        }
    }

    private static IntPtr Get(TapSoundChoice c)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(c, out var hit))
            {
                return hit;
            }
            var path = Paths.Asset(Path.Combine("sounds", c.FileName()));
            var ptr = IntPtr.Zero;
            if (path != null)
            {
                var bytes = File.ReadAllBytes(path);
                // Must not move while the sound plays asynchronously, so it lives outside the GC heap.
                ptr = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
            }
            else
            {
                Log.Write($"sound {c.FileName()} not found");
            }
            Loaded[c] = ptr;
            return ptr;
        }
    }
}
