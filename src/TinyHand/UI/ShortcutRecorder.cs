using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TinyHand.Native;

namespace TinyHand.UI;

/// <summary>Click, then press a new key combination. Esc cancels. Shows the keys as keycaps.</summary>
internal sealed class ShortcutRecorder : Border
{
    public Func<Shortcut, bool>? Changed;
    public event Action<bool>? RecordingChanged;

    private readonly StackPanel keys = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private Shortcut shortcut;
    private bool recording;

    public ShortcutRecorder(Shortcut shortcut)
    {
        this.shortcut = shortcut;
        Width = 160;
        Height = 32;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        ToolTip = "Click, then press the new shortcut. Esc cancels.";
        Child = keys;
        keys.VerticalAlignment = VerticalAlignment.Center;
        this.Res(BackgroundProperty, Ui.ControlFill).Res(BorderBrushProperty, Ui.ControlStroke);
        SetLabel(shortcut.Display);

        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Start();
        };
        PreviewKeyDown += OnKey;
        LostKeyboardFocus += (_, _) => Stop();
    }

    public void Show(Shortcut s)
    {
        if (recording || s == shortcut)
        {
            return;
        }
        shortcut = s;
        SetLabel(s.Display);
    }

    private void Start()
    {
        if (recording)
        {
            return;
        }
        recording = true;
        Focus();
        Keyboard.Focus(this);
        this.Res(BorderBrushProperty, Ui.Accent);
        SetLabel("Press keys", dim: true);
        RecordingChanged?.Invoke(true);
    }

    private void Stop()
    {
        if (!recording)
        {
            return;
        }
        recording = false;
        this.Res(BorderBrushProperty, Ui.ControlStroke);
        SetLabel(shortcut.Display);
        RecordingChanged?.Invoke(false);
    }

    private static readonly HashSet<Key> ModifierKeys = new()
    {
        Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin, Key.System,
    };

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!recording)
        {
            return;
        }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (key == Key.Escape && mods == System.Windows.Input.ModifierKeys.None)
        {
            Stop();
            return;
        }
        if (ModifierKeys.Contains(key))
        {
            var held = Describe(mods, win);
            SetLabel(held.Length == 0 ? "Press keys" : held, dim: held.Length == 0);
            return;
        }
        bool hasCombo = (mods & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt)) != 0 || win;
        if (!hasCombo)
        {
            SetLabel("Add Ctrl, Alt or Win", dim: true);
            return;
        }
        uint m = 0;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) m |= Win32.MOD_CONTROL;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) m |= Win32.MOD_ALT;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) m |= Win32.MOD_SHIFT;
        if (win) m |= Win32.MOD_WIN;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var s = new Shortcut(vk, m, Describe(mods, win) + KeyName(key));
        if (Changed?.Invoke(s) == true)
        {
            shortcut = s;
        }
        Stop();
    }

    private static string Describe(System.Windows.Input.ModifierKeys mods, bool win)
    {
        var parts = new List<string>();
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) parts.Add("Shift");
        if (win) parts.Add("Win");
        return parts.Count == 0 ? "" : string.Join("+", parts) + "+";
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
        Key.Space => "Space",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        Key.PageUp => "PgUp",
        Key.Next => "PgDn",
        _ => key.ToString(),
    };

    private void SetLabel(string text, bool dim = false)
    {
        keys.Children.Clear();
        if (dim)
        {
            keys.Children.Add(Ui.Text(text, 12, Ui.TextSecondary));
            return;
        }
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var cap = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Padding = new Thickness(6, 0, 6, 1),
                Margin = new Thickness(2, 0, 2, 0),
                MinWidth = 22,
                Height = 22,
                Child = new TextBlock
                {
                    Text = part,
                    FontSize = 12,
                    FontFamily = Ui.Font,
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                }.Res(TextBlock.ForegroundProperty, Ui.TextPrimary),
            }.Res(Border.BackgroundProperty, Ui.SubtleHover).Res(Border.BorderBrushProperty, Ui.ControlStroke);
            keys.Children.Add(cap);
        }
    }
}
