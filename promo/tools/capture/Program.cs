using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Models;
using MusicBar.Services;
using MusicBar.Views;
using MusicBar.Views.Face;

// Renders MusicBar's own UI to PNG frames with transparency, for the promo video.
//
//   seek   <style> <out> <seconds> <wDip> <hDip> <scale> <progressFrom> <progressTo> <#colorA> <#colorB>
//          the real SeekRenderer, frame-exact (its clock is driven per frame)
//   lyrics <out> <seconds> <font> <#text> <#stroke> <strokeOpacity> <strokeRadius> <wDip> <hDip> <scale> <sizeDip>
//          the real LyricsPresenter singing the promo's lines, captured in real time at 30 fps
//          (<font> is a family name, or "bundled:<Name>" for one of MusicBar's own fonts)
//   panel  <out> <scale>
//          the real control center panel: open, and with the playback-device list slid out
//   assets <out>
//          the app icon and the demo cover art as PNG
//   settings <outDir> <English|Vietnamese> <scale>
//          the real settings window, one PNG per page, in the given language
//   controls <out.png> <taskbarHeightDip> <scale>
//          the real widget's previous / play / next buttons in every control style, paused and playing
//   props  <outDir> <cellHeight> [scale]
//          what the taskbar face wears (hats, glasses, headphones, mic, timer) on every face set,
//          and the newer signs, on a dark and a light taskbar
static class Program
{
    const double Fps = 30;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string FontsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        @"..\..\..\..\..\..\src\MusicBar\Assets\Fonts\"));

    // The lines the lyrics engine sings in the film (LRC, parsed by the app's own parser).
    const string PromoLrc = """
        [00:00.40]Taskbar ơi, dậy đi thôi
        [00:03.20]Hôm nay em được hát rồi
        [00:06.00]Lời chạy theo nhạc từng chữ một
        [00:08.80]Đổi màu, đổi phông, vẫn đúng dấu
        [00:11.60]
        """;

