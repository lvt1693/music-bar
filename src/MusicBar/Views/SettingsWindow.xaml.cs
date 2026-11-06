using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

public partial class SettingsWindow : Window
{
    readonly AppSettings _s;
    readonly MediaService _media;
    readonly LyricsService _lyrics;
    readonly ObservableCollection<SourceOption> _sources = [];
    readonly StackPanel[] _pages;
    readonly Stopwatch _previewClock = Stopwatch.StartNew();
    bool _loadingTweaks;

    /// <summary>A row in the tracked-app list; app names are shown as they are, not looked up like <see cref="Option"/> labels.</summary>
    sealed record SourceOption(string Value, string Label);

    public SettingsWindow(AppSettings settings, MediaService media, LyricsService lyrics)
    {
        _s = settings;
        _media = media;
        _lyrics = lyrics;
        InitializeComponent();
        DataContext = settings;
        _pages = [PageGeneral, PageBar, PagePlayer, PageFace, PageProgress, PageGallery, PageLyrics, PageAbout];

        var version = Loc.T("Set_Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
        VersionText.Text = version;
        AboutVersion.Text = $"{version} · .NET {Environment.Version.ToString(2)}";

        FontCombo.ItemsSource = BuildFonts(settings.FontFamily);
        SourceCombo.ItemsSource = _sources;
        RefreshSources();
        RefreshVlc();
        RefreshLyricsInfo();
        RefreshLayerStatus();
        RefreshTaskbarTweaks(pendingRestart: false);
        RefreshDisplays();
        PlayerParts.IsEnabled = settings.PlayerMode == PlayerMode.Full;
        BuildLooks();
        BuildFaceGallery();
        BuildFaceStates();
        ShowFacePictures();
        CompositionTarget.Rendering += OnPreviewFrame;
        UpdateNowPlaying();
        Nav.SelectedIndex = 0;

        _media.PropertyChanged += OnMediaChanged;
        _media.ThumbnailChanged += OnThumbnailChanged;
        _s.PropertyChanged += OnSettingChanged;
        Loaded += (_, _) => LoadAds();
        ShowFocusState(FocusPhase.Off, FocusTimer.Current.Phase);
        FocusTimer.Current.Changed += ShowFocusState;
        Closed += (_, _) =>
        {
            FocusTimer.Current.Changed -= ShowFocusState;
            _media.PropertyChanged -= OnMediaChanged;
            _media.ThumbnailChanged -= OnThumbnailChanged;
            _s.PropertyChanged -= OnSettingChanged;
            CompositionTarget.Rendering -= OnPreviewFrame;
        };
    }

    // ------------------------------------------------------------------ screens

    sealed record DisplayChoice(DisplayTarget Target, string Device, string Label);

    sealed record PlayerChoice(PlayerTarget Target, string Device, string Label);

    bool _loadingDisplays;

    /// <summary>
    /// The main screen, every screen, then each connected screen by the number Windows gives it; for the
    /// player also "with the progress bar" first. A picked screen that is unplugged stays listed.
    /// </summary>
    void RefreshDisplays()
    {
        var screens = Interop.Monitors.All().Select(m =>
        {
            string label = Loc.T("Set_Display_One", Interop.Monitors.Number(m.DeviceName), m.Bounds.Width, m.Bounds.Height);
            return (m.DeviceName, Label: m.IsPrimary ? label + Loc.T("Set_Display_IsMain") : label);
        }).ToList();
        string Missing(string device) => Loc.T("Set_Display_Missing", Interop.Monitors.Number(device));

        var bar = new System.Collections.Generic.List<DisplayChoice>
        {
            new(DisplayTarget.Main, "", Loc.T("Set_Display_Main")),
            new(DisplayTarget.All, "", Loc.T("Set_Display_All")),
        };
        bar.AddRange(screens.Select(s => new DisplayChoice(DisplayTarget.One, s.DeviceName, s.Label)));
        if (_s.DisplayTarget == DisplayTarget.One && _s.DisplayMonitor.Length > 0 && bar.All(c => c.Device != _s.DisplayMonitor))
            bar.Add(new(DisplayTarget.One, _s.DisplayMonitor, Missing(_s.DisplayMonitor)));

        var player = new System.Collections.Generic.List<PlayerChoice>
        {
            new(PlayerTarget.SameAsBar, "", Loc.T("Set_Display_SameAsBar")),
            new(PlayerTarget.Main, "", Loc.T("Set_Display_Main")),
            new(PlayerTarget.All, "", Loc.T("Set_Display_All")),
        };
        player.AddRange(screens.Select(s => new PlayerChoice(PlayerTarget.One, s.DeviceName, s.Label)));
        if (_s.PlayerTarget == PlayerTarget.One && _s.PlayerMonitor.Length > 0 && player.All(c => c.Device != _s.PlayerMonitor))
            player.Add(new(PlayerTarget.One, _s.PlayerMonitor, Missing(_s.PlayerMonitor)));

        _loadingDisplays = true;
        DisplayCombo.ItemsSource = bar;
        DisplayCombo.SelectedItem = bar.FirstOrDefault(c => c.Target == _s.DisplayTarget
                                                            && (c.Target != DisplayTarget.One || c.Device == _s.DisplayMonitor))
                                    ?? bar[0];
        PlayerScreenCombo.ItemsSource = player;
        PlayerScreenCombo.SelectedItem = player.FirstOrDefault(c => c.Target == _s.PlayerTarget
                                                                    && (c.Target != PlayerTarget.One || c.Device == _s.PlayerMonitor))
                                         ?? player[0];
        _loadingDisplays = false;
    }

    void DisplayCombo_DropDownOpened(object? sender, EventArgs e) => RefreshDisplays();

    void DisplayCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDisplays || DisplayCombo.SelectedItem is not DisplayChoice choice) return;
        _s.DisplayMonitor = choice.Device;
        _s.DisplayTarget = choice.Target;
    }

    void PlayerScreenCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDisplays || PlayerScreenCombo.SelectedItem is not PlayerChoice choice) return;
        _s.PlayerMonitor = choice.Device;
        _s.PlayerTarget = choice.Target;
    }

    /// <summary>The page on screen, by its place in the navigation list.</summary>
    public int Page { get => Nav.SelectedIndex; set => Nav.SelectedIndex = value; }

    /// <summary>The face's page, for the face's own menu.</summary>
    public const int FacePage = 3;

    public int PageCount => _pages.Length;

    /// <summary>MusicBar's own fonts first (they all handle Vietnamese), then the installed ones.</summary>
    static System.Collections.Generic.List<string> BuildFonts(string current)
    {
        var names = BundledFonts.Names.ToList();
        names.AddRange(Fonts.SystemFontFamilies.Select(f => f.Source).Except(names).Distinct().OrderBy(n => n));
        if (!names.Contains(current)) names.Insert(0, current);
        return names;
    }

    void RefreshSources()
    {
        if (_sources.Count == 0) _sources.Add(new SourceOption("", Loc.T("Set_Source_Auto")));
        foreach (var s in _media.GetSessions())
            if (_sources.All(o => o.Value != s.AppId)) _sources.Add(new SourceOption(s.AppId, s.AppName));
        var pinned = _s.PinnedAppId;
        if (pinned.Length > 0 && _sources.All(o => o.Value != pinned))
            _sources.Add(new SourceOption(pinned, Loc.T("Set_Source_NotRunning", AppVisualResolver.Resolve(pinned).Name)));
    }

    void RefreshLyricsInfo()
    {
        LyricsFolderText.Text = _lyrics.Folder;
        var (saved, misses) = _lyrics.GetCacheStats();
        CacheCard.Description = Loc.T("Set_Cache_Desc", saved, misses);
    }

    /// <summary>Shows the taskbar's current size/alignment without writing anything back.</summary>
    void RefreshTaskbarTweaks(bool pendingRestart)
    {
        _loadingTweaks = true;
        try
        {
            TaskbarSizeCombo.SelectedValue = TaskbarTweaks.IsSmall;
            TaskbarCenterSwitch.IsChecked = TaskbarTweaks.IsCentered;
            TaskbarCenterSwitch.IsEnabled = TaskbarTweaks.CanCenter;
        }
        finally
        {
            _loadingTweaks = false;
        }

        RefreshTransparentNote();
        TaskbarSizeCard.Description = Loc.T(TaskbarTweaks.Kind == TaskbarKind.Windows11 && TaskbarTweaks.HasTaskbarSizeSetting
            ? "Set_TaskbarSize_DescWin11"
            : TaskbarTweaks.CanShrinkHeight
                ? "Set_TaskbarSize_Desc"
                : "Set_TaskbarSize_DescIconsOnly");
        SwitchTaskbarButton.Visibility = TaskbarTweaks.CanSwitchToExplorerPatcherTaskbar ? Visibility.Visible : Visibility.Collapsed;

        var status = Loc.T("Set_Tb_Current", TaskbarTweaks.KindLabel);
        if (!TaskbarTweaks.CanCenter)
            status += " " + Loc.T("Set_Tb_NoCenter");
        if (TaskbarTweaks.CanSwitchToExplorerPatcherTaskbar)
            status += " " + Loc.T("Set_Tb_TryExplorerPatcher");
        status += " " + Loc.T(pendingRestart ? "Set_Tb_PendingRestart" : "Set_Tb_RestartNote");
        TaskbarStatusText.Text = status;
    }

    void RefreshTransparentNote()
    {
        TransparentCard.Description = Loc.T(Overlay?.TranslucentTaskbarDetected == true
            ? "Set_Transparent_DescTranslucentTB"
            : Interop.XamlTaskbar.Blocked ? "Set_Transparent_DescRestartExplorer"
            : Overlay?.TaskbarAutoHides == true && _s.LayerMode == LayerMode.Auto ? "Set_Transparent_DescAutoHide"
            : "Set_Transparent_Desc");
    }

    void TaskbarSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTweaks || TaskbarSizeCombo.SelectedValue is not bool small) return;
        TaskbarTweaks.SetSmall(small);
        RefreshTaskbarTweaks(pendingRestart: !TaskbarTweaks.AppliesLive(sizeChange: true));
    }

    void TaskbarCenter_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingTweaks) return;
        TaskbarTweaks.SetCentered(TaskbarCenterSwitch.IsChecked == true);
        RefreshTaskbarTweaks(pendingRestart: !TaskbarTweaks.AppliesLive(sizeChange: false));
    }

    void RestartExplorer_Click(object sender, RoutedEventArgs e)
        => ConfirmAndRestartExplorer(Loc.T("Set_Restart_Ask"));

    void SwitchTaskbar_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("Set_SwitchEp_Ask"), Loc.T("Set_SwitchEp_Caption"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        TaskbarTweaks.UseExplorerPatcherTaskbar();
        ConfirmAndRestartExplorer(Loc.T("Set_SwitchEp_Restart"));
    }

    async void ConfirmAndRestartExplorer(string question)
    {
        var answer = MessageBox.Show(this, question + "\n" + Loc.T("Set_Restart_Warning"), Loc.T("Set_RestartExplorer"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
        {
            RefreshTaskbarTweaks(pendingRestart: true);
            return;
        }
        RestartExplorerButton.IsEnabled = SwitchTaskbarButton.IsEnabled = false;
        await System.Threading.Tasks.Task.Run(TaskbarTweaks.RestartExplorer);
        RestartExplorerButton.IsEnabled = SwitchTaskbarButton.IsEnabled = true;
        RefreshTaskbarTweaks(pendingRestart: false);
    }

    // ------------------------------------------------------------------ seekbar gallery

    static (Color, Color, BitmapSource?) DemoPalette()
    {
        var art = DemoContent.CreateArt(DemoContent.Tracks[0]);
        var palette = ColorExtractor.FromBitmap(art);
        return (palette?.Primary ?? ColorUtil.Brand, palette?.Secondary ?? ColorUtil.Brand, art);
    }

    // ------------------------------------------------------------------ face gallery

    readonly System.Collections.Generic.List<(Face.FaceStyle Value, Border Tile, Face.FaceView Preview)> _faceTiles = [];

    /// <summary>Every face set, live: each runs through its expressions on its own.</summary>
    void BuildFaceGallery()
    {
        var (primary, _, _) = Overlay?.PreviewPalette() ?? DemoPalette();
        int seed = 0;
        foreach (var option in SettingsOptions.FaceStyles)
        {
            var preview = new Face.FaceView(++seed * 7) { Look = option.Value, Accent = primary, Touchable = false, IsHitTestVisible = false, Height = 84 };
            preview.Brain.Demo = true;
            if (option.Value == Face.FaceStyle.Custom) preview.Pack = new FacePack();
            var screen = new Border
            {
                Margin = new Thickness(8, 8, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)),
                CornerRadius = new CornerRadius(6),
                Child = preview,
            };
            var text = new StackPanel { Margin = new Thickness(12, 8, 12, 10) };
            text.Children.Add(new TextBlock { Text = option.Label, FontSize = 13, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = option.Description, FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var body = new StackPanel();
            body.Children.Add(screen);
            body.Children.Add(text);

            var tile = new Border
            {
                Width = 186,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Child = body,
            };
            var value = option.Value;
            tile.MouseLeftButtonUp += (_, _) => _s.FaceStyle = value;
            tile.MouseEnter += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255));
            tile.MouseLeave += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255));
            _faceTiles.Add((value, tile, preview));
            FaceGallery.Children.Add(tile);
        }
        UpdateFaceSelection();
    }

    void UpdateFaceSelection()
    {
        var accent = (Brush)FindResource("Accent");
        var idle = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        foreach (var (value, tile, _) in _faceTiles) tile.BorderBrush = value == _s.FaceStyle ? accent : idle;
    }

    /// <summary>Keeps the previews moving while their page is on screen.</summary>
    void OnPreviewFrame(object? sender, EventArgs e)
    {
        if (IsVisible && PageFace.Visibility == Visibility.Visible)
        {
            var senses = new Face.FaceSenses(DateTime.Now, true, false, -1, TimeSpan.Zero, null, false);
            // Dressed as on the taskbar, so a new hat shows at once.
            var outfit = Face.FaceWindow.OutfitFor(_s, DateTime.Now);
            foreach (var (_, _, preview) in _faceTiles)
            {
                preview.Outfit = outfit;
                preview.Brain.HoldsMic = _s.FaceHoldsMic;
                preview.Step(senses);
            }
            TickFacePictures();
        }
        // What the taskbar is doing changes behind the page (the helper clearing it, auto-hide switched,
        // TranslucentTB started): the notes follow along instead of telling what was true when it opened.
        if (IsVisible && PageBar.Visibility == Visibility.Visible && DateTime.Now - _barNotesAt > TimeSpan.FromSeconds(1))
        {
            _barNotesAt = DateTime.Now;
            RefreshLayerStatus();
            RefreshTransparentNote();
        }
        if (IsVisible && PageGallery.Visibility == Visibility.Visible)
            TickGallery(0.22 + (_previewClock.Elapsed.TotalSeconds * 0.015) % 0.6);
        if (!IsVisible || PageProgress.Visibility != Visibility.Visible) return;
        double t = _previewClock.Elapsed.TotalSeconds;
        TickLooks(0.22 + (t * 0.015) % 0.6);
    }

    DateTime _barNotesAt;

    void RefreshLayerStatus()
    {
        if (Overlay is not { } overlay) return;
        var where = Loc.T(overlay.IsOverMode ? "Set_Layer_Over" : "Set_Layer_Under");
        var tool = Loc.T(overlay.TranslucentTaskbarDetected ? "Set_Layer_ToolFound" : "Set_Layer_ToolMissing");
        LayerStatusText.Text = Loc.T("Set_Layer_Status", where, tool);
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.LyricsFolder)) RefreshLyricsInfo();
        if (e.PropertyName == nameof(AppSettings.LayerMode))
        {
            RefreshLayerStatus();
            RefreshTransparentNote();
        }
        if (e.PropertyName is nameof(AppSettings.SeekStyle) or nameof(AppSettings.Skin) or nameof(AppSettings.Palettes)) RefreshLooks();
        if (e.PropertyName == nameof(AppSettings.FillOpacity))
            foreach (var look in _looks) look.Engine.Intensity = _s.FillOpacity;
        if (e.PropertyName == nameof(AppSettings.SeekDirection))
            foreach (var look in _looks) look.Engine.Direction = _s.SeekDirection;
        if (e.PropertyName == nameof(AppSettings.PlayerMode)) PlayerParts.IsEnabled = _s.PlayerMode == PlayerMode.Full;
        if (e.PropertyName == nameof(AppSettings.FaceStyle))
        {
            UpdateFaceSelection();
            ShowFacePictures();
        }
    }

    void OnMediaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaService.Title) or nameof(MediaService.Artist) or nameof(MediaService.HasSession))
            UpdateNowPlaying();
        else if (e.PropertyName == nameof(MediaService.VlcAnswers))
            RefreshVlc();
    }

    /// <summary>VLC's switch shows with VLC installed; under it, what happens next, or that it works.</summary>
    void RefreshVlc()
    {
        VlcPanel.Visibility = VlcLink.Installed ? Visibility.Visible : Visibility.Collapsed;
        string? status = !_s.ReadVlc ? null
            : _media.VlcAnswers ? Loc.T("Set_Vlc_Connected")
            : !VlcLink.Enabled ? Loc.T("Set_Vlc_Failed")
            : VlcLink.Running ? Loc.T("Set_Vlc_Restart")
            : Loc.T("Set_Vlc_Ready");
        VlcStatus.Text = status ?? "";
        VlcStatus.Visibility = status != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Turned on, VLC's web interface has just been switched on in its settings (MediaService does it): say what's next.</summary>
    void Vlc_Click(object sender, RoutedEventArgs e) => RefreshVlc();

    void OnThumbnailChanged(object? sender, EventArgs e) => NowArt.ImageSource = _media.Thumbnail;

    void UpdateNowPlaying()
    {
        if (_media.HasSession)
        {
            NowTitle.Text = _media.Title.Length > 0 ? _media.Title : _media.AppName;
            NowArtist.Text = _media.Artist.Length > 0 ? $"{_media.Artist} · {_media.AppName}" : _media.AppName;
        }
        else
        {
            NowTitle.Text = Loc.T("Set_NowPlaying_None");
            NowArtist.Text = Loc.T("Set_NowPlaying_Hint");
        }
        NowArt.ImageSource = _media.Thumbnail;
    }

    // ------------------------------------------------------------------ handlers

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages == null) return;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.ScrollToTop();
        if (PageLyrics.Visibility == Visibility.Visible) RefreshLyricsInfo();
        if (PageGallery.Visibility == Visibility.Visible) ShowGalleryPage();
    }

    void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;
        _s.CustomColor = hex;
        _s.AccentSource = AccentSource.Custom;
    }

    void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        HexBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        _s.AccentSource = AccentSource.Custom;
    }

    void LyricSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) _s.LyricColor = hex;
    }

    void StrokeSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) _s.LyricStrokeColor = hex;
    }

    /// <summary>Enter applies a typed hex color right away.</summary>
    void ColorBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    void PreviewEffect_Click(object sender, RoutedEventArgs e) => Overlay?.PreviewLyrics();

    void SourceCombo_DropDownOpened(object? sender, EventArgs e) => RefreshSources();

    void OpenLyricsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_lyrics.Folder);
        Open(_lyrics.Folder);
    }

    void ChangeLyricsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("Set_PickLyricsFolder"),
            InitialDirectory = Directory.Exists(_lyrics.Folder) ? _lyrics.Folder : AppPaths.DataDir,
        };
        if (dialog.ShowDialog(this) == true) _s.LyricsFolder = dialog.FolderName;
    }

    void ResetLyricsFolder_Click(object sender, RoutedEventArgs e) => _s.LyricsFolder = "";

    void ClearMisses_Click(object sender, RoutedEventArgs e)
    {
        _lyrics.ClearMisses();
        RefreshLyricsInfo();
    }

    void ClearDownloaded_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("Set_ClearDownloaded_Ask"), Loc.T("Set_ClearDownloaded"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        int removed = _lyrics.ClearDownloaded();
        RefreshLyricsInfo();
        Log.Info($"Removed {removed} downloaded lyric files");
    }

    async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;
        CheckNowButton.IsEnabled = false;
        UpdateCard.Description = Loc.T("Set_CheckNow_Checking");
        var release = await app.CheckForUpdatesAsync(manual: true);
        CheckNowButton.IsEnabled = true;
        UpdateCard.Description = release == null
            ? Loc.T("Set_CheckNow_None", UpdateService.Current.ToString(3))
            : Loc.T("Set_CheckNow_Found", release.Version.ToString(3));
    }

    void Recap_Click(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowRecap();

    void Focus_Click(object sender, RoutedEventArgs e)
    {
        var timer = FocusTimer.Current;
        if (timer.Phase == FocusPhase.Off) timer.Start(_s);
        else timer.Stop();
    }

    void ShowFocusState(FocusPhase from, FocusPhase to)
        => FocusButton.Content = Loc.T(to == FocusPhase.Off ? "Set_FocusStartButton" : "Set_FocusStopButton");

    void OpenDataFolder_Click(object sender, RoutedEventArgs e) => Open(AppPaths.DataDir);

    void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(AppPaths.LogFile)) Open(AppPaths.LogFile);
        else Open(AppPaths.DataDir);
    }

    void Tour_Click(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowWelcome();

    /// <summary>Every option back to how MusicBar comes, after asking: it can't be undone.</summary>
    void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("Set_Reset_Ask"), Loc.T("Set_Reset"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        _s.ResetToDefaults();
        Log.Info("Settings put back to their defaults");
        ShowLookGroup(CurrentLookGroup());
        MessageBox.Show(this, Loc.T("Set_Reset_Done"), Loc.T("Set_Reset"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>The bar, when this window runs inside MusicBar (a preview tool may host it alone).</summary>
    static TaskbarOverlay? Overlay => (Application.Current as App)?.Overlay;

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Opening {target} failed", ex);
        }
    }
}
