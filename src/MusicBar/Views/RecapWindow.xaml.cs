using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;
using MusicBar.Views.Face;

namespace MusicBar.Views;

/// <summary>
/// The year in music on one card, the size of a portrait post: hours listened, the top songs and
/// artists, the biggest day, the golden hour, the longest streak and what kind of listener the
/// year shows, with the face singing into its mic. Save or copy it as a picture to share.
/// </summary>
public partial class RecapWindow : Window
{
    readonly YearRecap _recap;
    readonly AppSettings _s;
    readonly DispatcherTimer _clock = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly CultureInfo _culture;

    public RecapWindow(YearRecap recap, AppSettings settings, Color accent)
    {
        _recap = recap;
        _s = settings;
        _culture = CultureInfo.GetCultureInfo(settings.Language == AppLanguage.Vietnamese ? "vi-VN" : "en-US");
        InitializeComponent();
        // The card is a fixed-size picture, so on a small screen the window is scaled down; the saved picture stays full size.
        ScreenFit.AttachScaled(this);
        Paint(ColorUtil.ForDarkSurface(accent));
        Fill();

        Face.Look = settings.FaceStyle;
        Face.Outfit = FaceWindow.OutfitFor(settings, DateTime.Now);
        Face.Brain.FollowsClock = false;
        Face.Brain.HoldsMic = true;
        _clock.Tick += (_, _) => Face.Step(new FaceSenses(DateTime.Now, true, true, -1, TimeSpan.Zero, null, false));
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    /// <summary>The sky, the year and the glows in the current song's color and its neighbour.</summary>
    void Paint(Color accent)
    {
        var second = ColorUtil.ShiftHue(accent, 40, 0.05);
        Resources["Accent"] = new SolidColorBrush(accent);
        GlowA.Color = Color.FromArgb(0x8C, accent.R, accent.G, accent.B);
        GlowB.Color = Color.FromArgb(0x59, second.R, second.G, second.B);
        YearA.Color = ColorUtil.Lerp(accent, Colors.White, 0.15);
        YearB.Color = ColorUtil.Lerp(second, Colors.White, 0.35);
        SkyTop.Color = ColorUtil.Lerp(Color.FromRgb(0x1A, 0x16, 0x2C), accent, 0.12);
        KindBox.Background = new SolidColorBrush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B));
        KindBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B));
        KindBox.BorderThickness = new Thickness(1);
    }

    string N(double value) => Math.Round(value).ToString("N0", _culture);

    string Duration(double minutes) => minutes >= 60
        ? Loc.T("Rec_HoursMinutes", (int)(minutes / 60), (int)(minutes % 60))
        : Loc.T("Rec_Minutes", N(minutes));

    void Fill()
    {
        var r = _recap;
        Kicker.Text = Loc.T("Rec_Kicker");
        YearText.Text = r.Year.ToString(CultureInfo.InvariantCulture);
        Subtitle.Text = Loc.T("Rec_Subtitle");
        Note.Text = Loc.T("Rec_Note");

        if (r.Minutes < 1)
        {
            EmptyCover.Visibility = Visibility.Visible;
            EmptyText.Text = Loc.T("Rec_Empty", r.Year);
            SaveButton.IsEnabled = CopyButton.IsEnabled = false;
            return;
        }

        if (r.Minutes >= 120)
        {
            BigNumber.Text = N(r.Minutes / 60);
            BigUnit.Text = " " + Loc.T("Rec_HoursUnit");
        }
        else
        {
            BigNumber.Text = N(r.Minutes);
            BigUnit.Text = " " + Loc.T("Rec_MinutesUnit");
        }
        Counts.Text = Loc.T("Rec_Counts", N(r.Plays), N(r.Songs), N(r.Artists));

        SongsLabel.Text = Loc.T("Rec_TopSongs");
        int rank = 1;
        foreach (var song in r.TopSongs) SongList.Children.Add(SongRow(rank++, song));

        ArtistsLabel.Text = Loc.T("Rec_TopArtists");
        foreach (var artist in r.TopArtists.Take(3)) ArtistList.Children.Add(Chip(artist.Name, Duration(artist.Minutes)));
        if (r.TopArtists.Count == 0) ArtistList.Children.Add(Chip("—", ""));

        Facts.Children.Add(Fact(Loc.T("Rec_BigDay"),
            r.BiggestDay?.ToString(_s.Language == AppLanguage.Vietnamese ? "dd/MM" : "MMM d", _culture) ?? "—",
            r.BiggestDay != null ? Duration(r.BiggestDayMinutes) : ""));
        Facts.Children.Add(Fact(Loc.T("Rec_GoldenHour"), $"{r.PeakHour:00}:00", Loc.T("Rec_GoldenHourSub", (r.PeakHour + 1) % 24)));
        Facts.Children.Add(Fact(Loc.T("Rec_Streak"), Loc.T("Rec_Days", r.LongestStreak), Loc.T("Rec_DaysListened", r.DaysListened)));
        Facts.Children.Add(Fact(Loc.T("Rec_App"), r.TopApp ?? "—", r.TopApp != null ? $"{Math.Round(r.TopAppShare * 100)}%" : ""));

        KindLabel.Text = Loc.T("Rec_YouAre");
        KindName.Text = Loc.T("Rec_Kind_" + r.Kind);
        KindLine.Text = Loc.T("Rec_KindLine_" + r.Kind);
    }

    FrameworkElement SongRow(int rank, RecapSong song)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = new TextBlock
        {
            Text = rank.ToString(CultureInfo.InvariantCulture),
            FontSize = 18,
            FontWeight = FontWeights.Black,
            Foreground = (Brush)Resources["Accent"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = song.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = song.Artist, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
        var plays = new TextBlock
        {
            Text = Loc.T("Rec_Plays", N(song.Plays)),
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(plays, 2);
        grid.Children.Add(number);
        grid.Children.Add(text);
        grid.Children.Add(plays);
        return grid;
    }

    static Border Chip(string name, string detail)
    {
        var text = new TextBlock { FontSize = 12, MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.SemiBold });
        if (detail.Length > 0) text.Inlines.Add(new System.Windows.Documents.Run("  " + detail) { Foreground = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF)) });
        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 5, 12, 6),
            Margin = new Thickness(0, 0, 7, 7),
            Child = text,
        };
    }

    static Border Fact(string label, string value, string detail)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.55, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = detail, FontSize = 10.5, Opacity = 0.62, TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(10, 7, 8, 7),
            Margin = new Thickness(4, 0, 4, 0),
            Child = stack,
        };
    }

    // ------------------------------------------------------------------ the picture

    /// <summary>The card at twice its size, square-cornered for posting.</summary>
    public BitmapSource Picture()
    {
        CardClip.RadiusX = CardClip.RadiusY = 0;
        Card.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(Card.ActualWidth * 2), (int)(Card.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(Card);
        bitmap.Freeze();
        CardClip.RadiusX = CardClip.RadiusY = 24;
        return bitmap;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"MusicBar-{_recap.Year}.png",
            DefaultExt = ".png",
            Filter = "PNG|*.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Picture()));
            using var file = File.Create(dialog.FileName);
            encoder.Save(file);
            Note.Text = Loc.T("Rec_Saved");
        }
        catch (Exception ex)
        {
            Log.Error("Saving the recap picture failed", ex);
            Note.Text = Loc.T("Rec_SaveFailed");
        }
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(Picture());
            Note.Text = Loc.T("Rec_Copied");
        }
        catch (Exception ex)
        {
            Log.Error("Copying the recap picture failed", ex);
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
