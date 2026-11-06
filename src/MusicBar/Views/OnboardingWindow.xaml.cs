using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;
using MusicBar.Views.Face;

namespace MusicBar.Views;

/// <summary>
/// The welcome tour, the first time MusicBar runs (and again from Settings → About): the choices
/// that matter most, one step each, every one applied the moment it is clicked so the taskbar
/// shows it at once (with nothing playing, the demo song plays meanwhile). Along the window's
/// bottom runs the taskbar in miniature, wearing the same choices. Any step can be passed by and
/// says where it lives in the settings; skipping keeps MusicBar as it comes.
/// </summary>
public partial class OnboardingWindow : Window
{
    sealed record Step(string NameKey, string Glyph, string WhereKey, Func<FrameworkElement> Build);

    static readonly Brush Card = Frozen(new SolidColorBrush(Color.FromArgb(0x0C, 255, 255, 255)));
    static readonly Brush CardHover = Frozen(new SolidColorBrush(Color.FromArgb(0x17, 255, 255, 255)));
    static readonly Brush Edge = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255)));
    static readonly Brush Screen = Frozen(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0E)));
    static readonly Color Coral = Color.FromRgb(0xFF, 0x5F, 0x6D), Amber = Color.FromRgb(0xFF, 0xC3, 0x71);

    // The steps in between the welcome and the end, the ones counted on the little seekbar.
    const int Counted = 6;

    readonly AppSettings _s;
    readonly MediaService _media;
    readonly Action _openSettings;
    readonly List<Step> _steps;
    int _index = -1;
    bool _skipped;

    // What the step on screen shows live, and how to mark the tiles that are on.
    readonly List<SeekRenderer> _engines = [];
    // Faces running through their expressions (the face step), and faces singing to the music (the welcome).
    readonly List<FaceView> _faces = [];
    readonly List<FaceView> _singers = [];
    readonly List<(Border Tile, Func<bool> On)> _tiles = [];
    readonly List<Action> _followers = [];

    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastFrame = -1;
    Color _a = Coral, _b = Amber;
    BitmapSource? _art;

    public OnboardingWindow(AppSettings settings, MediaService media, Action openSettings)
    {
        InitializeComponent();
        _s = settings;
        _media = media;
        _openSettings = openSettings;
        _steps =
        [
            new("", "", "", Welcome),
            new("Wel_Style_Name", "", "Wel_Style_Where", StylePage),
            new("Wel_Bar_Name", "", "Wel_Bar_Where", TaskbarPage),
            new("Wel_Player_Name", "", "Wel_Player_Where", PlayerPage),
            new("Wel_Lyrics_Name", "", "Wel_Lyrics_Where", LyricsPage),
            new("Wel_Face_Name", "", "Wel_Face_Where", FacePage),
            new("Wel_Habits_Name", "", "Wel_Habits_Where", HabitsPage),
            new("Wel_Done_Name", "", "", DonePage),
        ];

        // Nothing playing: the demo song, so every choice shows on the taskbar.
        _media.StartPreview();
        TakeColors();
        BuildGlows();
        BuildLanguages();
        BuildStrip();

        Inside.SizeChanged += (_, _) => Inside.Clip = new RectangleGeometry(new Rect(Inside.RenderSize), 18, 18);
        Header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.Right or Key.Enter && _index > 0 && _index < _steps.Count - 1) Go(_index + 1);
            else if (e.Key == Key.Left && _index > 0 && _index < _steps.Count - 1) Go(_index - 1);
        };
        _s.PropertyChanged += OnSettingChanged;
        Loc.Changed += OnLanguageChanged;
        _media.ThumbnailChanged += OnThumbnailChanged;
        CompositionTarget.Rendering += OnFrame;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            _s.PropertyChanged -= OnSettingChanged;
            Loc.Changed -= OnLanguageChanged;
            _media.ThumbnailChanged -= OnThumbnailChanged;
            _media.StopPreview();
            Log.Info(_skipped ? "Welcome tour skipped" : _index >= _steps.Count - 1 ? "Welcome tour done" : $"Welcome tour closed at step {_index}");
        };
        Go(0);
    }

    // ------------------------------------------------------------------ moving through the steps

    void Go(int index)
    {
        bool forward = index >= _index;
        _index = Math.Clamp(index, 0, _steps.Count - 1);
        var step = _steps[_index];
        _engines.Clear();
        _faces.Clear();
        _singers.Clear();
        _tiles.Clear();
        _followers.Clear();

        var page = step.Build();
        page.Opacity = 0;
        var slide = new TranslateTransform(forward ? 34 : -34, 0);
        page.RenderTransform = slide;
        Stage.Content = page;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        MarkTiles();

        // The welcome and the end bring their own buttons; the steps between share the footer.
        bool between = _index > 0 && _index < _steps.Count - 1;
        Footer.Visibility = between ? Visibility.Visible : Visibility.Hidden;
        SkipButton.Content = Loc.T("Wel_SkipAll");
        BackButton.Content = Loc.T("Wel_Back");
        NextButton.Content = Loc.T("Wel_Next");
        WherePill.Visibility = step.WhereKey.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        WhereText.Text = step.WhereKey.Length > 0 ? Loc.T("Wel_Later", Loc.T(step.WhereKey)) : "";
        ShowProgress();
        Stage.Focus();
    }

    /// <summary>The steps as a seekbar: a dot for each, filled up to this one, the playhead glowing on it.</summary>
    void ShowProgress()
    {
        var step = _steps[_index];
        StepLabel.Text = _index == 0 ? Loc.T("Wel_Window").ToUpperInvariant()
            : _index == _steps.Count - 1 ? Loc.T(step.NameKey).ToUpperInvariant()
            : $"{_index} / {Counted}  ·  {Loc.T(step.NameKey).ToUpperInvariant()}";
        Seek.UpdateLayout();
        double width = Math.Max(1, Seek.ActualWidth);
        double fraction = Math.Clamp(_index / (double)(Counted + 1), 0, 1);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        SeekFill.BeginAnimation(WidthProperty, new DoubleAnimation(width * fraction, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
        ((TranslateTransform)SeekHead.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(width * fraction - SeekHead.Width / 2, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
        SeekDots.Children.Clear();
        for (int i = 1; i <= Counted; i++)
        {
            double at = width * i / (Counted + 1);
            var dot = new Ellipse { Width = 5, Height = 5, Fill = i <= _index ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)) };
            Canvas.SetLeft(dot, at - 2.5);
            Canvas.SetTop(dot, 3.5);
            SeekDots.Children.Add(dot);
        }
    }

    void Next_Click(object sender, RoutedEventArgs e) => Go(_index + 1);

    void Back_Click(object sender, RoutedEventArgs e) => Go(_index - 1);

    void Skip_Click(object sender, RoutedEventArgs e) => Skip();

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Straight to the end: whatever was picked so far stays, the rest as MusicBar comes.</summary>
    void Skip()
    {
        _skipped = true;
        Go(_steps.Count - 1);
    }

    // ------------------------------------------------------------------ the steps

    FrameworkElement Welcome()
    {
        // It sings along to the music (poke it and it giggles).
        var face = new FaceView(11) { Look = FaceStyle.Classic, Accent = Coral, Width = 200, Height = 132 };
        _singers.Add(face);
        var top = new Grid { Height = 150, Margin = new Thickness(0, 2, 0, 0) };
        top.Children.Add(new Ellipse
        {
            Width = 300, Height = 170, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(ColorUtil.WithAlpha(Coral, 0.3), ColorUtil.WithAlpha(Coral, 0)),
        });
        top.Children.Add(face);

        var page = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        page.Children.Add(top);
        page.Children.Add(new TextBlock
        {
            Text = Loc.T("Wel_Hero"), FontSize = 46, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)FindResource("Display"),
            Foreground = (Brush)FindResource("Signature"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
        });
        page.Children.Add(new TextBlock
        {
            Text = Loc.T("Wel_HeroSub"), FontSize = 15, Opacity = 0.72, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 20), LineHeight = 22,
        });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 26) };
        foreach (var (glyph, key) in new[] { ("", "Wel_Chip_Bar"), ("", "Wel_Chip_Lyrics"), ("", "Wel_Chip_Center") })
            chips.Children.Add(Pill(glyph, Loc.T(key)));
        page.Children.Add(chips);

        var start = new Button
        {
            Style = (Style)FindResource("CardAccentButton"), Padding = new Thickness(34, 11, 30, 11), FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = Labelled(Loc.T("Wel_Start"), "", glyphAfter: true),
        };
        start.Click += (_, _) => Go(1);
        page.Children.Add(start);
        var skip = new Button
        {
            Style = (Style)FindResource("CardLinkButton"), Content = Loc.T("Wel_UseDefaults"),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
        };
        skip.Click += (_, _) => Skip();
        page.Children.Add(skip);
        if (_media.IsDemo)
            page.Children.Add(new TextBlock
            {
                Text = "♪  " + Loc.T("Wel_Demo"), FontSize = 11.5, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0),
            });
        return page;
    }

    FrameworkElement StylePage()
    {
        SeekStyle[] featured = [SeekStyle.Aurora, SeekStyle.Silk, SeekStyle.Spectrum, SeekStyle.Neon,
                                SeekStyle.Liquid, SeekStyle.Equalizer, SeekStyle.LedRainbow, SeekStyle.Classic];
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 6) };
        foreach (var style in featured)
        {
            var name = SettingsOptions.AllStyles.First(o => o.Value == style).Label;
            var body = new StackPanel();
            body.Children.Add(LookPreview(style, 44, new Thickness(7, 7, 7, 0)));
            body.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(11, 7, 9, 9), TextTrimming = TextTrimming.CharacterEllipsis });
            var value = style;
            grid.Children.Add(Tile(body, () => _s.Skin.Length == 0 && _s.SeekStyle == value, () =>
            {
                _s.SeekStyle = value;
                _s.Skin = "";
            }));
        }
        return Page("Wel_Style_Title", "Wel_Style_Sub", grid,
            SliderRow("Wel_Style_Strength", 0.15, 1, () => _s.FillOpacity, v => _s.FillOpacity = v, v => $"{v:P0}"));
    }

    FrameworkElement TaskbarPage()
    {
        var choices = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, -10, 0) };
        foreach (var (background, key, swatch) in new (BarBackground, string, Brush)[]
                 {
                     (BarBackground.Transparent, "Wel_Bg_Clear", Wallpaper()),
                     (BarBackground.Auto, "Wel_Bg_Windows", new LinearGradientBrush(
                         [new GradientStop(Color.FromRgb(0xF2, 0xF3, 0xF5), 0.5), new GradientStop(Color.FromRgb(0x1C, 0x1C, 0x22), 0.5)], 35)),
                     (BarBackground.AutoTinted, "Wel_Bg_Song", new LinearGradientBrush(ColorUtil.WithAlpha(_a, 0.9), ColorUtil.WithAlpha(_b, 0.9), 0)),
                     (BarBackground.Dark, "Wel_Bg_Dark", new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F))),
                 })
        {
            var body = new StackPanel();
            body.Children.Add(new Border { Height = 34, CornerRadius = new CornerRadius(7), Background = swatch, Margin = new Thickness(7, 7, 7, 0) });
            body.Children.Add(new TextBlock { Text = Loc.T(key), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(11, 7, 9, 9) });
            var value = background;
            choices.Children.Add(Tile(body, () => _s.Background == value, () => _s.Background = value));
        }
        // A taskbar that hides itself keeps its background, so the switch would seem to do nothing: say why.
        bool autoHides = (Application.Current as App)?.Overlay?.TaskbarAutoHides == true && _s.LayerMode == LayerMode.Auto;
        return Page("Wel_Bar_Title", "Wel_Bar_Sub",
            Toggle("Wel_Bar_Clear", autoHides ? "Wel_Bar_Clear_DescAutoHide" : "Wel_Bar_Clear_Desc", () => _s.TransparentTaskbar, v => _s.TransparentTaskbar = v),
            Toggle("Wel_Bar_Avoid", "Wel_Bar_Avoid_Desc", () => _s.SeekAvoidIcons, v => _s.SeekAvoidIcons = v),
            choices);
    }

    FrameworkElement PlayerPage()
    {
        var modes = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 8) };
        foreach (var (mode, key) in new[] { (PlayerMode.Full, "Wel_Mode_Full"), (PlayerMode.LyricsOnly, "Wel_Mode_Lyrics"), (PlayerMode.Off, "Wel_Mode_Off") })
        {
            var body = new StackPanel();
            body.Children.Add(PlayerSketch(mode));
            body.Children.Add(new TextBlock { Text = Loc.T(key), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(11, 7, 9, 9) });
            var value = mode;
            modes.Children.Add(Tile(body, () => _s.PlayerMode == value, () => _s.PlayerMode = value));
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, -8, 0) };
        foreach (var (style, key) in new[]
                 {
                     (ControlStyle.Minimal, "Wel_Ctl_Minimal"), (ControlStyle.Glow, "Wel_Ctl_Glow"), (ControlStyle.Capsule, "Wel_Ctl_Capsule"),
                     (ControlStyle.Ring, "Wel_Ctl_Ring"), (ControlStyle.Vinyl, "Wel_Ctl_Vinyl"), (ControlStyle.Keys, "Wel_Ctl_Keys"),
                     (ControlStyle.Bubbles, "Wel_Ctl_Bubbles"),
                 })
        {
            var value = style;
            var label = new TextBlock { Text = Loc.T(key), FontSize = 12.5, Margin = new Thickness(14, 7, 14, 8) };
            buttons.Children.Add(Tile(label, () => _s.ControlStyle == value, () => _s.ControlStyle = value, new Thickness(0, 0, 8, 8), radius: 16));
        }
        var caption = new TextBlock { Text = Loc.T("Wel_Player_Buttons"), FontSize = 12, Opacity = 0.6, Margin = new Thickness(2, 8, 0, 4) };
        return Page("Wel_Player_Title", "Wel_Player_Sub", modes, caption, buttons);
    }

    FrameworkElement LyricsPage()
    {
        string[] fonts = ["Segoe UI Variable Text", "Be Vietnam Pro", "Lexend", "Playfair Display", "Dancing Script", "Pacifico"];
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, -10, 2) };
        foreach (var font in fonts)
        {
            var body = new StackPanel { Margin = new Thickness(14, 11, 12, 10) };
            body.Children.Add(new TextBlock
            {
                Text = Loc.T("Wel_Lyrics_Sample"), FontFamily = BundledFonts.Resolve(font), FontSize = 18,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            body.Children.Add(new TextBlock { Text = font.Replace(" Variable Text", ""), FontSize = 11, Opacity = 0.55, Margin = new Thickness(0, 3, 0, 0) });
            var value = font;
            grid.Children.Add(Tile(body, () => _s.FontFamily == value, () => _s.FontFamily = value));
        }
        var toggles = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, 0) };
        toggles.Children.Add(Toggle("Wel_Lyrics_Show", null, () => _s.LyricsEnabled, v => _s.LyricsEnabled = v, new Thickness(0, 0, 8, 8)));
        toggles.Children.Add(Toggle("Wel_Lyrics_Karaoke", null, () => _s.Karaoke, v => _s.Karaoke = v, new Thickness(0, 0, 8, 8)));
        return Page("Wel_Lyrics_Title", "Wel_Lyrics_Sub", toggles, grid,
            SliderRow("Wel_Lyrics_Size", 0.8, 1.6, () => _s.LyricScale, v => _s.LyricScale = v, v => $"{v * 100:0}%"));
    }

    FrameworkElement FacePage()
    {
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, -10, 0) };
        int seed = 3;
        foreach (var style in new[] { FaceStyle.Classic, FaceStyle.Robot, FaceStyle.Cat, FaceStyle.AnimeGirl })
        {
            var face = new FaceView(seed += 7) { Look = style, Accent = _a, Touchable = false, IsHitTestVisible = false, Height = 92 };
            face.Brain.Demo = true;
            _faces.Add(face);
            var body = new StackPanel();
            body.Children.Add(new Border { Background = Screen, CornerRadius = new CornerRadius(8), Margin = new Thickness(7, 7, 7, 0), Child = face });
            body.Children.Add(new TextBlock
            {
                Text = SettingsOptions.FaceStyles.First(o => o.Value == style).Label, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(11, 7, 9, 9),
            });
            var value = style;
            grid.Children.Add(Tile(body, () => _s.FaceEnabled && _s.FaceStyle == value, () =>
            {
                _s.FaceStyle = value;
                _s.FaceEnabled = true;
            }));
        }
        return Page("Wel_Face_Title", "Wel_Face_Sub",
            Toggle("Wel_Face_Show", null, () => _s.FaceEnabled, v => _s.FaceEnabled = v), grid);
    }

    FrameworkElement HabitsPage() => Page("Wel_Habits_Title", "Wel_Habits_Sub",
        Toggle("Wel_Habit_Start", null, () => _s.StartWithWindows, v => _s.StartWithWindows = v),
        Toggle("Wel_Habit_Idle", null, () => _s.HideWhenIdle, v => _s.HideWhenIdle = v),
        Toggle("Wel_Habit_Center", "Wel_Habit_Center_Desc", () => _s.ControlCenter, v => _s.ControlCenter = v),
        Toggle("Wel_Habit_Dot", null, () => _s.NotificationDot, v => _s.NotificationDot = v));

    FrameworkElement DonePage()
    {
        var page = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        var badge = new Grid { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center };
        badge.Children.Add(new Ellipse { Fill = (Brush)FindResource("Signature") });
        badge.Children.Add(new TextBlock
        {
            Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 28, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x12)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        page.Children.Add(badge);
        page.Children.Add(new TextBlock
        {
            Text = Loc.T(_skipped ? "Wel_Skipped_Title" : "Wel_Done_Title"), FontSize = 30, FontWeight = FontWeights.SemiBold,
            FontFamily = (FontFamily)FindResource("Display"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 6),
        });
        page.Children.Add(new TextBlock { Text = Loc.T("Wel_Done_Sub"), FontSize = 14, Opacity = 0.66, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 22) });

        var tips = new UniformGrid { Columns = 3, Width = 760, Margin = new Thickness(0, 0, -10, 0) };
        foreach (var (glyph, key) in new[] { ("", "Wel_Tip_Tray"), ("", "Wel_Tip_Right"), ("", "Wel_Tip_Lyrics") })
        {
            var body = new StackPanel { Margin = new Thickness(16, 15, 14, 15) };
            body.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 20, Foreground = new SolidColorBrush(Amber) });
            body.Children.Add(new TextBlock { Text = Loc.T(key), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = Loc.T(key + "_Desc"), FontSize = 12, Opacity = 0.62, TextWrapping = TextWrapping.Wrap, LineHeight = 17 });
            tips.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0), Child = body });
        }
        page.Children.Add(tips);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 0) };
        var settings = new Button { Style = (Style)FindResource("CardSoftButton"), Padding = new Thickness(18, 10, 18, 10), Content = Loc.T("Wel_OpenSettings") };
        settings.Click += (_, _) =>
        {
            Close();
            _openSettings();
        };
        var finish = new Button
        {
            Style = (Style)FindResource("CardAccentButton"), Padding = new Thickness(28, 10, 26, 10), FontSize = 13.5, Margin = new Thickness(10, 0, 0, 0),
            Content = Labelled(Loc.T("Wel_Finish"), "", glyphAfter: false),
        };
        finish.Click += (_, _) => Close();
        buttons.Children.Add(settings);
        buttons.Children.Add(finish);
        page.Children.Add(buttons);
        return page;
    }

    // ------------------------------------------------------------------ building blocks

    FrameworkElement Page(string titleKey, string subKey, params UIElement[] body)
    {
        var page = new StackPanel();
        page.Children.Add(new TextBlock
        {
            Text = Loc.T(titleKey), FontSize = 28, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Display"),
            Margin = new Thickness(0, 4, 0, 6),
        });
        page.Children.Add(new TextBlock
        {
            Text = Loc.T(subKey), FontSize = 14, Opacity = 0.66, TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 20),
        });
        foreach (var part in body) page.Children.Add(part);
        return page;
    }

    /// <summary>A choice: a card that takes the signature colors round its edge while it is the one on.</summary>
    Border Tile(UIElement content, Func<bool> on, Action pick, Thickness? margin = null, double radius = 12)
    {
        var tile = new Border
        {
            CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1.5), Background = Card, Cursor = Cursors.Hand,
            Child = content, Margin = margin ?? new Thickness(0, 0, 10, 10),
        };
        tile.MouseEnter += (_, _) => tile.Background = CardHover;
        tile.MouseLeave += (_, _) => tile.Background = Card;
        tile.MouseLeftButtonUp += (_, _) => pick();
        _tiles.Add((tile, on));
        return tile;
    }

    void MarkTiles()
    {
        var signature = (Brush)FindResource("Signature");
        foreach (var (tile, on) in _tiles) tile.BorderBrush = on() ? signature : Edge;
    }

    /// <summary>A row with a switch; the whole row turns it.</summary>
    FrameworkElement Toggle(string titleKey, string? descriptionKey, Func<bool> get, Action<bool> set, Thickness? margin = null)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Loc.T(titleKey), FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (descriptionKey != null)
            text.Children.Add(new TextBlock { Text = Loc.T(descriptionKey), FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        var knob = Switch(get);
        Grid.SetColumn(knob, 1);
        grid.Children.Add(knob);
        var row = new Border
        {
            CornerRadius = new CornerRadius(12), Background = Card, Padding = new Thickness(18, 12, 16, 12), Cursor = Cursors.Hand,
            Margin = margin ?? new Thickness(0, 0, 0, 8), Child = grid,
        };
        row.MouseEnter += (_, _) => row.Background = CardHover;
        row.MouseLeave += (_, _) => row.Background = Card;
        row.MouseLeftButtonUp += (_, _) => set(!get());
        return row;
    }

    /// <summary>MusicBar's switch: the track fills with the signature colors when on, the knob slides across.</summary>
    FrameworkElement Switch(Func<bool> get)
    {
        var knob = new Ellipse { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) };
        var move = new TranslateTransform();
        knob.RenderTransform = move;
        var track = new Border
        {
            Width = 44, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.2),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Child = new Grid { Children = { knob } },
        };
        bool? shown = null;
        void Paint()
        {
            bool on = get();
            if (on == shown) return;
            bool animate = shown != null;
            shown = on;
            track.Background = on ? (Brush)FindResource("Signature") : Brushes.Transparent;
            track.BorderBrush = on ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255));
            knob.Fill = on ? new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x12)) : new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255));
            var to = on ? 20.0 : 0.0;
            if (animate) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase() });
            else move.X = to;
        }
        Paint();
        _followers.Add(Paint);
        return track;
    }

    FrameworkElement SliderRow(string labelKey, double min, double max, Func<double> get, Action<double> set, Func<double, string> show)
    {
        var slider = new Slider { Style = (Style)FindResource("Slim"), Minimum = min, Maximum = max, Value = get(), VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Text = show(get()), FontSize = 12.5, Opacity = 0.75, Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            value.Text = show(e.NewValue);
        };
        var grid = new Grid { Margin = new Thickness(2, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = Loc.T(labelKey), FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    /// <summary>A look running live on a little dark screen.</summary>
    FrameworkElement LookPreview(SeekStyle style, double height, Thickness margin)
    {
        var engine = new SeekRenderer { Look = style, ClipToBounds = true, Intensity = 0.75 };
        engine.SetEdge(true);
        engine.SetArt(_art);
        engine.SetTrack(style.ToString());
        var own = StylePalettes.For(_s, StylePalettes.KeyFor(style));
        engine.SetColors(own?.A ?? _a, own?.B ?? _b, animate: false, own?.C);
        _engines.Add(engine);
        var screen = new Border { Height = height, CornerRadius = new CornerRadius(8), Background = Screen, Margin = margin, Child = engine };
        screen.SizeChanged += (_, _) => screen.Clip = new RectangleGeometry(new Rect(screen.RenderSize), 8, 8);
        return screen;
    }

    /// <summary>The player modes drawn small: a cover, buttons and a line of words; words alone; or the bar alone.</summary>
    FrameworkElement PlayerSketch(PlayerMode mode)
    {
        var canvas = new Grid { Height = 52 };
        var line = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush(_b, _a, 0), Margin = new Thickness(0, 0, 0, 6) };
        canvas.Children.Add(line);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 4) };
        if (mode != PlayerMode.Off)
        {
            row.Children.Add(new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                Children =
                {
                    new Border { Width = mode == PlayerMode.Full ? 70 : 110, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Brushes.White, Opacity = 0.85 },
                    new Border { Width = mode == PlayerMode.Full ? 46 : 80, Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, Opacity = 0.35, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left },
                },
            });
        }
        if (mode == PlayerMode.Full)
        {
            row.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = _art != null ? new ImageBrush(_art) : new SolidColorBrush(_a) });
            foreach (var glyph in new[] { "", "", "" })
                row.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 });
        }
        canvas.Children.Add(row);
        var screen = new Border { CornerRadius = new CornerRadius(8), Background = Screen, Margin = new Thickness(7, 7, 7, 0), Child = canvas };
        screen.SizeChanged += (_, _) => screen.Clip = new RectangleGeometry(new Rect(screen.RenderSize), 8, 8);
        return screen;
    }

    FrameworkElement Pill(string glyph, string text) => new Border
    {
        CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255)),
        BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 14, 6), Margin = new Thickness(5, 0, 5, 0),
        Child = Labelled(text, glyph, glyphAfter: false, size: 12.5),
    };

    StackPanel Labelled(string text, string glyph, bool glyphAfter, double size = 0)
    {
        var icon = new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Margin = glyphAfter ? new Thickness(9, 1, 0, 0) : new Thickness(0, 1, 8, 0),
        };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (size > 0) label.FontSize = size;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (!glyphAfter) row.Children.Add(icon);
        row.Children.Add(label);
        if (glyphAfter) row.Children.Add(icon);
        return row;
    }

    /// <summary>A desktop behind a clear taskbar: a dusk of the signature colors.</summary>
    Brush Wallpaper() => new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0x2B, 0x1E, 0x4A), 0), new GradientStop(Color.FromRgb(0x8A, 0x3B, 0x6E), 0.55), new GradientStop(Color.FromRgb(0xE8, 0x7A, 0x5D), 1)],
        new Point(0, 0), new Point(1, 1));

    // ------------------------------------------------------------------ the header's language switch

    void BuildLanguages()
    {
        Languages.Children.Clear();
        foreach (var (language, label) in new[] { (AppLanguage.English, "EN"), (AppLanguage.Vietnamese, "VI") })
        {
            bool on = _s.Language == language;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 10, 4), Cursor = Cursors.Hand,
                Background = on ? new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)) : Brushes.Transparent,
                Child = new TextBlock { Text = label, FontSize = 11.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Opacity = on ? 1 : 0.6 },
            };
            var value = language;
            pill.MouseLeftButtonUp += (_, _) => _s.Language = value;
            Languages.Children.Add(pill);
        }
    }

    void OnLanguageChanged()
    {
        BuildLanguages();
        UpdateStrip();
        Go(_index);
    }

    // ------------------------------------------------------------------ the taskbar in miniature

    readonly Border _stripBack = new();
    readonly Border _stripTint = new() { Opacity = 0.32 };
    readonly SeekRenderer _stripSeek = new() { ClipToBounds = true };
    readonly StackPanel _stripIcons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly FaceView _stripFace = new(5) { Width = 52, Height = 40, Touchable = false, IsHitTestVisible = false };
    readonly TextBlock _stripLyric = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    readonly Border _stripCover = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 4, 0) };
    readonly StackPanel _stripButtons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _stripPlayer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _stripClock = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly LinearGradientBrush _sung = new() { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };

    void BuildStrip()
    {
        _stripSeek.SetEdge(true);
        _stripSeek.SetTrack("welcome");
        Strip.Children.Add(_stripBack);
        Strip.Children.Add(_stripTint);
        Strip.Children.Add(_stripSeek);

        // The middle: Start and a few app icons, the face beside them when it is on.
        _stripIcons.Children.Add(_stripFace);
        var start = new UniformGrid { Columns = 2, Width = 16, Height = 16, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++) start.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)), Margin = new Thickness(0.8), CornerRadius = new CornerRadius(1) });
        _stripIcons.Children.Add(start);
        foreach (var color in new[] { Color.FromRgb(0xFF, 0xC8, 0x3D), Color.FromRgb(0x3D, 0xA5, 0xFF), Color.FromRgb(0x5B, 0xD6, 0x8A), Color.FromRgb(0xC9, 0x7B, 0xFF) })
            _stripIcons.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(color), Margin = new Thickness(5, 0, 5, 0), Opacity = 0.9 });
        Strip.Children.Add(_stripIcons);

        // The right: the player and lyrics, then the tray's clock.
        foreach (var glyph in new[] { "", "", "" })
            _stripButtons.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _stripPlayer.Children.Add(_stripLyric);
        _stripPlayer.Children.Add(_stripCover);
        _stripPlayer.Children.Add(_stripButtons);
        _stripPlayer.Children.Add(new Border { Width = 92 });
        Strip.Children.Add(_stripPlayer);
        _stripClock.Text = "♪  9:41";
        _stripClock.HorizontalAlignment = HorizontalAlignment.Right;
        _stripClock.Margin = new Thickness(0, 0, 22, 0);
        Strip.Children.Add(_stripClock);
        UpdateStrip();
    }

    /// <summary>The miniature wears the settings as they are now.</summary>
    void UpdateStrip()
    {
        bool windowsLight = ColorUtil.WindowsTaskbarIsLight();
        var background = _s.Background;
        bool clear = _s.TransparentTaskbar && background == BarBackground.Transparent;
        bool light = background is BarBackground.Light or BarBackground.LightTinted
                     || (background is BarBackground.Auto or BarBackground.AutoTinted && windowsLight);
        _stripBack.Background = clear ? Wallpaper()
            : background == BarBackground.Transparent ? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24))
            : new SolidColorBrush(light ? Color.FromRgb(0xF2, 0xF3, 0xF5) : Color.FromRgb(0x0B, 0x0B, 0x0F));
        bool tinted = background is BarBackground.Tinted or BarBackground.LightTinted or BarBackground.AutoTinted;
        _stripTint.Background = tinted ? new LinearGradientBrush(_b, _a, 0) : null;

        var ink = light ? Color.FromRgb(0x14, 0x14, 0x18) : Color.FromRgb(0xFA, 0xFA, 0xFA);
        _stripSeek.Look = _s.SeekStyle;
        _stripSeek.Intensity = _s.FillOpacity;
        var own = StylePalettes.For(_s, StylePalettes.KeyFor(_s.SeekStyle));
        _stripSeek.SetColors(own?.A ?? _a, own?.B ?? _b, animate: true, own?.C);
        _stripSeek.SetArt(_art);

        _stripFace.Visibility = _s.FaceEnabled ? Visibility.Visible : Visibility.Collapsed;
        _stripFace.Look = _s.FaceStyle == FaceStyle.Custom ? FaceStyle.Classic : _s.FaceStyle;
        _stripFace.Accent = _a;
        _stripFace.Ink = ink;

        bool player = _s.PlayerMode == PlayerMode.Full;
        _stripPlayer.Visibility = _s.PlayerMode == PlayerMode.Off ? Visibility.Collapsed : Visibility.Visible;
        _stripCover.Visibility = _stripButtons.Visibility = player ? Visibility.Visible : Visibility.Collapsed;
        _stripCover.Background = _art != null ? new ImageBrush(_art) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(_a);
        foreach (TextBlock glyph in _stripButtons.Children) glyph.Foreground = new SolidColorBrush(ink);
        _stripClock.Foreground = new SolidColorBrush(ink);
        _stripClock.Opacity = 0.75;

        _stripLyric.Visibility = _s.LyricsEnabled ? Visibility.Visible : Visibility.Collapsed;
        _stripLyric.Text = Loc.T("Wel_Lyrics_Sample");
        _stripLyric.FontFamily = BundledFonts.Resolve(_s.FontFamily);
        _stripLyric.FontSize = 14 * _s.LyricScale;
        _sung.GradientStops.Clear();
        var sung = ColorUtil.Lerp(_a, Colors.White, 0.15);
        _sung.GradientStops.Add(new GradientStop(sung, 0));
        _sung.GradientStops.Add(new GradientStop(sung, 0));
        _sung.GradientStops.Add(new GradientStop(ink, 0));
        _sung.GradientStops.Add(new GradientStop(ink, 1));
        _stripLyric.Foreground = _s.Karaoke ? _sung : new SolidColorBrush(ink);
    }

    // ------------------------------------------------------------------ live

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkTiles();
        foreach (var follow in _followers) follow();
        UpdateStrip();
        if (e.PropertyName == nameof(AppSettings.Language)) return;
    }

    void OnThumbnailChanged(object? sender, EventArgs e)
    {
        TakeColors();
        foreach (var engine in _engines)
        {
            engine.SetArt(_art);
            engine.SetColors(_a, _b, animate: true);
        }
        UpdateStrip();
    }

    /// <summary>The colors of what plays (the demo song, or the user's own), as the bar takes them.</summary>
    void TakeColors()
    {
        if ((Application.Current as App)?.Overlay?.PreviewPalette() is { } live)
        {
            (_a, _b, _art) = (live.Primary, live.Secondary, live.Art);
            return;
        }
        _art = _media.Thumbnail;
        if (_art != null && ColorExtractor.FromBitmap(_art) is { } palette) (_a, _b) = (palette.Primary, palette.Secondary);
    }

    readonly List<(Ellipse Glow, double Phase)> _glows = [];

    void BuildGlows()
    {
        foreach (var (color, size, phase) in new[] { (Coral, 520.0, 0.0), (Amber, 440.0, 2.4) })
        {
            var glow = new Ellipse
            {
                Width = size, Height = size * 0.8,
                Fill = new RadialGradientBrush(ColorUtil.WithAlpha(color, 0.16), ColorUtil.WithAlpha(color, 0)),
            };
            Glows.Children.Add(glow);
            _glows.Add((glow, phase));
        }
    }

    /// <summary>30 times a second: the previews dance to made-up music, the glows drift, the miniature sings.</summary>
    void OnFrame(object? sender, EventArgs e)
    {
        double t = _clock.Elapsed.TotalSeconds;
        if (_lastFrame >= 0 && t - _lastFrame < 1 / 31.0) return;
        _lastFrame = t;
        if (!IsVisible) return;

        double progress = 0.25 + (t * 0.02) % 0.6;
        foreach (var engine in _engines)
        {
            engine.SetState(progress, timeline: true, playing: true);
            engine.Tick();
        }
        _stripSeek.SetState(_media.HasSession && _media.Duration > TimeSpan.Zero
            ? Math.Clamp(_media.GetPosition().TotalSeconds / _media.Duration.TotalSeconds, 0, 1) : progress, timeline: true, playing: true);
        _stripSeek.Tick();

        var previews = new FaceSenses(DateTime.Now, true, false, -1, TimeSpan.Zero, null, false);
        foreach (var face in _faces) face.Step(previews);
        // Music on, swelling and easing like a song.
        var singing = new FaceSenses(DateTime.Now, true, true, 0.45 + 0.3 * Math.Sin(t * 2.3) * Math.Sin(t * 0.7), TimeSpan.Zero, null, false);
        foreach (var face in _singers) face.Step(singing);
        if (_stripFace.Visibility == Visibility.Visible) _stripFace.Step(singing);

        // The sample line sings itself over and over: three seconds of coloring, a breath, again.
        double k = Math.Clamp((t % 4.2) / 3.0, 0, 1);
        if (_sung.GradientStops.Count == 4)
        {
            _sung.GradientStops[1].Offset = k;
            _sung.GradientStops[2].Offset = Math.Min(1, k + 0.02);
        }

        double w = Inside.ActualWidth, h = Inside.ActualHeight;
        foreach (var (glow, phase) in _glows)
        {
            Canvas.SetLeft(glow, w * (0.15 + 0.5 * (0.5 + 0.5 * Math.Sin(t * 0.07 + phase))) - glow.Width / 2);
            Canvas.SetTop(glow, h * (0.25 + 0.4 * (0.5 + 0.5 * Math.Cos(t * 0.05 + phase * 1.3))) - glow.Height / 2);
        }
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