    [STAThread]
    static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // MusicBar's shared resources (icon font, button styles), as its App.xaml merges them.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MusicBar;component/Themes/Bar.xaml"),
        });
        int code = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                switch (args[0])
                {
                    case "seek": Seek(args); break;
            case "looks": await Looks(args); break;
            case "gaps": await Looks(args, gaps: true); break;
            case "leds": await Looks(args, styles: SettingsOptions.LedStrips.Select(o => o.Value).ToArray()); break;
            case "reset": ResetCheck(); break;
            case "welcome": await WelcomeTour(args); break;
            case "defaults":
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new AppSettings(), new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                }));
                break;
            case "settingsscroll": await SettingsScroll(args); break;
                    case "lyrics": await Lyrics(args); break;
                    case "panel": await Panel(args); break;
                    case "assets": Assets(args); break;
                    case "controls": await ControlStyles(args); break;
                    case "settings": await SettingsPages(args); break;
                    case "wireless": await Wireless(); break;
                    case "faces": Faces(args); break;
                    case "facelife": await FaceLife(args); break;
                    case "props": Props(args); break;
                    case "download": await Download(args); break;
                    case "clip": await Clip(args); break;
                    default: throw new ArgumentException("unknown mode " + args[0]);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                code = 1;
            }
            app.Shutdown();
        };
        app.Run();
        return code;
    }

    /// <summary>
    /// clip &lt;in.json|in.hlsl&gt; &lt;outDir&gt; &lt;seconds&gt; &lt;w&gt; &lt;h&gt; [#A #B #C]: a gallery item's preview as frames, drawn by
    /// the app's own code: a preset through the engine, a skin as its shader on the GPU over the engine's
    /// made-up music. Grabbed from an off-screen window (a shader only draws on the graphics card).
    /// </summary>
    static async Task Clip(string[] a)
    {
        string input = Path.GetFullPath(a[1]);
        string outDir = Directory.CreateDirectory(a[2]).FullName;
        double seconds = D(a[3]), w = D(a[4]), h = D(a[5]);
        double time = 0;
        var engine = new SeekRenderer { Width = w, Height = h, Clock = () => time };
        engine.SetEdge(true);
        SkinView? view = null;
        var host = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)) };
        if (input.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var preset = LookPresets.Parse(File.ReadAllText(input), Path.GetFileName(input)) ?? throw new InvalidDataException("not a preset");
            engine.Look = preset.Style;
            engine.Intensity = preset.Intensity;
            engine.Direction = preset.Direction;
            engine.SetColors(preset.Colors[0], preset.Colors[1], animate: false, preset.Colors[2]);
            host.Children.Add(engine);
        }
        else
        {
            var text = File.ReadAllText(input);
            var bytecode = SkinLibrary.Compile(text, out var error) ?? throw new InvalidDataException(error);
            bool visualizer = text.Contains("kind: visualizer", StringComparison.OrdinalIgnoreCase);
            engine.Look = visualizer ? SeekStyle.Equalizer : SeekStyle.Classic;
            engine.Skinned = true;
            engine.Visibility = Visibility.Hidden;
            engine.SetColors(C(a.Length > 6 ? a[6] : "#FF5F6D"), C(a.Length > 7 ? a[7] : "#47A9FF"), animate: false, C(a.Length > 8 ? a[8] : "#FFC371"));
            view = new SkinView();
            view.SetSkin(new SkinInfo(Path.GetFileName(input), "", "", "", visualizer, bytecode, null));
            host.Children.Add(engine);
            host.Children.Add(view);
        }
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight, Content = host,
        };
        window.Show();
        int frames = (int)Math.Round(seconds * Fps);
        for (int i = 0; i < frames; i++)
        {
            time = 20 + i / Fps;
            engine.SetState(0.18 + 0.64 * i / Math.Max(1, frames - 1), timeline: true, playing: true);
            engine.Tick();
            view?.Update(engine);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(30);
            SavePng(Grab(window, 1), Path.Combine(outDir, $"f{i:0000}.png"));
        }
        window.Close();
        Console.WriteLine($"{Path.GetFileName(input)}: {frames} frames → {outDir}");
    }

    static double D(string s) => double.Parse(s, Inv);

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>An invisible window far off screen, so elements get real layout, effects and animation ticks.</summary>
    static Window Stage(UIElement content, double w, double h)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000,
            Width = w, Height = h, Content = content,
        };
        window.Show();
        return window;
    }

    /// <summary>
    /// The element as an image. Drawn through a brush pinned to the element's own box: rendering
    /// the element directly would include its layout offset (margins) and crop the far edges.
    /// </summary>
    static BitmapSource Snap(FrameworkElement e, double scale)
    {
        e.UpdateLayout();
        double w = e.ActualWidth, h = e.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(e)
            {
                Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap((int)Math.Round(w * scale), (int)Math.Round(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    // ---------------------------------------------------------------- visualizers and skins

    /// <summary>
    /// looks &lt;out.png&gt; &lt;scale&gt; [skin.hlsl ...]: a sheet of the visualizers, each after two
    /// seconds of made-up music, then each skin given. The visualizers are drawn offscreen; a
    /// skin's shader only runs on the graphics card, so the skins are shown in a window off the
    /// screen and grabbed from it with PrintWindow.
    /// gaps &lt;out.png&gt; &lt;scale&gt; [skin.hlsl ...]: the same kept clear of made-up icons (grey), the
    /// way "Only in the empty space" lays the bar between a taskbar's icons.
    /// leds &lt;out.png&gt; &lt;scale&gt;: the RGB LED strip's effects.
    /// </summary>
    static async Task Looks(string[] a, bool gaps = false, SeekStyle[]? styles = null)
    {
        string outPath = Path.GetFullPath(a[1]);
        double scale = D(a[2]);
        const double w = 960, h = 40, rowH = 60, labelW = 150;
        Color ca = C("#FF5F6D"), cb = C("#47A9FF"), cc = C("#FFC371");
        var rows = new List<(string Label, BitmapSource Image)>();
        // Centred app icons and the tray at the right end, as on Windows 11.
        (double Left, double Right)[] icons = gaps ? [(400, 560), (850, 960)] : [];
        (double Left, double Right)[] pieces = gaps ? [(0, 394), (566, 844)] : [];
        styles ??= gaps
            ? new[] { SeekStyle.Classic, SeekStyle.Spectrum, SeekStyle.Aurora, SeekStyle.Liquid, SeekStyle.Equalizer, SeekStyle.Oscilloscope, SeekStyle.Led }
            : new[] { SeekStyle.Equalizer, SeekStyle.Horizon, SeekStyle.Oscilloscope, SeekStyle.Aura, SeekStyle.Fireflies, SeekStyle.Led, SeekStyle.Spectrum };

        foreach (var style in styles)
        {
            double time = 0;
            var seek = new SeekRenderer { Look = style, Intensity = 0.6, Width = w, Height = h, Clock = () => time };
            seek.SetEdge(true);
            seek.SetColors(ca, cb, animate: false, cc);
            seek.SetPieces(pieces);
            var stage = Stage(seek, w, h);
            stage.UpdateLayout();
            for (int i = 0; i < 120; i++)
            {
                time = 10 + i / 60.0;
                seek.SetState(0.6, timeline: true, playing: true);
                seek.Tick();
            }
            seek.InvalidateVisual();
            rows.Add((style.ToString(), Snap(seek, scale)));
            stage.Close();
        }

        foreach (var path in a.Skip(3))
        {
            var skin = SkinLibrary.Load(Path.GetFileName(path)) ?? throw new FileNotFoundException("Copy the skin into the Skins folder first", path);
            double time = 0;
            var engine = new SeekRenderer { Look = skin.Visualizer ? SeekStyle.Equalizer : SeekStyle.Classic, Skinned = true, Visibility = Visibility.Hidden, Clock = () => time };
            engine.SetEdge(true);
            engine.SetColors(ca, cb, animate: false, cc);
            var view = new SkinView();
            view.SetSkin(skin);
            engine.SetPieces(pieces);
            view.SetPieces(pieces);
            var grid = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)) };
            grid.Children.Add(engine);
            grid.Children.Add(view);
            var window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight, Content = grid,
            };
            window.Show();
            for (int i = 0; i < 120; i++)
            {
                time = 10 + i / 60.0;
                engine.SetState(0.6, timeline: true, playing: true);
                engine.Tick();
                view.Update(engine);
            }
            for (int i = 0; i < 20; i++)
            {
                time += 1 / 60.0;
                engine.Tick();
                view.Update(engine);
                await Task.Delay(100);
            }
            rows.Add((skin.Name, Grab(window, scale)));
            window.Close();
        }

        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x21)), null, new Rect(0, 0, labelW + w + 20, rowH * rows.Count));
            var face = new Typeface("Segoe UI");
            for (int i = 0; i < rows.Count; i++)
            {
                double y = rowH * i + (rowH - h) / 2;
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)), null, new Rect(labelW, y, w, h));
                dc.DrawImage(rows[i].Image, new Rect(labelW, y, w, h));
                foreach (var (left, right) in icons)
                    dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xB0, 0xB0, 0xB8)), 1), new Rect(labelW + left + 2, y + 6, right - left - 4, h - 12), 4, 4);
                dc.DrawText(new FormattedText(rows[i].Label, Inv, FlowDirection.LeftToRight, face, 14, Brushes.White, 1.0), new Point(14, rowH * i + rowH / 2 - 10));
            }
        }
        var rtb = new RenderTargetBitmap((int)((labelW + w + 20) * scale), (int)(rowH * rows.Count * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(sheet);
        SavePng(rtb, outPath);
        Console.WriteLine($"{rows.Count} looks → {outPath}");
    }

    /// <summary>
    /// settingsscroll &lt;out-dir&gt; &lt;language&gt; &lt;scale&gt; &lt;page&gt; &lt;offset&gt;...: one settings page
    /// scrolled to each offset, in a window off the screen grabbed from the graphics card (so the
    /// skins' shaders show too).
    /// </summary>
    static async Task SettingsScroll(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        var language = Enum.Parse<AppLanguage>(a[2], true);
        double scale = D(a[3]);
        int page = int.Parse(a[4], Inv);
        var settings = new AppSettings { Language = language };
        // MUSICBAR_CAPTURE_STYLE picks the look that's on, and so the gallery tab that opens.
        if (Enum.TryParse<SeekStyle>(Environment.GetEnvironmentVariable("MUSICBAR_CAPTURE_STYLE"), true, out var style)) settings.SeekStyle = style;
        MusicBar.Infrastructure.Loc.Instance.Language = language;
        var window = new SettingsWindow(settings, new MediaService(settings), new LyricsService(settings))
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1100, Height = 1000,
            ShowActivated = false,
        };
        window.Show();
        window.Page = page;
        await Task.Delay(800);
        var scroller = (ScrollViewer)window.FindName("PageScroll");
        foreach (var offset in a.Skip(5).Select(D))
        {
            scroller.ScrollToVerticalOffset(offset);
            await Task.Delay(900);
            SavePng(Grab(window, scale), Path.Combine(outDir, $"page{page}-{offset:0}.png"));
        }
        window.Close();
        Console.WriteLine($"page {page} → {outDir}");
    }

    /// <summary>
    /// welcome &lt;out-dir&gt; &lt;language&gt; [skip]: the welcome tour off the screen, each step saved as
    /// stepN.png (with "skip", the end as reached by skipping).
    /// </summary>
    static async Task WelcomeTour(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        var language = Enum.Parse<AppLanguage>(a[2], true);
        var settings = new AppSettings { Language = language };
        MusicBar.Infrastructure.Loc.Instance.Language = language;
        var media = new MediaService(settings);
        var window = new OnboardingWindow(settings, media, () => { })
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false,
        };
        window.Show();
        var go = typeof(OnboardingWindow).GetMethod("Go", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var skip = typeof(OnboardingWindow).GetMethod("Skip", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int steps = a.Length > 3 && a[3] == "skip" ? 1 : 8;
        for (int i = 0; i < steps; i++)
        {
            if (steps == 1) skip.Invoke(window, null);
            else go.Invoke(window, [i]);
            await Task.Delay(1400);
            var rtb = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(window);
            SavePng(rtb, Path.Combine(outDir, steps == 1 ? "skipped.png" : $"step{i}.png"));
        }
        window.Close();
        Console.WriteLine($"welcome → {outDir}");
    }

    /// <summary>reset: changes options (some of them kept on a reset), resets, and prints what each one is after.</summary>
    static void ResetCheck()
    {
        var s = new AppSettings
        {
            SeekStyle = SeekStyle.LedFire, FillOpacity = 0.8, HideWhenIdle = false, SeekAvoidIcons = true, ReadVlc = true,
            LyricsOffsetMs = 750, Language = AppLanguage.Vietnamese, ProxyUrl = "http://proxy:3128", SkippedVersion = "9.9.9", WindowsModeBefore = 1,
        };
        s.Palettes["Spectrum"] = "preset:neon";
        int changes = 0;
        s.PropertyChanged += (_, _) => changes++;
        s.ResetToDefaults();
        var d = new AppSettings();
        Console.WriteLine($"{changes} change notices");
        Console.WriteLine($"reset  SeekStyle {s.SeekStyle} (default {d.SeekStyle}), FillOpacity {s.FillOpacity} ({d.FillOpacity}), HideWhenIdle {s.HideWhenIdle} ({d.HideWhenIdle})");
        Console.WriteLine($"reset  SeekAvoidIcons {s.SeekAvoidIcons} ({d.SeekAvoidIcons}), ReadVlc {s.ReadVlc} ({d.ReadVlc}), LyricsOffsetMs {s.LyricsOffsetMs} ({d.LyricsOffsetMs}), Palettes {s.Palettes.Count} ({d.Palettes.Count})");
        Console.WriteLine($"kept   Language {s.Language}, ProxyUrl {s.ProxyUrl}, SkippedVersion {s.SkippedVersion}, WindowsModeBefore {s.WindowsModeBefore}");
    }

    /// <summary>What a window shows on the graphics card, as Windows composes it (PrintWindow with full content).</summary>
    static BitmapSource Grab(Window window, double scale)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var r);
        int pw = r.Right - r.Left, ph = r.Bottom - r.Top;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, pw, ph);
        var old = SelectObject(memory, bitmap);
        PrintWindow(hwnd, memory, 2);
        SelectObject(memory, old);
        var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        DeleteObject(bitmap);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
        source.Freeze();
        return source;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct WinRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out WinRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    // ---------------------------------------------------------------- seekbar

    static void Seek(string[] a)
    {
        var style = Enum.Parse<SeekStyle>(a[1], true);
        string outDir = Directory.CreateDirectory(a[2]).FullName;
        double seconds = D(a[3]), w = D(a[4]), h = D(a[5]), scale = D(a[6]), p0 = D(a[7]), p1 = D(a[8]);
        double time = 0;
        var seek = new SeekRenderer { Look = style, Intensity = 0.6, Width = w, Height = h, Clock = () => time };
        seek.SetEdge(true);
        seek.SetColors(C(a[9]), C(a[10]), animate: false);
        seek.SetArt(DemoContent.CreateArt(DemoContent.Tracks[0]));
        seek.SetTrack("promo-" + style);
        var stage = Stage(seek, w, h);

        int frames = (int)Math.Round(seconds * Fps);
        for (int i = 0; i < frames; i++)
        {
            time = 10 + i / Fps;
            seek.SetState(p0 + (p1 - p0) * i / Math.Max(1, frames - 1), timeline: true, playing: true);
            seek.Tick();
            seek.InvalidateVisual();
            SavePng(Snap(seek, scale), Path.Combine(outDir, $"f{i:0000}.png"));
        }
        stage.Close();
        Console.WriteLine($"{style}: {frames} frames → {outDir}");
    }

    // ---------------------------------------------------------------- lyrics

    static async Task Lyrics(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        double seconds = D(a[2]);
        var font = a[3].StartsWith("bundled:")
            ? new FontFamily(new Uri(FontsDir), "./#" + a[3]["bundled:".Length..])
            : new FontFamily(a[3]);
        double w = D(a[8]), h = D(a[9]), scale = D(a[10]), size = D(a[11]);

        var presenter = new LyricsPresenter { Accent = Color.FromRgb(0xFF, 0x6B, 0x6B) };
        presenter.Configure(LyricEffect.SlideUp, karaoke: true, twoLine: false, font, size, size * 0.75,
            HorizontalAlignment.Left, speed: 1);
        presenter.SetTextStyle(C(a[4]), 0.96);
        // The same halo MusicBar puts around lyrics (LyricStyle): a zero-offset drop shadow.
        var host = new Grid
        {
            Width = w, Height = h,
            Effect = new DropShadowEffect { ShadowDepth = 0, Color = C(a[5]), Opacity = D(a[6]), BlurRadius = D(a[7]) },
        };
        host.Children.Add(presenter);
        var stage = Stage(host, w, h);
        presenter.ShowLyrics(LrcParser.Parse(PromoLrc));

        // Real time: the line changes animate on WPF's own clock, the karaoke follows the given position.
        int frames = (int)Math.Round(seconds * Fps);
        var pending = new ConcurrentBag<Task>();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            double due = i / Fps;
            while (clock.Elapsed.TotalSeconds < due) await Task.Delay(1);
            presenter.Update(TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds), playing: true);
            var frame = Snap(host, scale);
            string path = Path.Combine(outDir, $"f{i:0000}.png");
            pending.Add(Task.Run(() => SavePng(frame, path)));
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        await Task.WhenAll(pending);
        stage.Close();
        Console.WriteLine($"lyrics: {frames} frames → {outDir}");
    }

    // ---------------------------------------------------------------- control center

    static async Task Panel(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        double scale = D(a[2]);
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // Two demo notifications in place of whatever this machine has waiting.
        var feed = new NotificationFeed();
        typeof(NotificationFeed).GetProperty("Available", Any)!.SetValue(feed, true);
        typeof(NotificationFeed).GetProperty("Items", Any)!.SetValue(feed, new List<NotificationItem>
        {
            new(1, "Tin nhắn", "", null, "Mẹ", "Con ăn cơm chưa? Nhớ nghỉ tay nghe nhạc nha",
                DateTimeOffset.Now.AddMinutes(-2)),
            new(2, "Lịch", "", null, "Họp nhóm lúc 10:00", "Phòng họp tầng 3 · còn 15 phút",
                DateTimeOffset.Now.AddMinutes(-15)),
        });
        var audio = new AudioOutputs();
        var panel = new ControlCenterWindow(feed, audio) { Left = -20000, Top = -20000, ShowActivated = false };
        panel.SetAccent(Color.FromRgb(0xFF, 0x6B, 0x6B));
        panel.Show();

        var type = typeof(ControlCenterWindow);
        void Call(string name, params object[] args) => type.GetMethod(name, Any)!.Invoke(panel, args);
        var root = (FrameworkElement)type.GetField("Root", Any)!.GetValue(panel)!;
        root.BeginAnimation(UIElement.OpacityProperty, null);
        root.Opacity = 1;
        typeof(ControlCenterWindow).GetProperty("IsOpen", Any)!.SetValue(panel, true);
        Call("SyncVolume");
        Call("SyncOutput");
        Call("RenderNotifications");
        Call("RenderTiles");
        await Task.Delay(1500); // the tiles load asynchronously (radios, Night light state)

        var sideKind = type.GetNestedType("SideKind", BindingFlags.NonPublic)!;
        // The whole window: its root sits inside a margin that leaves room for the shadows.
        Call("SetSide", Enum.Parse(sideKind, "None"), false);
        await Task.Delay(300);
        SavePng(Snap(panel, scale), Path.Combine(outDir, "panel-open.png"));

        Call("SetSide", Enum.Parse(sideKind, "Outputs"), false);
        await Task.Delay(300);
        SavePng(Snap(panel, scale), Path.Combine(outDir, "panel-outputs.png"));

        // The Wi-Fi list with made-up networks and a key being typed. It is drawn directly: opening it
        // for real would scan, and on a PC that blocks the scan, open Windows' own Wi-Fi list on screen.
        // Closed as far as Dismiss knows, so losing focus doesn't fade it out.
        typeof(ControlCenterWindow).GetProperty("IsOpen", Any)!.SetValue(panel, false);
        Call("SetSide", Enum.Parse(sideKind, "None"), false);
        type.GetField("_side", Any)!.SetValue(panel, Enum.Parse(sideKind, "WiFi"));
        var side = (FrameworkElement)type.GetField("Side", Any)!.GetValue(panel)!;
        side.BeginAnimation(UIElement.OpacityProperty, null);
        side.Opacity = 1;
        ((TranslateTransform)type.GetField("SideShift", Any)!.GetValue(panel)!).BeginAnimation(TranslateTransform.XProperty, null);
        ((TranslateTransform)type.GetField("SideShift", Any)!.GetValue(panel)!).X = 0;
        type.GetField("_networks", Any)!.SetValue(panel, new List<WifiNetwork>
        {
            new("Nhà Mèo 5G", 4, WifiSecurity.Password, true, "Nhà Mèo 5G", true),
            new("Cafe Sáng", 3, WifiSecurity.Open, false, null, true),
            new("VNPT-Tang2", 3, WifiSecurity.Password, false, null, true),
            new("Viettel_8A2C", 2, WifiSecurity.Password, false, "Viettel_8A2C", false),
            new("CongTy-Staff", 1, WifiSecurity.Enterprise, false, null, true),
        });
        type.GetField("_wifiOpen", Any)!.SetValue(panel, "VNPT-Tang2");
        type.GetField("_wifiAskKey", Any)!.SetValue(panel, true);
        Call("RenderWifi");
        await Task.Delay(300);
        SavePng(Snap(panel, scale), Path.Combine(outDir, "panel-wifi-list.png"));
        panel.Close();
        Console.WriteLine($"panel → {outDir} ({panel.ActualWidth}x{panel.ActualHeight} DIP)");
    }

    // ---------------------------------------------------------------- faces

    /// <summary>
    /// One sheet per background (dark, light): a row per face set, a column per expression, each
    /// face drawn the way the taskbar draws it (design box fitted to a taskbar-tall cell).
    /// </summary>
    static void Faces(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        double cell = D(a[2]);   // cell height in pixels, like the taskbar's thickness
        var accent = Color.FromRgb(0xFF, 0x6B, 0x6B);
        var poses = new (string Name, Func<Color, MusicBar.Views.Face.FaceFrame> Make)[]
        {
            ("rest", ink => Pose(ink, accent)),
            ("smile", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Happy, Mouth = MusicBar.Views.Face.MouthShape.Grin, Blush = 0.5 }),
            ("look", ink => Pose(ink, accent) with { Look = new Vector(0.9, -0.4), LidLeft = 0.3, LidRight = 0.3, Mouth = MusicBar.Views.Face.MouthShape.Flat }),
            ("wow", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Wide, Pupil = 0.65, Mouth = MusicBar.Views.Face.MouthShape.O, Open = 0.9, Brows = 0.6, Extras = MusicBar.Views.Face.FaceExtras.Exclaim }),
            ("angry", ink => Pose(ink, accent) with { LidLeft = 0.25, LidRight = 0.25, Brows = -1, Mouth = MusicBar.Views.Face.MouthShape.Teeth, Extras = MusicBar.Views.Face.FaceExtras.Anger }),
            ("asleep", ink => Pose(ink, accent) with { LidLeft = 1, LidRight = 1, Mouth = MusicBar.Views.Face.MouthShape.O, Open = 0.2, Extras = MusicBar.Views.Face.FaceExtras.Zzz }),
            ("laugh", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Squeeze, Mouth = MusicBar.Views.Face.MouthShape.Open, Open = 0.9, Blush = 0.8, Extras = MusicBar.Views.Face.FaceExtras.Tears }),
            ("love", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Heart, Mouth = MusicBar.Views.Face.MouthShape.Smile, Blush = 0.9, Extras = MusicBar.Views.Face.FaceExtras.Hearts }),
            ("dizzy", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Spiral, Mouth = MusicBar.Views.Face.MouthShape.Wavy, Brows = 0.4, Extras = MusicBar.Views.Face.FaceExtras.Sweat }),
            ("star", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Star, Mouth = MusicBar.Views.Face.MouthShape.Open, Open = 0.7, Extras = MusicBar.Views.Face.FaceExtras.Sparkle }),
            ("sad", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Teary, Brows = 0.9, Mouth = MusicBar.Views.Face.MouthShape.Frown, Extras = MusicBar.Views.Face.FaceExtras.Tears }),
            ("yawn", ink => Pose(ink, accent) with { LidLeft = 0.8, LidRight = 0.8, Mouth = MusicBar.Views.Face.MouthShape.Yawn, Open = 1, Brows = 0.35 }),
            ("sing", ink => Pose(ink, accent) with { Mouth = MusicBar.Views.Face.MouthShape.O, Open = 0.5, Level = 0.7, Extras = MusicBar.Views.Face.FaceExtras.Notes, Blush = 0.25 }),
            ("cat", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Happy, Mouth = MusicBar.Views.Face.MouthShape.Cat, Blush = 0.5 }),
            ("cheeky", ink => Pose(ink, accent) with { LidLeft = 1, Mouth = MusicBar.Views.Face.MouthShape.Tongue, Blush = 0.4 }),
        };
        var styles = Enum.GetValues<MusicBar.Views.Face.FaceStyle>();
        double cw = cell * 116 / 88, labelW = 110, headH = 22;
        foreach (var (name, background, ink) in new[]
                 {
                     ("dark", Color.FromRgb(0x14, 0x15, 0x1A), Color.FromRgb(0xFF, 0xFD, 0xF6)),
                     ("light", Color.FromRgb(0xF2, 0xF3, 0xF5), Color.FromRgb(0x15, 0x17, 0x1E)),
                 })
        {
            var sheet = new DrawingVisual();
            double width = labelW + cw * poses.Length, height = headH + cell * styles.Length;
            using (var dc = sheet.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
                var face = new Typeface("Segoe UI");
                var text = new SolidColorBrush(ink);
                for (int c = 0; c < poses.Length; c++)
                    dc.DrawText(new FormattedText(poses[c].Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, text, 1),
                        new Point(labelW + c * cw + 4, 4));
                for (int r = 0; r < styles.Length; r++)
                {
                    dc.DrawText(new FormattedText(styles[r].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 12, text, 1),
                        new Point(6, headH + r * cell + cell / 2 - 8));
                    var painter = MusicBar.Views.Face.FacePainters.For(styles[r]);
                    for (int c = 0; c < poses.Length; c++)
                    {
                        var frame = poses[c].Make(ink) with { Time = 1.3 + c * 0.37 };
                        double unit = Math.Min(cw / 116, cell / 88);
                        var m = Matrix.Identity;
                        m.Scale(unit, unit);
                        m.Translate(labelW + c * cw + (cw - 100 * unit) / 2, headH + r * cell + (cell - 72 * unit) / 2);
                        dc.PushClip(new RectangleGeometry(new Rect(labelW + c * cw, headH + r * cell, cw, cell)));
                        dc.PushTransform(new MatrixTransform(m));
                        painter.Draw(dc, frame);
                        MusicBar.Views.Face.FacePainters.DrawExtras(dc, frame);
                        dc.Pop();
                        dc.Pop();
                    }
                }
            }
            var rtb = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(sheet);
            SavePng(rtb, Path.Combine(outDir, $"faces-{name}.png"));
        }
        Console.WriteLine($"faces → {outDir}");
    }

    /// <summary>
    /// The face living for a while in real time, through a script of what happens around it (a
    /// poke, a burst of pokes, a rub, the scroll wheel, music, the user leaving), as a sheet of
    /// frames half a second apart.
    /// </summary>
    static async Task FaceLife(string[] a)
    {
        string outPath = Path.GetFullPath(a[1]);
        var style = Enum.Parse<MusicBar.Views.Face.FaceStyle>(a[2], true);
        const double w = 132, h = 96, step = 0.5;
        var view = new MusicBar.Views.Face.FaceView(3) { Look = style, Width = w, Height = h, Accent = Color.FromRgb(0xFF, 0x6B, 0x6B) };
        var host = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x1A)),
            Left = -20000, Top = -20000, Width = w, Height = h, ShowActivated = false, Content = view,
        };
        host.Show();
        var frames = new List<(string Label, BitmapSource Shot)>();
        var clock = Stopwatch.StartNew();
        double nextShot = 0;
        string label = "idle";
        while (clock.Elapsed.TotalSeconds < 19)
        {
            double t = clock.Elapsed.TotalSeconds;
            bool playing = t is >= 11 and < 14;
            double level = playing ? 0.35 + 0.35 * Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.9)) : -1;
            var idle = t >= 14 ? TimeSpan.FromMinutes(6) : TimeSpan.Zero;
            var senses = new MusicBar.Views.Face.FaceSenses(new DateTime(2026, 1, 5, 15, 30, 0), true, playing, level, idle, null, false);
            // The script.
            double now = view.Now;
            if (Cross(t, 1.0)) { view.Brain.Poke(now); label = "poke"; }
            if (t is >= 3 and < 3.7 && Every(t, 0.1)) { view.Brain.Poke(now); label = "pokes"; }
            if (t is >= 6.5 and < 8 && Every(t, 0.05)) { view.Brain.Rub(0.12); label = "rub"; }
            if (t is >= 9 and < 9.6 && Every(t, 0.08)) { view.Brain.Scroll(now, 120); label = "scroll"; }
            if (Cross(t, 11)) label = "music";
            if (Cross(t, 14)) label = "user away";
            view.Step(senses);
            if (t >= nextShot)
            {
                nextShot += step;
                host.UpdateLayout();
                frames.Add(($"{t:0.0}s {label}", Snap(view, 2)));
            }
            await Task.Delay(16);
        }
        host.Close();

        int columns = 8, rows = (frames.Count + columns - 1) / columns;
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x1A)), null, new Rect(0, 0, columns * w, rows * (h + 16)));
            var face = new Typeface("Segoe UI");
            for (int i = 0; i < frames.Count; i++)
            {
                double x = i % columns * w, y = i / columns * (h + 16);
                dc.DrawImage(frames[i].Shot, new Rect(x, y, w, h));
                dc.DrawText(new FormattedText(frames[i].Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 10, Brushes.Gray, 1), new Point(x + 4, y + h));
            }
        }
        var rtb = new RenderTargetBitmap((int)(columns * w) * 2, (int)(rows * (h + 16)) * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(sheet);
        SavePng(rtb, outPath);
        Console.WriteLine($"face life → {outPath} ({frames.Count} frames)");

        bool Cross(double time, double at) => time >= at && time - at < 0.017;
        bool Every(double time, double period) => time % period < 0.017;
    }

    /// <summary>
    /// What the face wears, on every set. Per background (dark, light): a sheet with a row per face
    /// set and a column per hat, glasses, headphones, mic, timer and one combination; and a sheet of
    /// the newer signs on two sets, each at three moments of its loop. Every cell is drawn the way
    /// the taskbar draws the face: props behind, the set, props in front, signs; clipped to the
    /// window's box.
    /// </summary>
    static void Props(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        double cell = D(a[2]);
        double scale = a.Length > 3 ? D(a[3]) : 2;
        var accent = Color.FromRgb(0xFF, 0x6B, 0x6B);
        var singing = new Func<Color, FaceFrame>(ink => Pose(ink, accent) with { Mouth = MouthShape.O, Open = 0.55, Level = 0.6 });
        var resting = new Func<Color, FaceFrame>(ink => Pose(ink, accent));
        var outfits = new (string Name, FaceOutfit Outfit, Func<Color, FaceFrame> Make)[]
        {
            ("plain", default, resting),
            ("cap", new FaceOutfit { Hat = FaceHat.Cap }, resting),
            ("beanie", new FaceOutfit { Hat = FaceHat.Beanie }, resting),
            ("crown", new FaceOutfit { Hat = FaceHat.Crown }, resting),
            ("santa", new FaceOutfit { Hat = FaceHat.Santa }, resting),
            ("witch", new FaceOutfit { Hat = FaceHat.Witch }, resting),
            ("bunny", new FaceOutfit { Hat = FaceHat.Bunny }, resting),
            ("mai", new FaceOutfit { Hat = FaceHat.Mai }, resting),
            ("round", new FaceOutfit { Glasses = FaceGlasses.Round }, resting),
            ("shades", new FaceOutfit { Glasses = FaceGlasses.Shades }, resting),
            ("pixel", new FaceOutfit { Glasses = FaceGlasses.Pixel }, resting),
            ("phones", new FaceOutfit { Headphones = true }, resting),
            ("mic 1.0", new FaceOutfit { Mic = 1 }, singing),
            ("mic 1.2", new FaceOutfit { Mic = 1.2 }, singing),
            ("focus .6", new FaceOutfit { Timer = 0.6 }, resting),
            ("break .3", new FaceOutfit { Timer = 0.3, TimerBreak = true }, resting),
            ("combo", new FaceOutfit { Hat = FaceHat.Beanie, Glasses = FaceGlasses.Round, Headphones = true, Mic = 1 }, singing),
        };
        var signs = new (string Name, FaceExtras Extras)[]
        {
            ("bolt", FaceExtras.Bolt), ("no signal", FaceExtras.NoSignal), ("water", FaceExtras.Water), ("steam", FaceExtras.Steam),
        };
        var styles = Enum.GetValues<FaceStyle>();
        var signStyles = new[] { FaceStyle.Classic, FaceStyle.AnimeGirl };
        double cw = cell * 116 / 88;
        foreach (var (name, background, ink) in new[]
                 {
                     ("dark", Color.FromRgb(0x14, 0x15, 0x1A), Color.FromRgb(0xFF, 0xFD, 0xF6)),
                     ("light", Color.FromRgb(0xF2, 0xF3, 0xF5), Color.FromRgb(0x15, 0x17, 0x1E)),
                 })
        {
            Sheet($"props-{name}-{cell:0}.png", outfits.Select(o => o.Name).ToArray(), styles, (r, c) =>
            {
                var (_, outfit, make) = outfits[c];
                return (styles[r], make(ink) with { Time = 1.3 + c * 0.37 }, outfit);
            });
            const int Moments = 3;
            Sheet($"signs-{name}-{cell:0}.png",
                signs.SelectMany(s => Enumerable.Range(0, Moments).Select(k => $"{s.Name} {k + 1}")).ToArray(), signStyles, (r, c) =>
                {
                    var (_, extras) = signs[c / Moments];
                    return (signStyles[r], Pose(ink, accent) with { Extras = extras, Time = 2 + (c % Moments) * 0.61 }, default(FaceOutfit));
                });

            void Sheet(string file, string[] columns, FaceStyle[] rows, Func<int, int, (FaceStyle Style, FaceFrame Frame, FaceOutfit Outfit)> at)
            {
                double labelW = 84, headH = 18;
                var sheet = new DrawingVisual();
                double width = labelW + cw * columns.Length, height = headH + cell * rows.Length;
                using (var dc = sheet.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
                    var face = new Typeface("Segoe UI");
                    var text = new SolidColorBrush(ink);
                    for (int c = 0; c < columns.Length; c++)
                        dc.DrawText(new FormattedText(columns[c], Inv, FlowDirection.LeftToRight, face, 10, text, 1), new Point(labelW + c * cw + 3, 3));
                    for (int r = 0; r < rows.Length; r++)
                    {
                        dc.DrawText(new FormattedText(rows[r].ToString(), Inv, FlowDirection.LeftToRight, face, 11, text, 1),
                            new Point(4, headH + r * cell + cell / 2 - 7));
                        for (int c = 0; c < columns.Length; c++)
                        {
                            var (style, frame, outfit) = at(r, c);
                            double unit = Math.Min(cw / 116, cell / 88);
                            var m = Matrix.Identity;
                            m.Scale(unit, unit);
                            m.Translate(labelW + c * cw + (cw - 100 * unit) / 2, headH + r * cell + (cell - 72 * unit) / 2);
                            dc.PushClip(new RectangleGeometry(new Rect(labelW + c * cw, headH + r * cell, cw, cell)));
                            dc.PushTransform(new MatrixTransform(m));
                            FaceProps.DrawBehind(dc, style, frame, outfit);
                            FacePainters.For(style).Draw(dc, frame);
                            FaceProps.DrawFront(dc, style, frame, outfit);
                            FacePainters.DrawExtras(dc, frame);
                            dc.Pop();
                            dc.Pop();
                        }
                    }
                }
                var rtb = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                rtb.Render(sheet);
                SavePng(rtb, Path.Combine(outDir, file));
            }
        }
        Console.WriteLine($"props → {outDir}");
    }

    static MusicBar.Views.Face.FaceFrame Pose(Color ink, Color accent) => new()
    {
        Time = 1, Eyes = MusicBar.Views.Face.EyeShape.Open, LidLeft = 0.05, LidRight = 0.05, Look = new Vector(0, 0.1),
        Pupil = 1, Brows = 0, Mouth = MusicBar.Views.Face.MouthShape.Smile, Open = 0, Blush = 0.15,
        Extras = MusicBar.Views.Face.FaceExtras.None, Accent = accent, Ink = ink, Level = 0,
    };

    // ---------------------------------------------------------------- updates

    /// <summary>The updater's download of a real release installer, checked against a digest (right or wrong).</summary>
    static async Task Download(string[] a)
    {
        var release = new ReleaseInfo(new Version(a[1].TrimStart('v')), a[1], "", "", new Uri(a[2]), 0, a[3]);
        var updates = new UpdateService(new AppSettings());
        var last = -1;
        var progress = new Progress<double>(p =>
        {
            int pct = (int)(p * 100);
            if (pct / 25 != last / 25) Console.WriteLine($"  {pct}%");
            last = pct;
        });
        string? path = await updates.DownloadAsync(release, progress, default);
        Console.WriteLine(path == null ? "download refused or failed" : $"downloaded and verified: {path} ({new FileInfo(path).Length} bytes)");
    }

    // ---------------------------------------------------------------- Wi-Fi and Bluetooth

    /// <summary>What the control center's Wi-Fi and Bluetooth lists would show, printed (read-only).</summary>
    static async Task Wireless()
    {
        var wifi = new WifiNetworks();
        Console.WriteLine($"wifi open: {await wifi.OpenAsync()}");
        var cached = await wifi.ListAsync(scan: false);
        Console.WriteLine($"wifi cached: {cached.Count} blocked={wifi.Blocked}");
        var networks = await wifi.ListAsync(scan: true);
        Console.WriteLine($"wifi scanned: {networks.Count} blocked={wifi.Blocked}");
        foreach (var n in networks)
            Console.WriteLine($"  {n.Bars} bars  {n.Security,-10} connected={n.Connected,-5} saved={n.Saved,-5} auto={n.AutoConnect,-5} {n.Ssid}");

        var bt = new BluetoothDevices(Dispatcher.CurrentDispatcher);
        bt.Start();
        await Task.Delay(8000);
        bt.Stop();
        var items = bt.Items();
        Console.WriteLine($"bluetooth: {items.Count}");
        foreach (var i in items)
        {
            bool audio = i.Paired && await BluetoothDevices.CanConnectAsync(i);
            Console.WriteLine($"  paired={i.Paired,-5} connected={i.Connected,-5} {i.Kind,-8} audio={audio,-5} {i.Address,-17} {i.Name}");
        }
    }

    // ---------------------------------------------------------------- settings window

    static async Task SettingsPages(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        var language = Enum.Parse<AppLanguage>(a[2], true);
        double scale = D(a[3]);
        var settings = new AppSettings { Language = language };
        MusicBar.Infrastructure.Loc.Instance.Language = language;
        var window = new SettingsWindow(settings, new MediaService(settings), new LyricsService(settings))
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1100, Height = 1300,
            ShowActivated = false,
        };
        window.Show();
        // MUSICBAR_CAPTURE_DELAY (ms): a longer wait per page, for pages that load from the network.
        int delay = int.TryParse(Environment.GetEnvironmentVariable("MUSICBAR_CAPTURE_DELAY"), out var ms) ? ms : 700;
        for (int page = 0; page < window.PageCount; page++)
        {
            window.Page = page;
            await Task.Delay(delay);
            window.UpdateLayout();
            // The window's own dark backdrop isn't part of its content: lay the content over one.
            var content = Snap((FrameworkElement)window.Content, scale);
            var sheet = new DrawingVisual();
            using (var dc = sheet.RenderOpen())
            {
                var box = new Rect(0, 0, content.PixelWidth / scale, content.PixelHeight / scale);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)), null, box);
                dc.DrawImage(content, box);
            }
            var rtb = new RenderTargetBitmap(content.PixelWidth, content.PixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(sheet);
            SavePng(rtb, Path.Combine(outDir, $"settings-{language}-{page}.png"));
        }
        window.Close();
        Console.WriteLine($"settings ({language}) → {outDir}");
    }

    // ---------------------------------------------------------------- control styles

    /// <summary>One sheet: a row per control style, the buttons paused and then playing, on a dark taskbar.</summary>
    static async Task ControlStyles(string[] a)
    {
        string outPath = Path.GetFullPath(a[1]);
        double h = D(a[2]), scale = D(a[3]);
        var accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        var glyph = new SolidColorBrush(Colors.White);
        var settings = new AppSettings { ShowAlbumArt = false };
        var widget = new WidgetWindow(new MediaService(settings), accent, glyph) { Width = 400, Height = h };
        widget.Show();
        widget.SetArt(DemoContent.CreateArt(DemoContent.Tracks[0]));
        widget.SetAccent(accent.Color);
        // The whole window is snapped (an element's own snapshot loses its layout offset) and the
        // right end, where the pill sits, is cropped out of it.
        var root = (FrameworkElement)widget.FindName("Root");
        var shell = (FrameworkElement)widget.FindName("Pill");
        var inner = (FrameworkElement)widget.FindName("ControlsShell");

        const double labelW = 250, cellW = 170, rowH = 64;
        var styles = Enum.GetValues<ControlStyle>();
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x21)), null, new Rect(0, 0, labelW + cellW * 2, rowH * styles.Length));
            var face = new Typeface("Segoe UI");
            for (int i = 0; i < styles.Length; i++)
            {
                settings.ControlStyle = styles[i];
                widget.ApplyStyle(settings, h, textShadow: false, vertical: false);
                for (int state = 0; state < 2; state++)
                {
                    widget.SetPlaying(state == 1, animate: false);
                    widget.SetProgress(0.62);
                    widget.UpdateLayout();
                    await Task.Delay(120);
                    if (state == 0) Console.WriteLine($"{styles[i],-8} pill {shell.ActualWidth:F1}  shell {inner.ActualWidth:F1} (desired {inner.DesiredSize.Width:F1})");
                    var full = Snap(root, scale);
                    int cropW = (int)Math.Round((shell.ActualWidth + 6) * scale);
                    var img = new CroppedBitmap(full, new Int32Rect(full.PixelWidth - cropW, 0, cropW, full.PixelHeight));
                    double w = img.PixelWidth / scale, hh = img.PixelHeight / scale;
                    dc.DrawImage(img, new Rect(labelW + cellW * state + (cellW - w) / 2, rowH * i + (rowH - hh) / 2, w, hh));
                }
                var label = SettingsOptions.ControlStyles.First(o => (ControlStyle)o.Value == styles[i]).Label.Split(':')[0];
                dc.DrawText(new FormattedText(label, Inv, FlowDirection.LeftToRight, face, 15, Brushes.White, 1.0),
                    new Point(16, rowH * i + rowH / 2 - 10));
            }
        }
        var rtb = new RenderTargetBitmap((int)((labelW + cellW * 2) * scale), (int)(rowH * styles.Length * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(sheet);
        SavePng(rtb, outPath);
        widget.Close();
        Console.WriteLine($"{styles.Length} control styles → {outPath}");
    }

    // ---------------------------------------------------------------- icon and cover

    static void Assets(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        string ico = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..\src\MusicBar\Assets\MusicBar.ico"));
        var decoder = new IconBitmapDecoder(new Uri(ico), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var largest = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        SavePng(largest, Path.Combine(outDir, "musicbar-icon.png"));
        SavePng(DemoContent.CreateArt(DemoContent.Tracks[0]), Path.Combine(outDir, "cover-mua-neon.png"));
        SavePng(DemoContent.CreateArt(DemoContent.Tracks[1]), Path.Combine(outDir, "cover-dem-thanh-pho.png"));
        Console.WriteLine($"icon {largest.PixelWidth}px and covers → {outDir}");
    }
}
