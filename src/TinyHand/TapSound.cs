using System.Runtime.InteropServices;
using Vortice.XAudio2;

namespace TinyHand;

/// <summary>
/// Plays the short tap sound through XAudio2, kept running ("warm") while it may be needed, like the
/// macOS app keeps its AVAudioEngine running. PlaySound opened the device from cold on every tap, and
/// Windows dropped the start of the sound; the 50 ms "click" was lost completely.
/// </summary>
internal sealed class TapSound : IDisposable
{
    private const int Voices = 4;               // taps in quick succession overlap instead of cutting off
    private static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<TapSoundChoice, (IntPtr Data, int Bytes)> Loaded = new();
    private static readonly object LoadGate = new();

    private readonly object gate = new();
    private readonly Timer idleTimer;
    private IXAudio2? engine;
    private IXAudio2MasteringVoice? master;
    private readonly IXAudio2SourceVoice?[] voices = new IXAudio2SourceVoice?[Voices];
    private int next;
    private bool held;
    private volatile int choice;

    public volatile bool Enabled = true;

    public TapSound(TapSoundChoice choice)
    {
        this.choice = (int)choice;
        idleTimer = new Timer(_ => ReleaseIfIdle(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TapSoundChoice Choice
    {
        get => (TapSoundChoice)choice;
        set => choice = (int)value;
    }

    /// <summary>Open the audio output now so the first tap plays in full.</summary>
    public void Warm() => ThreadPool.UnsafeQueueUserWorkItem(_ =>
    {
        lock (gate)
        {
            Ensure();
            ArmIdleTimer();
        }
    }, null);

    /// <summary>While held (the pointer is on) the output stays open; released 30 s after letting go.</summary>
    public void Hold(bool value)
    {
        lock (gate)
        {
            held = value;
            ArmIdleTimer();
        }
        if (value)
        {
            Warm();
        }
    }

    public void Play()
    {
        if (!Enabled)
        {
            return;
        }
        var c = Choice;
        // Never block the caller (the mouse hook must return quickly).
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            var (data, bytes) = Get(c);
            if (data == IntPtr.Zero)
            {
                return;
            }
            lock (gate)
            {
                try
                {
                    Ensure();
                    var voice = voices[next];
                    next = (next + 1) % Voices;
                    if (voice == null)
                    {
                        return;
                    }
                    voice.Stop();
                    voice.FlushSourceBuffers();
                    voice.SubmitSourceBuffer(new AudioBuffer
                    {
                        AudioDataPointer = data,
                        AudioBytes = (uint)bytes,
                        Flags = BufferFlags.EndOfStream,
                    });
                    voice.Start();
                    ArmIdleTimer();
                }
                catch (Exception e)
                {
                    // e.g. the audio device was unplugged: start over on the next tap.
                    Log.Write($"tap sound failed: {e.Message}");
                    ReleaseEngine();
                }
            }
        }, null);
    }

    private void Ensure()
    {
        if (engine != null)
        {
            return;
        }
        try
        {
            engine = XAudio2.XAudio2Create();
            master = engine.CreateMasteringVoice();
            // All tap sounds are 44.1 kHz 16-bit mono PCM.
            var format = new Vortice.Multimedia.WaveFormat(44100, 16, 1);
            for (int i = 0; i < Voices; i++)
            {
                voices[i] = engine.CreateSourceVoice(format);
            }
        }
        catch (Exception e)
        {
            Log.Write($"audio output unavailable: {e.Message}");
            ReleaseEngine();
        }
    }

    private void ArmIdleTimer() => idleTimer.Change(held ? Timeout.InfiniteTimeSpan : IdleRelease, Timeout.InfiniteTimeSpan);

    private void ReleaseIfIdle()
    {
        lock (gate)
        {
            if (!held)
            {
                ReleaseEngine();
            }
        }
    }

    private void ReleaseEngine()
    {
        for (int i = 0; i < Voices; i++)
        {
            try
            {
                voices[i]?.DestroyVoice();
            }
            catch
            {
                // Already gone with the device.
            }
            voices[i] = null;
        }
        master?.Dispose();
        master = null;
        engine?.Dispose();
        engine = null;
    }

    /// <summary>The PCM samples of a tap WAV (data chunk only), loaded once into unmanaged memory.</summary>
    private static (IntPtr Data, int Bytes) Get(TapSoundChoice c)
    {
        lock (LoadGate)
        {
            if (Loaded.TryGetValue(c, out var hit))
            {
                return hit;
            }
            (IntPtr, int) result = (IntPtr.Zero, 0);
            var path = Paths.Asset(Path.Combine("sounds", c.FileName()));
            if (path != null && DataChunk(File.ReadAllBytes(path)) is { } pcm)
            {
                var ptr = Marshal.AllocHGlobal(pcm.Length);
                Marshal.Copy(pcm, 0, ptr, pcm.Length);
                result = (ptr, pcm.Length);
            }
            else
            {
                Log.Write($"sound {c.FileName()} missing or unreadable");
            }
            Loaded[c] = result;
            return result;
        }
    }

    private static byte[]? DataChunk(byte[] wav)
    {
        for (int i = 12; i + 8 <= wav.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, i, 4);
            int size = BitConverter.ToInt32(wav, i + 4);
            if (id == "data")
            {
                return wav.AsSpan(i + 8, Math.Min(size, wav.Length - i - 8)).ToArray();
            }
            i += 8 + size + (size & 1);
        }
        return null;
    }

    public void Dispose()
    {
        idleTimer.Dispose();
        lock (gate)
        {
            ReleaseEngine();
        }
    }
}
