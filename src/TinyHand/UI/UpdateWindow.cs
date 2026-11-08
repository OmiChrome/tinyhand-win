using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TinyHand.Native;

namespace TinyHand.UI;

/// <summary>Small window that shows update progress: checking, downloading (with a bar), ready, or a message.</summary>
internal sealed class UpdateWindow : Window
{
    public event Action? RestartClicked;

    private readonly TextBlock title = Ui.Text("", 16, weight: FontWeights.SemiBold);
    private readonly TextBlock detail = Ui.Text("", 13, Ui.TextSecondary);
    private readonly ProgressBar bar = new() { Height = 4, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 14, 0, 0) };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };

    public UpdateWindow()
    {
        Title = "TinyHand update";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Icon = Icons.AppIcon;
        FontFamily = Ui.Font;
        Theme.ApplyWindow(this);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Ui.Picture(Icons.AppIcon, 48);
        icon.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(16, 2, 0, 0) };
        text.Children.Add(title);
        detail.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(detail);
        text.Children.Add(bar);
        text.Children.Add(buttons);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        Content = new Border { Padding = new Thickness(24, 22, 24, 20), Child = row };

        Closing += (_, e) =>
        {
            // Hiding keeps a download going in the background.
            e.Cancel = true;
            Hide();
        };
    }

    public void ShowChecking() => Set("Checking for updates…", $"You have TinyHand {AppController.Version}.", progress: -1);

    public void ShowDownloading(string version, int percent) =>
        Set($"Downloading TinyHand {version}", percent > 0 ? $"{percent}% downloaded. You can keep working." : "Starting the download…", progress: percent);

    public void ShowReady(string version)
    {
        Set($"TinyHand {version} is ready", "Restart TinyHand to finish updating. If you choose Later, it updates the next time TinyHand quits.", progress: null);
        AddButton("Later", Hide, accent: false);
        AddButton("Restart now", () => RestartClicked?.Invoke(), accent: true);
    }

    public void ShowMessage(string heading, string message)
    {
        Set(heading, message, progress: null);
        AddButton("OK", Hide, accent: true);
    }

    private void Set(string heading, string message, int? progress)
    {
        title.Text = heading;
        detail.Text = message;
        buttons.Children.Clear();
        buttons.Visibility = Visibility.Collapsed;
        bar.Visibility = progress == null ? Visibility.Collapsed : Visibility.Visible;
        bar.IsIndeterminate = progress < 0;
        if (progress >= 0)
        {
            bar.Value = progress.Value;
        }
    }

    private void AddButton(string text, Action action, bool accent)
    {
        var b = Ui.Button(text, action, accent);
        b.Margin = new Thickness(8, 0, 0, 0);
        if (accent)
        {
            b.IsDefault = true;
        }
        buttons.Children.Add(b);
        buttons.Visibility = Visibility.Visible;
    }

    /// <summary>Show and bring to the front (also after a long background download).</summary>
    public void ShowOnTop()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        // Briefly topmost so it surfaces even when Windows won't hand us the foreground.
        Topmost = true;
        Activate();
        Win32.SetForegroundWindow(hwnd);
        Topmost = false;
    }
}
