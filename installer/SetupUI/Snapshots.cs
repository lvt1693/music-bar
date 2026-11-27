using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicBar.Setup;

/// <summary>
/// Tests: every screen, in both languages, drawn into PNGs (1.5x, over a desktop grey) without opening a window
/// or installing anything. MusicBarSetup.exe /SNAPSHOTS=folder
/// </summary>
static class Snapshots
{
    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var old = new Engine.Installed { Version = "1.8.6", Dir = Engine.DefaultDir };
        var same = new Engine.Installed { Version = Engine.Version, Dir = Engine.DefaultDir };
        foreach (string lang in new[] { "vi", "en" })
        {
            Snap(folder, $"{lang}-1-welcome", Flow.Install, lang, w => w.Preview(Screen.Welcome));
            Snap(folder, $"{lang}-2-options", Flow.Install, lang, w => w.Preview(Screen.Options));
            Snap(folder, $"{lang}-3-installing", Flow.Install, lang, w => w.Preview(Screen.Progress, progress: 0.62, busy: true));
            Snap(folder, $"{lang}-4-done", Flow.Install, lang, w => w.Preview(Screen.Done, progress: 1, succeeded: true));
            Snap(folder, $"{lang}-5-update", Flow.Update, lang, w => w.Preview(Screen.Update, installed: old));
            Snap(folder, $"{lang}-5b-reinstall", Flow.Update, lang, w => w.Preview(Screen.Update, installed: same));
            Snap(folder, $"{lang}-6-updating", Flow.QuietUpdate, lang, w => w.Preview(Screen.Progress, progress: 0.35, installed: old, busy: true));
            Snap(folder, $"{lang}-7-updated", Flow.Update, lang, w => w.Preview(Screen.Done, progress: 1, succeeded: true, reopened: true, installed: old));
            Snap(folder, $"{lang}-8-error", Flow.Install, lang, w => w.Preview(Screen.Error, progress: 0.38, error: 4));
            Snap(folder, $"{lang}-9-uninstall", Flow.Uninstall, lang, w => w.Preview(Screen.Uninstall));
            Snap(folder, $"{lang}-10-uninstalling", Flow.Uninstall, lang, w => w.Preview(Screen.Progress, progress: 0.5, busy: true));
            Snap(folder, $"{lang}-11-goodbye", Flow.Uninstall, lang, w => w.Preview(Screen.Done, progress: 1, succeeded: true));
            Snap(folder, $"{lang}-12-uninstall-error", Flow.Uninstall, lang, w => w.Preview(Screen.Error, error: 2));
        }
    }

    static void Snap(string folder, string name, Flow flow, string lang, Action<MainWindow> setup)
    {
        var window = new MainWindow(flow, new Request(), lang, flow == Flow.Uninstall ? @"C:\Example\MusicBar" : null);
        setup(window);
        var root = (FrameworkElement)window.Content;
        var size = new Size(window.Width, window.Height);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        setup(window);
        root.UpdateLayout();

        const double scale = 1.5;
        var shot = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        shot.Render(root);
        var desk = new DrawingVisual();
        using (var dc = desk.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x1B)), null, new Rect(size));
            dc.DrawImage(shot, new Rect(size));
        }
        var final = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        final.Render(desk);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(final));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        png.Save(file);
        window.Close();
    }
}
