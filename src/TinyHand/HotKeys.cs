using TinyHand.Native;

namespace TinyHand;

/// <summary>Global hotkeys via RegisterHotKey on the app's message window.</summary>
internal sealed class HotKeys
{
    private readonly MessageWindow window;
    private readonly Dictionary<int, Action> handlers = new();
    private int nextId = 1;

    public HotKeys(MessageWindow window)
    {
        this.window = window;
        window.On(Win32.WM_HOTKEY, (wParam, _) =>
        {
            if (handlers.TryGetValue(wParam.ToInt32(), out var action))
            {
                action();
            }
            return IntPtr.Zero;
        });
    }

    /// <summary>Returns an id to unregister with, or null when another app already owns the combination.</summary>
    public int? Register(uint vk, uint modifiers, Action action)
    {
        int id = nextId++;
        if (!Win32.RegisterHotKey(window.Handle, id, modifiers | Win32.MOD_NOREPEAT, vk))
        {
            Log.Write($"hotkey vk=0x{vk:X} mods=0x{modifiers:X} unavailable (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
            return null;
        }
        handlers[id] = action;
        return id;
    }

    public void Unregister(int id)
    {
        Win32.UnregisterHotKey(window.Handle, id);
        handlers.Remove(id);
    }
}
