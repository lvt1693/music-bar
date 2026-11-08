using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// The online gallery page: what others shared (live previews, one click to keep), the presets kept
/// here, and sharing one's own look or skin under a nickname. Also the sponsor's banner under the pages.
/// </summary>
public partial class SettingsWindow
{
    public const int GalleryPage = 5;

    sealed record GalleryTile(GalleryItem Item, Border Tile, SeekRenderer Engine, SkinView? Shader, Button Get);
    sealed record ShareChoice(string Label, SkinInfo? Skin);

    Community? _community;
    Community Hub => _community ??= new Community(_s);
    readonly List<GalleryTile> _galleryTiles = [];
    readonly List<(LookPreset Preset, Border Tile, SeekRenderer Engine)> _presetTiles = [];
    readonly List<(string Category, Border Tab)> _galleryTabs = [];
    string _galleryCategory = "";
    int _galleryPage = 1;
    bool _galleryAsked;
    CancellationTokenSource? _galleryCts;
    // Only the tiles on screen (or about to be) move, and a skin tile fetches and compiles its skin the first
    // time it comes near: a page of 30 live skins, all compiled at once, froze weaker PCs (#17).
    readonly HashSet<Border> _nearTiles = [];
    readonly Dictionary<Border, Func<Task>> _pendingPreviews = [];
    bool _nearStale = true;

    // ------------------------------------------------------------------ browsing

    /// <summary>The page opened: the first look at the gallery, and the presets kept here.</summary>
    void ShowGalleryPage()
    {
        if (_galleryTabs.Count == 0)
        {
            BuildGalleryTabs();
            PageScroll.ScrollChanged += (_, _) => _nearStale = true;
        }
        BuildPresetTiles();
        BuildShareChoices();
        if (!_galleryAsked) LoadGallery();
        _nearStale = true;
    }

