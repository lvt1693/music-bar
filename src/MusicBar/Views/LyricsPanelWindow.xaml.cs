using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>What the lyrics panel needs from the bar that opened it.</summary>
public interface ILyricsHost
{
    MediaService Media { get; }

    LyricsService LyricsSource { get; }

    LyricsStatus LyricsState { get; }

    /// <summary>The lyrics found for the song playing, if any.</summary>
    LyricsResult? Lyrics { get; }

    /// <summary>How far ahead of the player the lyrics run: the global setting plus this song's own correction.</summary>
    TimeSpan LyricsLead { get; }

    TrackQuery CurrentQuery();

    /// <summary>Moves this song's lyrics earlier (positive) or later, and keeps it for next time.</summary>
    void NudgeLyrics(int ms);

    /// <summary>Shows lyrics the user picked by hand, from now on.</summary>
    void UseLyrics(LyricsResult result);

    /// <summary>A new song, or new lyrics for it.</summary>
    event Action? LyricsChanged;
}

/// <summary>
/// The whole song's lyrics in a card beside the taskbar: the line being sung lit and kept in the
/// middle, a click on any line seeks there. It also corrects the timing, shows the romanization
/// of Korean, Japanese and Chinese songs, and searches LRCLIB by hand when the wrong song was found.
/// </summary>
public partial class LyricsPanelWindow : Window
{
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });

    sealed class Row
    {
        public required LyricLine Line;
        public required Border Box;
        public required TextBlock Main;
        public required TextBlock Sub;
    }

    readonly ILyricsHost _host;
    readonly AppSettings _s;
    readonly DispatcherTimer _clock = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly List<Row> _rows = [];
    // Replaced, not changed: a brush put in Resources gets frozen.
    SolidColorBrush _accent = new(ColorUtil.Brand);
    CancellationTokenSource? _work;
    int _current = -2;
    DateTime _userScrolled = DateTime.MinValue;
    double? _scrollTarget;
    TaskbarEdge _edge = TaskbarEdge.Bottom;

    public LyricsPanelWindow(ILyricsHost host, AppSettings settings)
    {
        CpuRendering.Use(this);
        _host = host;
        _s = settings;
        InitializeComponent();
        Resources["Accent"] = _accent;
        _clock.Tick += (_, _) => Tick();
        _repeat.Tick += Repeat_Tick;
        WireRepeat(EarlierButton, +250);
        WireRepeat(LaterButton, -250);
        _host.LyricsChanged += OnLyricsChanged;
        Deactivated += (_, _) => Dismiss();
        Closed += (_, _) =>
        {
            _host.LyricsChanged -= OnLyricsChanged;
            _clock.Stop();
            _work?.Cancel();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (SearchView.IsVisible) ShowLyricsView();
            else Dismiss();
            e.Handled = true;
        };
        SourceInitialized += (_, _) =>
            Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW, Native.WS_EX_APPWINDOW);
    }

    public bool IsOpen { get; private set; }

    public void SetAccent(Color accent)
    {
        _accent = new SolidColorBrush(ColorUtil.ForDarkSurface(accent));
        Resources["Accent"] = _accent;
        _current = -2;
    }

    /// <summary>Opens beside the taskbar near <paramref name="anchor"/> (screen pixels), on the song playing.</summary>
    internal void ShowAt(Point anchor, TaskbarEdge edge, Native.RECT taskbar, Native.RECT monitor)
    {
        _edge = edge;
        Root.RenderTransformOrigin = edge switch
        {
            TaskbarEdge.Top => new Point(0.5, 0),
            TaskbarEdge.Left => new Point(0, 0.5),
            TaskbarEdge.Right => new Point(1, 0.5),
            _ => new Point(0.5, 1),
        };
        ShowLyricsView();
        Fill();
        if (!IsVisible) Show();
        UpdateLayout();
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var margin = new Thickness(Root.Margin.Left * m.M11, Root.Margin.Top * m.M22, Root.Margin.Right * m.M11, Root.Margin.Bottom * m.M22);
        var (x, y) = TaskbarSide.CardSpot((int)Math.Round(ActualWidth * m.M11), (int)Math.Round(ActualHeight * m.M22), margin,
            (int)Math.Round(8 * m.M11), anchor, edge, taskbar, monitor);
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE);
        Activate();

        IsOpen = true;
        var d = TimeSpan.FromMilliseconds(300);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, d) { EasingFunction = Spring });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, d) { EasingFunction = Spring });
        var (axis, toward) = TaskbarSide.Slide(edge);
        RootShift.BeginAnimation(axis == TranslateTransform.XProperty ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
        RootShift.BeginAnimation(axis, new DoubleAnimation(toward * 14, 0, d) { EasingFunction = EaseOut });
        _clock.Start();
        // Jump straight to the line being sung.
        _current = -2;
        _scrollTarget = null;
        Tick();
        if (_scrollTarget is double target) LineScroll.ScrollToVerticalOffset(target);
    }

    public void Dismiss()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _clock.Stop();
        _work?.Cancel();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) =>
        {
            if (!IsOpen) Hide();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        var (axis, toward) = TaskbarSide.Slide(_edge);
        RootShift.BeginAnimation(axis, new DoubleAnimation(toward * 8, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut });
    }

    void OnLyricsChanged()
    {
        if (!IsOpen) return;
        if (SearchView.IsVisible || ShareView.IsVisible) return;
        Fill();
        _current = -2;
    }

    // ------------------------------------------------------------------ the song's lines

    /// <summary>Puts the song playing and its lyrics (or why there are none) on the card.</summary>
    void Fill()
    {
        var media = _host.Media;
        SongTitle.Text = media.Title.Length > 0 ? media.Title : media.AppName;
        // Where the lyrics came from, after the artist.
        var from = LyricsService.OriginOf(_host.Lyrics) is { } origin ? origin == "file" ? Loc.T("LyrP_FromFile") : origin : null;
        SongArtist.Text = from == null || _host.Lyrics?.Lyrics == null ? media.Artist : Loc.T("LyrP_From", media.Artist, from);
        ShareLink.Visibility = _host.LyricsSource.DraftFor(_host.Lyrics, _host.CurrentQuery()) != null ? Visibility.Visible : Visibility.Collapsed;
        CoverBrush.ImageSource = media.Thumbnail;
        Cover.Visibility = media.Thumbnail != null ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Text = $"{TitleCleaner.Clean(media.Artist, media.Title).Artist} {TitleCleaner.Clean(media.Artist, media.Title).Title}".Trim();

        _rows.Clear();
        LinePanel.Children.Clear();
        var result = _host.Lyrics;
        bool synced = result?.Lyrics is { Lines.Count: > 0 };
        OffsetPanel.Visibility = synced ? Visibility.Visible : Visibility.Hidden;
        RomanChip.Visibility = Visibility.Collapsed;
        OthersLink.Visibility = media.HasSession && !media.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        UpdateOffsetText();

        if (synced)
        {
            ShowMessage(null);
            var font = BundledFonts.Resolve(_s.FontFamily);
            foreach (var line in result!.Lyrics!.Lines)
            {
                if (line.IsBlank) continue;
                AddRow(line, font);
            }
            Romanize(result.Lyrics.Lines.Where(l => !l.IsBlank).Select(l => l.Text).ToList());
            return;
        }

        if (result is { Status: LyricsStatus.PlainOnly, FilePath: { } plain } && File.Exists(plain))
        {
            ShowMessage(null);
            try
            {
                var font = BundledFonts.Resolve(_s.FontFamily);
                foreach (var text in File.ReadAllLines(plain).Where(t => t.Trim().Length > 0))
                    AddRow(new LyricLine(TimeSpan.MinValue, text.Trim()), font);
                Romanize(_rows.Select(r => r.Line.Text).ToList());
            }
            catch (Exception ex)
            {
                Log.Error("Reading plain lyrics failed", ex);
            }
            return;
        }

        string message = !media.HasSession ? Loc.T("Msg_NothingPlaying")
            : _host.LyricsState switch
            {
                LyricsStatus.Loading => Loc.T("LyrP_Loading"),
                LyricsStatus.Instrumental => Loc.T("LyrP_Instrumental"),
                LyricsStatus.Error => Loc.T("LyrP_Error"),
                LyricsStatus.None when !_s.LyricsEnabled => Loc.T("LyrP_Off"),
                _ => Loc.T("LyrP_NotFound"),
            };
        ShowMessage(message);
        MessageSearch.Visibility = media.HasSession && !media.IsDemo && _host.LyricsState != LyricsStatus.Loading
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void ShowMessage(string? text)
    {
        MessagePanel.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        LineScroll.Visibility = text == null ? Visibility.Visible : Visibility.Hidden;
        MessageText.Text = text ?? "";
    }

    void AddRow(LyricLine line, FontFamily font)
    {
        var main = new TextBlock
        {
            Text = line.Text,
            FontFamily = font,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
        };
        var sub = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Opacity = 0.62,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        var stack = new StackPanel();
        stack.Children.Add(main);
        stack.Children.Add(sub);
        var box = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = stack,
            Opacity = 0.72,
        };
        bool timed = line.Start != TimeSpan.MinValue;
        if (timed)
        {
            box.Cursor = Cursors.Hand;
            box.ToolTip = Loc.T("LyrP_SeekTip");
            box.MouseEnter += (_, _) => box.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            box.MouseLeave += (_, _) => box.Background = Brushes.Transparent;
            box.MouseLeftButtonUp += (_, _) => SeekTo(line);
        }
        _rows.Add(new Row { Line = line, Box = box, Main = main, Sub = sub });
        LinePanel.Children.Add(box);
    }

    /// <summary>Plays from the start of a line: the player's time is the line's, less the lead the lyrics run with.</summary>
    void SeekTo(LyricLine line)
    {
        var target = line.Start - _host.LyricsLead;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        _ = _host.Media.SeekAsync(target);
        _userScrolled = DateTime.MinValue;
        _current = -2;
    }

    /// <summary>Every frame while open: lights the line being sung and glides it to the middle.</summary>
    void Tick()
    {
        if (_host.Lyrics?.Lyrics is { } lyrics && _rows.Count > 0 && _rows[0].Line.Start != TimeSpan.MinValue)
        {
            var at = _host.Media.GetPosition() + _host.LyricsLead;
            int index = lyrics.IndexAt(at);
            var line = index >= 0 ? lyrics.Lines[index] : null;
            int row = line == null ? -1 : _rows.FindLastIndex(r => r.Line.Start <= line.Start);
            if (row != _current)
            {
                _current = row;
                for (int i = 0; i < _rows.Count; i++)
                {
                    var r = _rows[i];
                    bool now = i == row;
                    r.Main.Foreground = now ? _accent : Brushes.White;
                    r.Box.BeginAnimation(OpacityProperty, new DoubleAnimation(now ? 1 : i < row ? 0.42 : 0.72, TimeSpan.FromMilliseconds(220)));
                }
                if (row >= 0 && DateTime.Now - _userScrolled > TimeSpan.FromSeconds(4))
                {
                    var box = _rows[row].Box;
                    var top = box.TranslatePoint(new Point(0, 0), LinePanel).Y + LinePanel.Margin.Top;
                    _scrollTarget = Math.Clamp(top - (LineScroll.ViewportHeight - box.ActualHeight) * 0.42, 0, LineScroll.ScrollableHeight);
                }
            }
        }
        if (_scrollTarget is double target)
        {
            double y = LineScroll.VerticalOffset;
            double next = Math.Abs(target - y) < 1 ? target : y + (target - y) * 0.18;
            LineScroll.ScrollToVerticalOffset(next);
            if (next == target) _scrollTarget = null;
        }
    }

    void LineScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // A hand on the wheel takes over for a few seconds.
        _userScrolled = DateTime.Now;
        _scrollTarget = null;
    }

    // ------------------------------------------------------------------ romanization

    void Romanize(IReadOnlyList<string> lines)
    {
        _work?.Cancel();
        var cts = new CancellationTokenSource();
        _work = cts;
        var rows = _rows.ToList();
        _ = Task.Run(() => LyricRomanization.ForLines(lines), cts.Token).ContinueWith(t =>
        {
            if (cts.IsCancellationRequested || t.Status != TaskStatus.RanToCompletion || t.Result is not { } roman) return;
            for (int i = 0; i < rows.Count && i < roman.Length; i++)
            {
                rows[i].Sub.Text = roman[i] ?? "";
                rows[i].Sub.Tag = roman[i];
            }
            RomanChip.Visibility = Visibility.Visible;
            RomanChip.IsChecked = _s.LyricsPanelRomanized;
            ShowRomanization();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void ShowRomanization()
    {
        foreach (var r in _rows)
            r.Sub.Visibility = _s.LyricsPanelRomanized && r.Sub.Tag is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _current = -2;
    }

    void RomanChip_Click(object sender, RoutedEventArgs e)
    {
        _s.LyricsPanelRomanized = RomanChip.IsChecked == true;
        ShowRomanization();
    }

    // ------------------------------------------------------------------ timing

    void UpdateOffsetText()
    {
        int ms = (int)(_host.Lyrics?.Lyrics?.Offset.TotalMilliseconds ?? 0);
        OffsetText.Text = LyricsService.DescribeTiming(ms);
    }

    void Earlier_Click(object sender, RoutedEventArgs e)
    {
        if (!_held) Nudge(+250);
        _held = false;
    }

    void Later_Click(object sender, RoutedEventArgs e)
    {
        if (!_held) Nudge(-250);
        _held = false;
    }

    // Holding a nudge button repeats it, slowly at first and then faster, so a big correction
    // takes one press instead of a dozen clicks.
    readonly DispatcherTimer _repeat = new();
    int _repeatStep, _repeatCount;
    // The press turned into a hold: the click that ends it adds nothing more.
    bool _held;

    void WireRepeat(Button button, int step)
    {
        button.PreviewMouseLeftButtonDown += (_, _) =>
        {
            _held = false;
            _repeatStep = step;
            _repeatCount = 0;
            _repeat.Interval = TimeSpan.FromMilliseconds(420);
            _repeat.Start();
        };
        button.PreviewMouseLeftButtonUp += (_, _) => _repeat.Stop();
        button.MouseLeave += (_, _) => _repeat.Stop();
        button.LostMouseCapture += (_, _) => _repeat.Stop();
    }

    void Repeat_Tick(object? sender, EventArgs e)
    {
        _held = true;
        _repeatCount++;
        _repeat.Interval = TimeSpan.FromMilliseconds(_repeatCount < 6 ? 140 : _repeatCount < 16 ? 90 : 55);
        Nudge(_repeatStep);
    }

    void OffsetText_Click(object sender, MouseButtonEventArgs e)
    {
        int ms = (int)(_host.Lyrics?.Lyrics?.Offset.TotalMilliseconds ?? 0);
        if (ms != 0) Nudge(-ms);
    }

    void Nudge(int ms)
    {
        _host.NudgeLyrics(ms);
        UpdateOffsetText();
        _current = -2;
        // Corrected timing is worth sharing.
        ShareLink.Visibility = _host.LyricsSource.DraftFor(_host.Lyrics, _host.CurrentQuery()) != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ search by hand

    void Search_Click(object sender, RoutedEventArgs e)
    {
        LyricsView.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Visible;
        SearchNote.Text = Loc.T("LyrP_SearchHint");
        SearchBox.Focus();
        SearchBox.SelectAll();
        RunSearch();
    }

    void Back_Click(object sender, RoutedEventArgs e) => ShowLyricsView();

    void ShowLyricsView()
    {
        SearchView.Visibility = Visibility.Collapsed;
        ShareView.Visibility = Visibility.Collapsed;
        LyricsView.Visibility = Visibility.Visible;
        Footer.Visibility = Visibility.Visible;
    }

    void Find_Click(object sender, RoutedEventArgs e) => RunSearch();

    void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        RunSearch();
        e.Handled = true;
    }

    async void RunSearch()
    {
        _work?.Cancel();
        var cts = new CancellationTokenSource();
        _work = cts;
        ResultPanel.Children.Clear();
        SearchNote.Text = Loc.T("LyrP_Searching");
        IReadOnlyList<LyricsCandidate> found;
        try
        {
            found = await _host.LyricsSource.SearchAsync(SearchBox.Text, _host.CurrentQuery(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error("Lyrics search failed", ex);
            if (!cts.IsCancellationRequested) SearchNote.Text = Loc.T("LyrP_SearchFailed");
            return;
        }
        if (cts.IsCancellationRequested) return;
        SearchNote.Text = found.Count == 0 ? Loc.T("LyrP_NoResults") : Loc.T("LyrP_PickOne");
        foreach (var pick in found) ResultPanel.Children.Add(ResultRow(pick));
    }

    FrameworkElement ResultRow(LyricsCandidate pick)
    {
        var title = new TextBlock { Text = pick.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var parts = new List<string>();
        if (pick.Artist.Length > 0) parts.Add(pick.Artist);
        if (pick.Album.Length > 0) parts.Add(pick.Album);
        if (pick.Duration > TimeSpan.Zero) parts.Add(pick.Duration.ToString(pick.Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"));
        var detail = new TextBlock { Text = string.Join(" · ", parts), FontSize = 11.5, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };

        // NetEase songs only show whether they have synced lyrics once picked: they are marked by their source.
        string kind = pick.From == "NetEase" ? "NetEase" : pick.Synced ? Loc.T("LyrP_Synced") : pick.Instrumental ? Loc.T("LyrP_NoWords") : Loc.T("LyrP_WordsOnly");
        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 1, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = pick.Synced ? _accent : new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock { Text = kind, FontSize = 10.5, Foreground = pick.Synced ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) : Brushes.White },
        };
        // Close to the song's length is the best hint that it is the same recording.
        var length = _host.Media.Duration;
        if (length > TimeSpan.Zero && pick.Duration > TimeSpan.Zero && Math.Abs((pick.Duration - length).TotalSeconds) <= 3)
            detail.Text += "  ✓";

        var text = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        text.Children.Add(title);
        text.Children.Add(detail);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        var row = new Border
        {
            Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += async (_, _) =>
        {
            _work?.Cancel();
            var cts = new CancellationTokenSource();
            _work = cts;
            LyricsResult? result;
            try
            {
                result = await _host.LyricsSource.UseAsync(pick, _host.CurrentQuery(), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Fetching picked lyrics failed", ex);
                SearchNote.Text = Loc.T("LyrP_SearchFailed");
                return;
            }
            if (result == null)
            {
                SearchNote.Text = Loc.T("LyrP_NetEaseNone");
                return;
            }
            Log.Info($"Lyrics picked by hand: '{pick.Artist} - {pick.Title}' from {pick.From} ({result.Status})");
            _host.UseLyrics(result);
            ShowLyricsView();
            Fill();
            _current = -2;
        };
        return row;
    }

    // ------------------------------------------------------------------ sharing on LRCLIB

    LyricsDraft? _draft;

    void Share_Click(object sender, RoutedEventArgs e)
    {
        _draft = _host.LyricsSource.DraftFor(_host.Lyrics, _host.CurrentQuery());
        if (_draft == null) return;
        LyricsView.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Collapsed;
        ShareView.Visibility = Visibility.Visible;
        ShareTitle.Text = _draft.Title;
        ShareArtist.Text = _draft.Artist;
        ShareAlbum.Text = _draft.Album;
        var length = _draft.Duration.ToString(_draft.Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        ShareFacts.Text = _draft.OffsetMs == 0
            ? Loc.T("LyrS_Facts", _draft.Lines, length)
            : Loc.T("LyrS_FactsOffset", _draft.Lines, length, LyricsService.DescribeTiming(_draft.OffsetMs));
        ShareStatus.Visibility = Visibility.Collapsed;
        ShareButton.IsEnabled = true;
    }

    async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        var draft = _draft with { Title = ShareTitle.Text.Trim(), Artist = ShareArtist.Text.Trim(), Album = ShareAlbum.Text.Trim() };
        ShareStatus.Visibility = Visibility.Visible;
        ShareStatus.Foreground = Brushes.White;
        if (LyricsService.Problem(draft) is { } problem)
        {
            ShareStatus.Text = Loc.T(problem);
            ShareStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x96));
            return;
        }
        _work?.Cancel();
        var cts = new CancellationTokenSource();
        _work = cts;
        ShareButton.IsEnabled = false;
        var stage = new Progress<string>(s => ShareStatus.Text = Loc.T("LyrS_Stage_" + s));
        try
        {
            await _host.LyricsSource.PublishAsync(draft, stage, cts.Token);
            ShareStatus.Text = Loc.T("LyrS_Done");
            ShareStatus.Foreground = _accent;
        }
        catch (OperationCanceledException)
        {
            ShareButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error("Sharing lyrics on LRCLIB failed", ex);
            // LRCLIB says why it turned the lyrics down; anything else is the connection.
            ShareStatus.Text = ex is LyricsService.RefusedException refused ? Loc.T("LyrS_Refused", refused.Reason) : Loc.T("LyrS_Failed");
            ShareStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x96));
            ShareButton.IsEnabled = true;
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Dismiss();

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