    void BuildGalleryTabs()
    {
        foreach (var (category, key) in new[] { ("", "Set_Gal_All"), ("visualizer", "Set_Progress_GroupVisualizer"), ("progress", "Set_Progress_TabProgress"), ("led", "Set_Progress_GroupLed") })
        {
            var tab = new Border { Padding = new Thickness(12, 6, 13, 6), CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = Loc.T(key), FontSize = 12.5 } };
            tab.MouseLeftButtonUp += (_, _) =>
            {
                _galleryCategory = category;
                PaintGalleryTabs();
                LoadGallery();
            };
            _galleryTabs.Add((category, tab));
            GalleryTabs.Children.Add(tab);
        }
        PaintGalleryTabs();
    }

    void PaintGalleryTabs()
    {
        foreach (var (category, tab) in _galleryTabs)
        {
            bool on = category == _galleryCategory;
            tab.Background = on ? new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) : Brushes.Transparent;
            ((TextBlock)tab.Child).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    async void LoadGallery(bool more = false)
    {
        _galleryAsked = true;
        _galleryCts?.Cancel();
        var cts = _galleryCts = new CancellationTokenSource();
        if (!more)
        {
            _galleryPage = 1;
            GalleryGrid.Children.Clear();
            _galleryTiles.Clear();
            _pendingPreviews.Clear();
        }
        GalleryStatus.Text = Loc.T("Set_Gal_Loading");
        try
        {
            var (items, total) = await Hub.GalleryAsync(_galleryCategory, "popular", _galleryPage, cts.Token);
            if (cts.IsCancellationRequested) return;
            foreach (var item in items) GalleryGrid.Children.Add(GalleryTileFor(item, cts.Token));
            _nearStale = true;
            GalleryStatus.Text = total == 0 ? Loc.T("Set_Gal_Empty") : Loc.T("Set_Gal_Count", total);
            GalleryMore.Visibility = _galleryTiles.Count < total ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException)
        {
            if (cts.IsCancellationRequested) return;
            GalleryStatus.Text = Loc.T("Set_Gal_Offline");
            Log.Info($"Gallery unreachable: {ex.Message}");
        }
    }

    Border GalleryTileFor(GalleryItem item, CancellationToken ct)
    {
        var engine = new SeekRenderer { ClipToBounds = true, Intensity = 0.8 };
        engine.SetEdge(true);
        var colors = item.Colors.Select(TryColor).OfType<Color>().ToArray();
        if (colors.Length == 3) engine.SetColors(colors[0], colors[1], animate: false, colors[2]);
        else engine.SetColors(_songA, _songB, animate: false);
        SkinView? shader = null;
        UIElement preview = engine;
        if (item.IsSkin)
        {
            engine.Look = item.Category == "visualizer" ? SeekStyle.Equalizer : SeekStyle.Classic;
            engine.Skinned = true;
            engine.Visibility = Visibility.Hidden;
            shader = new SkinView();
            var stage = new Grid();
            stage.Children.Add(engine);
            stage.Children.Add(shader);
            preview = stage;
        }
        else
        {
            engine.Look = Enum.TryParse<SeekStyle>(item.Style, out var style) ? style : SeekStyle.Spectrum;
        }

        var get = new Button { Padding = new Thickness(12, 3, 12, 3), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var tile = GalleryCard(preview, item.Name, $"{Loc.T("Set_Gal_By")} {item.Author} · {Loc.T("Set_Gal_Downloads", item.Downloads)}", get);
        if (shader is { } view) _pendingPreviews[tile] = () => PreviewSkinAsync(item, view, ct);
        var entry = new GalleryTile(item, tile, engine, shader, get);
        PaintGet(entry);
        get.Click += async (_, e) =>
        {
            e.Handled = true;
            await InstallAsync(entry);
        };
        // The whole card puts it on the taskbar too, the way the looks on the progress page do.
        tile.Cursor = Cursors.Hand;
        tile.MouseLeftButtonUp += async (_, _) => await InstallAsync(entry);
        tile.ToolTip = item.Description.Length > 0 ? item.Description : null;
        _galleryTiles.Add(entry);
        return tile;
    }

    /// <summary>A skin's preview: its code fetched and compiled off the UI thread, then running in the tile.</summary>
    async Task PreviewSkinAsync(GalleryItem item, SkinView shader, CancellationToken ct)
    {
        try
        {
            var bytecode = await Hub.PreviewShaderAsync(item, ct);
            if (bytecode != null && !ct.IsCancellationRequested)
                shader.SetSkin(new SkinInfo("gallery:" + item.Slug, item.Name, item.Author, item.Description, item.Category == "visualizer", bytecode, null));
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or OperationCanceledException or InvalidDataException or DecoderFallbackException or IOException)
        {
        }
    }

    /// <summary>
    /// The item is on disk as the gallery has it now. A look fixed on the gallery after it was downloaded has
    /// another checksum, so Apply brings the new one down instead of keeping the old copy for good.
    /// </summary>
    bool UpToDate(GalleryItem item)
    {
        var path = Path.Combine(item.IsSkin ? SkinLibrary.Folder : LookPresets.Folder,
            LookPresets.SafeName(item.Slug) + (item.IsSkin ? ".hlsl" : ".json"));
        if (!File.Exists(path)) return false;
        if (item.Sha256.Length == 0) return true;
        try
        {
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() == item.Sha256;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The item is the look on the taskbar right now.</summary>
    bool InUse(GalleryItem item)
    {
        var name = LookPresets.SafeName(item.Slug);
        if (item.IsSkin) return _s.Skin == name + ".hlsl";
        return LookPresets.List().FirstOrDefault(p => p.File == name + ".json") is { } preset && LookPresets.IsOn(preset, _s);
    }

    void PaintGet(GalleryTile t)
    {
        bool on = InUse(t.Item);
        // One button, as on the website: it downloads the look when it isn't here yet, then puts it on.
        t.Get.Content = on ? Loc.T("Set_Gal_InUse") : Loc.T("Set_Gal_Apply");
        t.Tile.BorderBrush = on ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
    }

    /// <summary>
    /// A musicbar:// link from the website: the item is fetched by its slug (from the gallery, checked as
    /// always), kept and put on the taskbar, and this page says so.
    /// </summary>
    public async Task OpenFromLinkAsync(string slug)
    {
        Page = GalleryPage;
        GalleryStatus.Text = Loc.T("Set_Gal_Loading");
        try
        {
            if (await Hub.ItemAsync(slug, CancellationToken.None) is not { } item)
            {
                GalleryStatus.Text = Loc.T("Set_Gal_NoSuchItem");
                return;
            }
            // The item as the gallery has it this moment: a tile loaded earlier may carry an older checksum.
            var entry = _galleryTiles.FirstOrDefault(t => t.Item.Slug == slug) is { } tile
                ? tile with { Item = item }
                : new GalleryTile(item, new Border(), new SeekRenderer(), null, new Button());
            await InstallAsync(entry);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!ApplyDownloaded(slug)) GalleryStatus.Text = Loc.T("Set_Gal_Offline");
        }
    }

    /// <summary>A look downloaded before, put on without the gallery (no connection, or the site is down).</summary>
    bool ApplyDownloaded(string slug)
    {
        var name = LookPresets.SafeName(slug);
        if (File.Exists(Path.Combine(SkinLibrary.Folder, name + ".hlsl")))
        {
            _s.Skin = name + ".hlsl";
            BuildSkinTiles();
        }
        else if (LookPresets.List().FirstOrDefault(p => p.File == name + ".json") is { } preset)
        {
            LookPresets.Apply(preset, _s);
        }
        else
        {
            return false;
        }
        RefreshLooks();
        BuildPresetTiles();
        foreach (var g in _galleryTiles) PaintGet(g);
        GalleryStatus.Text = Loc.T("Set_Gal_Applied", slug);
        Log.Info($"Gallery link '{slug}' applied from the copy on disk (the gallery didn't answer)");
        return true;
    }

    async Task InstallAsync(GalleryTile t)
    {
        try
        {
            t.Get.IsEnabled = false;
            bool fetched = !UpToDate(t.Item);
            if (fetched) await Hub.InstallAsync(t.Item, CancellationToken.None);
            var name = LookPresets.SafeName(t.Item.Slug);
            if (t.Item.IsSkin)
            {
                // A new copy of the skin that is already on: let go of it for a moment so the bar loads the new file.
                if (fetched && _s.Skin == name + ".hlsl") _s.Skin = "";
                _s.Skin = name + ".hlsl";
                BuildSkinTiles();
            }
            else if (LookPresets.List().FirstOrDefault(p => p.File == name + ".json") is { } preset)
            {
                LookPresets.Apply(preset, _s);
            }
            RefreshLooks();
            BuildPresetTiles();
            BuildShareChoices();
            GalleryStatus.Text = Loc.T("Set_Gal_Applied", t.Item.Name);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidDataException or IOException or DecoderFallbackException)
        {
            GalleryStatus.Text = Loc.T("Set_Gal_Failed", t.Item.Name, ex.Message);
            Log.Error($"Installing '{t.Item.Slug}' from the gallery failed", ex);
        }
        finally
        {
            t.Get.IsEnabled = true;
            // The one that was on before isn't any more.
            foreach (var g in _galleryTiles) PaintGet(g);
            if (!_galleryTiles.Contains(t)) PaintGet(t);
        }
    }

    /// <summary>A gallery card: the live preview on a little dark screen, the name, who made it, and its button.</summary>
    Border GalleryCard(UIElement preview, string name, string line, UIElement action)
    {
        var screen = new Border
        {
            Height = 34, Margin = new Thickness(6, 6, 6, 0), Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)),
            Child = preview, ClipToBounds = true,
        };
        screen.SizeChanged += (_, _) => screen.Clip = new RectangleGeometry(new Rect(screen.RenderSize), 5, 5);
        var text = new StackPanel { Margin = new Thickness(9, 6, 6, 8) };
        text.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = line, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        Grid.SetColumn((UIElement)action, 1);
        if (action is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(action);
        var body = new StackPanel();
        body.Children.Add(screen);
        body.Children.Add(row);
        return new Border
        {
            Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)), Child = body,
        };
    }

    /// <summary>Keeps the gallery's previews moving while its page is on screen, the ones in view only.</summary>
    void TickGallery(double progress)
    {
        if (_nearStale) FindNearTiles();
        foreach (var t in _galleryTiles)
        {
            if (!_nearTiles.Contains(t.Tile)) continue;
            t.Engine.SetState(progress, timeline: true, playing: true);
            t.Engine.Tick();
            t.Shader?.Update(t.Engine);
        }
        foreach (var (_, tile, engine) in _presetTiles)
        {
            if (!_nearTiles.Contains(tile)) continue;
            engine.SetState(progress, timeline: true, playing: true);
            engine.Tick();
        }
    }

    /// <summary>
    /// The tiles in the page's view or close to it (a skin tile starts its preview the first time), worked out
    /// again when the page scrolls, resizes or gets new tiles.
    /// </summary>
    void FindNearTiles()
    {
        _nearStale = false;
        _nearTiles.Clear();
        var view = new Rect(PageScroll.RenderSize);
        view.Inflate(0, 160);
        foreach (var tile in _galleryTiles.Select(t => t.Tile).Concat(_presetTiles.Select(p => p.Tile)))
        {
            // Not laid out yet: ask again next frame rather than guess (a guess would start every preview).
            if (!tile.IsArrangeValid || tile.RenderSize.Height == 0)
            {
                _nearStale = true;
                continue;
            }
            if (!tile.TransformToAncestor(PageScroll).TransformBounds(new Rect(tile.RenderSize)).IntersectsWith(view)) continue;
            _nearTiles.Add(tile);
            if (_pendingPreviews.Remove(tile, out var start)) _ = start();
        }
    }

    void GalleryMore_Click(object sender, RoutedEventArgs e)
    {
        _galleryPage++;
        LoadGallery(more: true);
    }

    void GalleryRefresh_Click(object sender, RoutedEventArgs e) => LoadGallery();

    void GalleryWeb_Click(object sender, RoutedEventArgs e) => Open(Community.Server + "/gallery");

    // ------------------------------------------------------------------ presets kept here

    void BuildPresetTiles()
    {
        PresetGrid.Children.Clear();
        _presetTiles.Clear();
        var accent = (Brush)FindResource("Accent");
        foreach (var preset in LookPresets.List())
        {
            var engine = new SeekRenderer { Look = preset.Style, ClipToBounds = true, Intensity = preset.Intensity, Direction = preset.Direction };
            engine.SetEdge(true);
            engine.SetColors(preset.Colors[0], preset.Colors[1], animate: false, preset.Colors[2]);
            var remove = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11, Padding = new Thickness(6, 3, 6, 3), ToolTip = Loc.T("Set_Gal_Remove") };
            var tile = GalleryCard(engine, preset.Name, preset.Style.ToString(), remove);
            tile.Cursor = Cursors.Hand;
            if (LookPresets.IsOn(preset, _s)) tile.BorderBrush = accent;
            var p = preset;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                LookPresets.Apply(p, _s);
                RefreshLooks();
                BuildPresetTiles();
            };
            remove.Click += (_, e) =>
            {
                e.Handled = true;
                LookPresets.Delete(p.File);
                BuildPresetTiles();
                foreach (var t in _galleryTiles) PaintGet(t);
            };
            _presetTiles.Add((preset, tile, engine));
            PresetGrid.Children.Add(tile);
        }
        PresetsEmpty.Visibility = _presetTiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _nearStale = true;
    }

    // ------------------------------------------------------------------ sharing

    void BuildShareChoices()
    {
        var choices = new List<ShareChoice>();
        if (_s.Skin.Length == 0) choices.Add(new ShareChoice(Loc.T("Set_Gal_ShareLook", _s.SeekStyle), null));
        // Only skins compiled already (the Looks page compiles the rest on a pool thread): compiling here froze the page.
        foreach (var skin in SkinLibrary.List(compile: false).Where(k => k.Works)) choices.Add(new ShareChoice(Loc.T("Set_Gal_ShareSkin", skin.Name), skin));
        var was = (ShareWhat.SelectedItem as ShareChoice)?.Label;
        ShareWhat.ItemsSource = choices;
        ShareWhat.SelectedItem = choices.FirstOrDefault(c => c.Label == was) ?? choices.FirstOrDefault();
    }

    async void Share_Click(object sender, RoutedEventArgs e)
    {
        string nick = _s.CommunityNickname.Trim(), name = ShareName.Text.Trim(), about = ShareDesc.Text.Trim();
        if (nick.Length < 2) { ShowShare(Loc.T("Set_Gal_NeedNick"), false); return; }
        if (name.Length < 2) { ShowShare(Loc.T("Set_Gal_NeedName"), false); return; }
        if (ShareWhat.SelectedItem is not ShareChoice choice) return;

        byte[] file;
        string fileName, category;
        byte[]? picture = null;
        if (choice.Skin is { } skin)
        {
            file = await File.ReadAllBytesAsync(Path.Combine(SkinLibrary.Folder, skin.File));
            fileName = skin.File;
            category = skin.Visualizer ? "visualizer" : "progress";
            picture = await SkinPreviewPng(skin);
        }
        else
        {
            var (a, b, _) = Overlay?.PreviewPalette() ?? DemoPalette();
            var json = LookPresets.FromSettings(_s, name, about, a, b);
            if (json == null) return;
            file = Encoding.UTF8.GetBytes(json);
            fileName = LookPresets.SafeName(name) + ".json";
            category = SeekRenderer.IsLedStrip(_s.SeekStyle) ? "led" : SeekRenderer.IsVisualizer(_s.SeekStyle) ? "visualizer" : "progress";
            picture = PreviewPng(LookPresets.Parse(json, fileName));
        }

        ShareButton.IsEnabled = false;
        ShowShare(Loc.T("Set_Gal_Sending"), true);
        var result = await Hub.SubmitAsync(fileName, file, name, about, category, picture, CancellationToken.None);
        ShareButton.IsEnabled = true;
        if (result.Ok) ShowShare(Loc.T("Set_Gal_Sent", name) + (result.Findings.Count > 0 ? "\n" + string.Join("\n", result.Findings) : ""), true);
        else ShowShare(Loc.T(result.Message switch { "rate" => "Set_Gal_TooMany", "network" => "Set_Gal_Offline", _ => "Set_Gal_Refused" })
                       + (result.Findings.Count > 0 && result.Message != "network" ? "\n" + string.Join("\n", result.Findings) : ""), false);
    }

    void ShowShare(string text, bool good)
    {
        ShareStatus.Text = text;
        ShareStatus.Foreground = good ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
        ShareStatus.Visibility = Visibility.Visible;
    }

    /// <summary>The preset drawn a moment into its song, as the picture the gallery shows until someone adds a clip.</summary>
    static byte[]? PreviewPng(LookPreset? preset)
    {
        if (preset == null) return null;
        const double w = 960, h = 40;
        double time = 0;
        var engine = new SeekRenderer { Look = preset.Style, Intensity = preset.Intensity, Direction = preset.Direction, Width = w, Height = h, Clock = () => time };
        engine.SetEdge(true);
        engine.SetColors(preset.Colors[0], preset.Colors[1], animate: false, preset.Colors[2]);
        var host = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)) };
        host.Children.Add(engine);
        var stage = new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000,
            SizeToContent = SizeToContent.WidthAndHeight, Content = host,
        };
        try
        {
            stage.Show();
            for (int i = 0; i < 150; i++)
            {
                time = 10 + i / 60.0;
                engine.SetState(0.62, timeline: true, playing: true);
                engine.Tick();
            }
            host.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)w * 2, (int)h * 2, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error("Drawing the preset's preview failed", ex);
            return null;
        }
        finally
        {
            stage.Close();
        }
    }

    /// <summary>
    /// A skin a moment into the song, for the gallery. A shader only draws on the graphics card, so the
    /// skin runs in a window off screen (on the card) and the window's picture is taken as Windows composed it.
    /// </summary>
    async Task<byte[]?> SkinPreviewPng(SkinInfo skin)
    {
        const double w = 760, h = 84;
        double time = 0;
        var engine = new SeekRenderer { Look = skin.Visualizer ? SeekStyle.Equalizer : SeekStyle.Classic, Skinned = true,
            Visibility = Visibility.Hidden, Width = w, Height = h, Clock = () => time };
        engine.SetEdge(true);
        var palette = StylePalettes.For(_s, StylePalettes.KeyForSkin(skin.File));
        engine.SetColors(palette?.A ?? _songA, palette?.B ?? _songB, animate: false, palette?.C);
        var shader = new SkinView();
        shader.SetSkin(skin);
        var host = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)) };
        host.Children.Add(engine);
        host.Children.Add(shader);
        var stage = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight, Content = host,
        };
        try
        {
            stage.Show();
            for (int i = 0; i < 90; i++)
            {
                time = 10 + i / 30.0;
                engine.SetState(0.62, timeline: true, playing: true);
                engine.Tick();
                shader.Update(engine);
                if (i % 15 == 0) await Task.Delay(16);
            }
            await Task.Delay(150);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(stage).Handle;
            if (!Interop.Native.GetWindowRect(hwnd, out var r) || r.Width <= 0 || r.Height <= 0) return null;
            // A heavy skin may not have drawn its first frame yet: the picture then comes back black, without
            // even the window's dark ground. Keep it running and look again; a black picture is never sent.
            BitmapSource? source = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var shot = GrabWindow(hwnd, r.Width, r.Height);
                if (!IsBlack(shot))
                {
                    source = shot;
                    break;
                }
                for (int i = 0; i < 12; i++)
                {
                    time += 1 / 30.0;
                    engine.Tick();
                    shader.Update(engine);
                    await Task.Delay(16);
                }
            }
            if (source == null)
            {
                Log.Info($"The preview of {skin.File} stayed black: shared without a picture");
                return null;
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error("Drawing the skin's preview failed", ex);
            return null;
        }
        finally
        {
            stage.Close();
        }
    }

    /// <summary>A window's picture as Windows composed it (works off screen, and for what the graphics card draws).</summary>
    static BitmapSource GrabWindow(IntPtr hwnd, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        PrintWindow(hwnd, memory, 2 /* PW_RENDERFULLCONTENT */);
        SelectObject(memory, old);
        var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        Interop.Native.DeleteObject(bitmap);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
        return source;
    }

    /// <summary>Nothing drawn at all: every pixel black (the stage's ground alone is a dark grey, not black).</summary>
    static bool IsBlack(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] > 4 || pixels[i + 1] > 4 || pixels[i + 2] > 4) return false;
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    static Color? TryColor(string hex)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$") ? (Color)ColorConverter.ConvertFromString(hex) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ the sponsor's banner

    IReadOnlyList<AdBanner> _ads = [];
    readonly Dictionary<int, BitmapImage> _adImages = [];
    readonly HashSet<int> _adsSeen = [];
    DispatcherTimer? _adTimer;
    int _adIndex;

    /// <summary>Banners from the website, one at a time, sliding on when there are several. Nothing shows when none load.</summary>
    async void LoadAds()
    {
        var (ads, seconds) = await Hub.AdsAsync(CancellationToken.None);
        foreach (var ad in ads)
        {
            if (await Hub.ImageAsync(ad.ImageUrl, CancellationToken.None) is not { } bytes) continue;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1400;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();
                _adImages[ad.Id] = image;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException)
            {
            }
        }
        _ads = ads.Where(a => _adImages.ContainsKey(a.Id)).ToList();
        if (_ads.Count == 0 || !IsLoaded) return;
        AdDots.Children.Clear();
        if (_ads.Count > 1)
            foreach (var _ in _ads) AdDots.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 0, 0) });
        ShowAd(0, animate: false);
        AdBand.Visibility = Visibility.Visible;
        if (_ads.Count > 1)
        {
            _adTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _adTimer.Tick += (_, _) => { if (!AdBand.IsMouseOver) ShowAd((_adIndex + 1) % _ads.Count, animate: true); };
            _adTimer.Start();
            Closed += (_, _) => _adTimer.Stop();
        }
    }

    void ShowAd(int index, bool animate)
    {
        _adIndex = index;
        var ad = _ads[index];
        AdImage.Source = _adImages[ad.Id];
        AdImage.ToolTip = ad.Alt.Length > 0 ? ad.Alt : ad.Title;
        AutomationProperties_SetName(ad.Alt.Length > 0 ? ad.Alt : ad.Title);
        for (int i = 0; i < AdDots.Children.Count; i++)
            ((Border)AdDots.Children[i]).Background = new SolidColorBrush(i == index ? Colors.White : Color.FromArgb(0x66, 255, 255, 255));
        if (animate) AdImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(450)));
        if (_adsSeen.Add(ad.Id)) _ = Hub.SeenAsync(ad);
    }

    void AutomationProperties_SetName(string name) => System.Windows.Automation.AutomationProperties.SetName(AdBand, name);

    void AdBand_Click(object sender, MouseButtonEventArgs e)
    {
        if (_ads.Count == 0) return;
        Open(_ads[_adIndex].ClickUrl);
    }
}
