using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace MusicBar.Setup;

/// <summary>Why the window is open.</summary>
public enum Flow
{
    /// <summary>MusicBar isn't on this machine yet.</summary>
    Install,
    /// <summary>It is (any version): update, reinstall or go back to this one.</summary>
    Update,
    /// <summary>/SILENT, as MusicBar starts its own updates: no questions, the progress only, then it closes.</summary>
    QuietUpdate,
    /// <summary>From Windows' Installed apps, through MusicBar-Uninstall.exe.</summary>
    Uninstall,
}

enum Screen { Welcome, Options, Progress, Done, Update, Uninstall, Error }

public partial class MainWindow : Window
{
    const string Repo = "https://github.com/lvt1693/music-bar";
    const string Site = "https://musicbar.truclv.com";

    readonly Flow _flow;
    readonly Request _request;
    Engine.Installed? _installed;
    readonly string? _appDir;
    string _lang;
    Screen _screen;
    IntPtr _hwnd;

    string _dir = Engine.DefaultDir;
    bool _busy;
    bool _succeeded;
    double _progress;
    int _errorCode;
    bool _reopened;
    readonly DispatcherTimer _drift = new() { Interval = TimeSpan.FromMilliseconds(60) };

    public MainWindow(Flow flow, Request request, string lang, string? appDir)
    {
        InitializeComponent();
        _flow = flow;
        _request = request;
        _lang = lang;
        _appDir = appDir;
        _installed = flow == Flow.Uninstall ? null : Engine.FindInstalled();
        if (_installed != null) _dir = _installed.Dir;
        if (request.TestPos is Point at)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = at.X;
            Top = at.Y;
            ShowActivated = false;
        }

        BuildSpectrum();
        Inside.SizeChanged += (_, e) => Inside.Clip = new RectangleGeometry(new Rect(e.NewSize), 18, 18);
        Loaded += (_, _) => StartGlows();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_busy) Close();
        };
        Closing += (_, e) =>
        {
            if (_busy) e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            if (_flow == Flow.Uninstall) Engine.RemoveSelfLater();
        };
        // While uninstalling (no progress to report), the seekbar's head wanders on its own.
        _drift.Tick += (_, _) =>
        {
            _progress = _progress + (0.92 - _progress) * 0.02;
            SetSeek(_progress);
        };

        FolderBox.Text = _dir;
        _screen = flow switch
        {
            Flow.Update => Screen.Update,
            Flow.QuietUpdate => Screen.Progress,
            Flow.Uninstall => appDir == null ? Screen.Error : Screen.Uninstall,
            _ => Screen.Welcome,
        };
        if (flow == Flow.Uninstall && appDir == null) _errorCode = -2;
        ApplyText();
        Go(_screen);
    }

    string L(string vi, string en) => _lang == "vi" ? vi : en;

    /// <summary>Tests (Snapshots): a screen with made-up state; nothing runs.</summary>
    internal void Preview(Screen screen, double progress = 0, int error = 0, bool succeeded = false, bool reopened = false,
        Engine.Installed? installed = null, bool busy = false)
    {
        _installed = installed;
        if (installed != null) _dir = installed.Dir;
        _progress = progress;
        _errorCode = error;
        _succeeded = succeeded;
        _reopened = reopened;
        _busy = busy;
        Go(screen);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
        if (_flow == Flow.QuietUpdate) Dispatcher.BeginInvoke(new Action(Install), DispatcherPriority.Background);
    }

    /// <summary>The installer underneath posts its progress here, in thousandths.</summary>
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Engine.ProgressMessage)
        {
            double p = Math.Max(0, Math.Min(1, wParam.ToInt64() / 1000.0));
            if (p > _progress)
            {
                _progress = p;
                ShowProgress();
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------ screens

    void Go(Screen screen)
    {
        _screen = screen;
        WelcomePanel.Visibility = Vis(screen == Screen.Welcome);
        OptionsPanel.Visibility = Vis(screen == Screen.Options);
        ProgressPanel.Visibility = Vis(screen == Screen.Progress);
        DonePanel.Visibility = Vis(screen == Screen.Done);
        UpdatePanel.Visibility = Vis(screen == Screen.Update);
        UninstallPanel.Visibility = Vis(screen == Screen.Uninstall);
        ErrorPanel.Visibility = Vis(screen == Screen.Error);
        CloseButton.IsEnabled = !_busy;

        bool removing = _flow == Flow.Uninstall;
        switch (screen)
        {
            case Screen.Progress:
                ProgressFace.Mood = removing ? Mood.Sad : Mood.Sing;
                BarFace.Mood = removing ? Mood.Sad : Mood.Sing;
                Playing(!removing);
                SetSeek(_progress);
                break;
            case Screen.Done:
                DoneFace.Mood = removing ? Mood.Normal : Mood.Happy;
                BarFace.Mood = removing ? Mood.Normal : Mood.Happy;
                Playing(!removing);
                SetSeek(removing ? 0 : 1);
                break;
            case Screen.Error:
                BarFace.Mood = Mood.Normal;
                Playing(false);
                SetSeek(_progress);
                break;
            case Screen.Uninstall:
                BarFace.Mood = Mood.Sad;
                Playing(false);
                SetSeek(0);
                break;
            default:
                BarFace.Mood = Mood.Normal;
                Playing(false);
                SetSeek(0);
                break;
        }
        ApplyText();
        if (screen == Screen.Options) ShowSpace();
    }

    static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Every word on the window, in the language picked.</summary>
    void ApplyText()
    {
        bool removing = _flow == Flow.Uninstall;
        Title = removing ? L("Gỡ MusicBar", "Uninstall MusicBar") : L("Cài MusicBar", "MusicBar Setup");
        VersionText.Text = Engine.Version;
        ViButton.Tag = _lang == "vi" ? "on" : null;
        EnButton.Tag = _lang == "vi" ? null : "on";
        CloseButton.ToolTip = L("Đóng", "Close");
        AutomationSetName(CloseButton, L("Đóng", "Close"));

        // Welcome
        WelcomeTitle.Text = L("Cho taskbar biết hát", "Let your taskbar sing");
        WelcomeText.Text = L("Bài đang phát, lời karaoke và hơn trăm skin của cộng đồng, chạy ngay trên thanh taskbar của bạn.",
            "What's playing, karaoke lyrics and over a hundred community skins, right on your taskbar.");
        InstallButton.Content = L("Cài đặt", "Install");
        OptionsButton.Content = L("Tùy chỉnh thư mục, lối tắt", "Choose folder and shortcuts");
        WelcomeFine.Inlines.Clear();
        WelcomeFine.Inlines.Add(new Run(L($"Chỉ cài cho tài khoản này, không cần quyền admin · khoảng {Engine.SizeMb} MB · ",
            $"For this account only, no admin needed · about {Engine.SizeMb} MB · ")));
        WelcomeFine.Inlines.Add(Link(L("Giấy phép MIT", "MIT License"), Repo + "/blob/main/LICENSE"));

        // Options
        OptionsTitle.Text = L("Tùy chỉnh cài đặt", "Install options");
        FolderLabel.Text = L("Cài vào thư mục", "Install to");
        ChangeButton.Content = L("Đổi…", "Change…");
        StartMenuBox.Content = L("Thêm vào menu Start", "Add to the Start menu");
        DesktopBox.Content = L("Tạo lối tắt trên màn hình", "Create a desktop shortcut");
        BackButton.Content = L("Quay lại", "Back");
        OptionsInstallButton.Content = L("Cài đặt", "Install");
        ShowSpace();

        // Progress
        ShowProgress();

        // Done
        bool updated = _installed != null || _flow == Flow.QuietUpdate;
        GalleryText.Inlines.Clear();
        if (removing)
        {
            DoneTitle.Text = L("Gỡ xong rồi", "All gone");
            DoneText.Text = L("Taskbar đã về như cũ. Hẹn gặp lại nhé!", "Your taskbar is back to how it was. See you around!");
            DoneCloseButton.Visibility = Visibility.Collapsed;
            OpenButton.Content = L("Đóng", "Close");
            GalleryText.Visibility = Visibility.Collapsed;
        }
        else
        {
            DoneTitle.Text = updated ? L($"Đã lên {Engine.Version}!", $"Now on {Engine.Version}!") : L("Xong rồi!", "All set!");
            DoneText.Text = updated
                ? _reopened || _flow == Flow.QuietUpdate
                    ? L("MusicBar đã mở lại, cài đặt và skin của bạn vẫn y nguyên.", "MusicBar is back, with your settings and skins as they were.")
                    : L("Cài đặt và skin của bạn vẫn y nguyên.", "Your settings and skins are just as they were.")
                : L("Mở Spotify, YouTube hay trình phát nào cũng được, MusicBar sẽ hiện ngay dưới taskbar. Lần đầu mở, em dẫn bạn đi một vòng.",
                    "Play something in Spotify, YouTube or any player and MusicBar shows up on your taskbar. The first time, I'll show you around.");
            bool openAlready = _reopened || _flow == Flow.QuietUpdate;
            DoneCloseButton.Visibility = Vis(!openAlready);
            DoneCloseButton.Content = L("Đóng", "Close");
            OpenButton.Content = openAlready ? L("Đóng", "Close") : L("Mở MusicBar", "Open MusicBar");
            GalleryText.Visibility = Visibility.Visible;
            GalleryText.Inlines.Add(new Run(L("Thêm skin cho taskbar: ", "More looks for your taskbar: ")));
            GalleryText.Inlines.Add(Link(L("kho skin của cộng đồng", "the community gallery"), Site + "/gallery"));
        }

        // Update
        string old = _installed?.Version ?? "";
        int order = Engine.Compare(old, Engine.Version);
        UpdateTitle.Text = order < 0 ? L("Lên đời MusicBar", "Update MusicBar")
            : order == 0 ? L("Cài lại MusicBar", "Reinstall MusicBar")
            : L("Máy đang có bản mới hơn", "A newer version is installed");
        UpdateButton.Content = order < 0 ? L("Cập nhật", "Update") : order == 0 ? L("Cài lại", "Reinstall") : L("Vẫn cài bản này", "Install this one anyway");
        LaterButton.Content = L("Để sau", "Later");
        OldVersion.Text = old.Length > 0 ? old : "?";
        NewVersion.Text = Engine.Version;
        // The same version again: one chip, no arrow.
        OldChip.Visibility = Vis(order != 0);
        VersionArrow.Visibility = Vis(order != 0);
        // What's new: the CHANGELOG's headings (Vietnamese only), else a link to the release's notes.
        bool news = order < 0 && _lang == "vi" && Engine.News.Count > 0;
        NewsList.Visibility = Vis(news);
        NewsList.ItemsSource = Engine.News.Take(3).ToList();
        NewsLabel.Visibility = Vis(order < 0);
        NewsLabel.Inlines.Clear();
        if (news) NewsLabel.Inlines.Add(new Run(L("Có gì mới", "What's new")));
        else NewsLabel.Inlines.Add(Link(L($"Xem bản {Engine.Version} có gì mới", $"See what's new in {Engine.Version}"),
            $"{Repo}/releases/tag/v{Engine.Version}"));
        UpdateNote.Text = Engine.AppRunning()
            ? L("MusicBar đang chạy sẽ tắt một chút rồi tự mở lại. Cài đặt và skin của bạn vẫn giữ nguyên.",
                "MusicBar will close for a moment and open again. Your settings and skins stay as they are.")
            : L("Cài đặt và skin của bạn vẫn giữ nguyên.", "Your settings and skins stay as they are.");

        // Uninstall
        UninstallTitle.Text = L("Gỡ MusicBar thật hả?", "Uninstall MusicBar?");
        UninstallText.Text = L("Taskbar sẽ về lại đúng như trước khi cài: kiểu ẩn hiện, kích thước, căn giữa hay căn trái.",
            "Your taskbar goes back to how it was before: auto-hide, size and alignment.");
        PurgeBox.Content = L("Xóa luôn cài đặt và skin đã tải", "Also delete settings and downloaded skins");
        StayButton.Content = L("Thôi, ở lại", "Never mind");
        RemoveButton.Content = L("Gỡ cài đặt", "Uninstall");

        // Error
        ErrorTitle.Text = L("Ơ, trục trặc rồi", "Oops, something went wrong");
        ErrorText.Text = _errorCode switch
        {
            -2 => L("Không thấy MusicBar trên máy này, có thể nó đã được gỡ rồi.", "MusicBar isn't on this machine; it may be uninstalled already."),
            _ when removing => L("Chưa gỡ được MusicBar. Đóng MusicBar rồi bấm Thử lại nhé.", "MusicBar couldn't be removed. Close it and press Try again."),
            -1 => L("Không mở được bộ cài bên trong. Tải lại file Setup thử xem.", "The installer inside couldn't start. Try downloading Setup again."),
            _ => L("Chưa chép được MusicBar vào máy. Thường là do ổ đĩa đầy, hoặc phần mềm diệt virus đang giữ file.",
                "MusicBar couldn't be copied onto this PC. Usually the drive is full, or antivirus is holding a file."),
        };
        ErrorDetail.Text = removing
            ? L($"Mã lỗi {_errorCode}", $"Error code {_errorCode}")
            : L($"Mã lỗi {_errorCode} · Nhật ký: {Engine.LogPath}", $"Error code {_errorCode} · Log: {Engine.LogPath}");
        ReportButton.Content = L("Báo lỗi trên GitHub", "Report it on GitHub");
        PortableButton.Content = L("Tải bản portable", "Get the portable version");
        PortableButton.Visibility = Vis(!removing);
        RetryButton.Content = L("Thử lại", "Try again");
        RetryButton.Visibility = Vis(_errorCode != -2);

        // The taskbar in miniature
        switch (_screen)
        {
            case Screen.Progress:
                BarTitle.Text = removing ? L("Đang gỡ…", "Uninstalling…") : updated ? L("Đang cập nhật…", "Updating…") : L("Đang cài…", "Installing…");
                BarSub.Text = removing ? "" : $"{Math.Round(_progress * 100)}%";
                break;
            case Screen.Done:
                BarTitle.Text = removing ? L("Tạm biệt nhé", "Goodbye for now") : L("Sẵn sàng", "Ready");
                BarSub.Text = removing ? L("nhớ quay lại nghe tiếp", "come back for more songs") : L("Bật một bài thử xem", "Play something");
                break;
            case Screen.Update:
                BarTitle.Text = order < 0 ? L("Có bản mới", "Update ready") : order == 0 ? L("Đã cài bản này", "Already installed")
                    : L("Bản cũ hơn", "An older version");
                BarSub.Text = order == 0 ? "MusicBar " + Engine.Version : $"{OldVersion.Text} → {Engine.Version}";
                break;
            case Screen.Uninstall:
                BarTitle.Text = L("Tạm biệt nhé", "Goodbye for now");
                BarSub.Text = L("nhớ quay lại nghe tiếp", "come back for more songs");
                break;
            case Screen.Error:
                BarTitle.Text = removing ? L("Chưa gỡ xong", "Not removed yet") : L("Chưa cài xong", "Not installed yet");
                BarSub.Text = _errorCode == -2 ? "" : L("bấm Thử lại nhé", "press Try again");
                break;
            default:
                BarTitle.Text = L("Cho taskbar hát đi", "Let your taskbar sing");
                BarSub.Text = "MusicBar " + Engine.Version;
                break;
        }
    }

    void ShowProgress()
    {
        bool removing = _flow == Flow.Uninstall;
        if (removing)
        {
            PercentText.FontSize = 28;
            PercentText.Text = L("Đang gỡ…", "Uninstalling…");
            ProgressLine.Text = L("Đang trả taskbar về như cũ…", "Putting your taskbar back…");
            ProgressFine.Text = L("Chỉ một lát thôi.", "Just a moment.");
            return;
        }
        string[] lines = _lang == "vi"
            ? new[] { "Đang mở hộp đàn…", "Đang lên dây cho taskbar…", "Đang xếp skin lên kệ…", "Đang chỉnh âm cho vừa tai…", "Chờ nốt nhạc cuối…" }
            : new[] { "Opening the instrument case…", "Tuning the taskbar…", "Shelving the skins…", "Setting the volume just right…", "Waiting for the last note…" };
        int percent = (int)Math.Round(_progress * 100);
        PercentText.FontSize = 40;
        PercentText.Text = percent + "%";
        ProgressLine.Text = lines[Math.Min(4, percent / 20)];
        ProgressFine.Text = L("Chỉ một lát thôi, cứ để đó em lo.", "Just a moment, leave it to me.");
        if (_screen == Screen.Progress)
        {
            SetSeek(_progress);
            BarSub.Text = percent + "%";
        }
    }

    void ShowSpace()
    {
        long need = Engine.SizeMb * 1024L * 1024L;
        long free = Engine.FreeBytes(_dir);
        string text = L($"Cần khoảng {Engine.SizeMb} MB trống.", $"Needs about {Engine.SizeMb} MB of free space.");
        if (free >= 0 && free < need)
            text += " " + L($"Ổ này chỉ còn {free / (1024 * 1024)} MB.", $"This drive has only {free / (1024 * 1024)} MB left.");
        SpaceText.Text = text;
        SpaceText.Foreground = free >= 0 && free < need ? new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x96)) : (Brush)FindResource("Faint");
    }

    Hyperlink Link(string text, string url)
    {
        var link = new Hyperlink(new Run(text)) { Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), Cursor = Cursors.Hand };
        link.Click += (_, _) => Engine.Open(url);
        return link;
    }

    static void AutomationSetName(DependencyObject element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);

    // ------------------------------------------------------------------ installing

    async void Install()
    {
        if (_busy) return;
        _busy = true;
        _progress = 0;
        _errorCode = 0;
        bool wasRunning = Engine.AppRunning();
        bool fresh = _installed == null && _flow == Flow.Install;
        Go(Screen.Progress);

        string? payload = null;
        int code;
        try
        {
            payload = await Task.Run(Engine.ExtractPayload);
            string args = _flow == Flow.QuietUpdate
                ? _request.QuietTail() + " /HOSTWND=" + _hwnd.ToInt64()
                  + (_request.Tail.IndexOf("/LOG", StringComparison.OrdinalIgnoreCase) >= 0 ? "" : " /LOG=" + Engine.Quote(Engine.LogPath))
                : Engine.InnoArgs(_hwnd, _lang, fresh ? _dir : null, fresh ? StartMenuBox.IsChecked == true : null,
                    fresh ? DesktopBox.IsChecked == true : null, launch: false);
            code = await Engine.RunAsync(payload, args);
        }
        catch
        {
            code = -1;
        }
        finally
        {
            string? done = payload;
            _ = Task.Run(() => Engine.Discard(done));
        }

        _busy = false;
        if (code != 0)
        {
            _errorCode = code;
            Go(Screen.Error);
            return;
        }
        _succeeded = true;
        _progress = 1;
        var now = Engine.FindInstalled();
        if (now != null) _dir = now.Dir;
        // An update reopens MusicBar if it was running (a quiet update's installer opens it itself).
        if (_flow == Flow.Update && wasRunning)
        {
            Engine.Launch(_dir);
            _reopened = true;
        }
        Go(Screen.Done);
        if (_flow == Flow.QuietUpdate)
        {
            await Task.Delay(1800);
            Close();
        }
    }

    async void Remove()
    {
        if (_busy || _appDir == null) return;
        _busy = true;
        _progress = 0.05;
        _errorCode = 0;
        bool purge = PurgeBox.IsChecked == true;
        Go(Screen.Progress);
        _drift.Start();
        int code;
        try
        {
            code = await Engine.UninstallAsync(_appDir);
            if (code == 0 && purge) await Task.Run(Engine.Purge);
        }
        catch
        {
            code = -1;
        }
        _drift.Stop();
        _busy = false;
        if (code != 0)
        {
            _errorCode = code;
            Go(Screen.Error);
            return;
        }
        _succeeded = true;
        _progress = 1;
        Go(Screen.Done);
    }

    // ------------------------------------------------------------------ the taskbar in miniature, the glows

    void SetSeek(double p)
    {
        double w = Seek.ActualWidth;
        if (w <= 0) return;
        SeekFill.Width = w * p;
        SeekHead.Margin = new Thickness(w * p - 5.5, 0, 0, 0);
        SeekHead.Opacity = _screen is Screen.Welcome or Screen.Options or Screen.Update or Screen.Uninstall ? 0.9 : 1;
    }

    void Seek_SizeChanged(object sender, SizeChangedEventArgs e) => SetSeek(_screen switch
    {
        Screen.Done => _flow == Flow.Uninstall ? 0 : 1,
        Screen.Progress or Screen.Error => _progress,
        _ => 0,
    });

    readonly Rectangle[] _bars = new Rectangle[5];

    void BuildSpectrum()
    {
        var fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xC3, 0x71), Color.FromRgb(0xFF, 0x5F, 0x6D), 90);
        fill.Freeze();
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle
            {
                Width = 3, Height = 3.5, RadiusX = 1.5, RadiusY = 1.5, Fill = fill, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, i < _bars.Length - 1 ? 3 : 0, 0),
            };
            Spectrum.Children.Add(_bars[i]);
        }
    }

    void Playing(bool on)
    {
        double[] beat = { 0.82, 0.95, 0.7, 1.08, 0.9 };
        for (int i = 0; i < _bars.Length; i++)
        {
            if (on)
            {
                _bars[i].Opacity = 1;
                _bars[i].BeginAnimation(HeightProperty, new DoubleAnimation(4, 18, TimeSpan.FromSeconds(beat[i] / 2))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }
            else
            {
                _bars[i].BeginAnimation(HeightProperty, null);
                _bars[i].Height = 3.5;
                _bars[i].Opacity = 0.45;
            }
        }
    }

    void StartGlows()
    {
        Drift((TranslateTransform)GlowA.RenderTransform, 14);
        Drift((TranslateTransform)GlowB.RenderTransform, 17, reverse: true);
    }

    static void Drift(TranslateTransform t, double seconds, bool reverse = false)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(reverse ? 46 : 0, reverse ? 0 : 46, TimeSpan.FromSeconds(seconds))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(reverse ? 26 : 0, reverse ? 0 : 26, TimeSpan.FromSeconds(seconds))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
    }

    // ------------------------------------------------------------------ clicks

    void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    void Vi_Click(object sender, RoutedEventArgs e)
    {
        _lang = "vi";
        ApplyText();
    }

    void En_Click(object sender, RoutedEventArgs e)
    {
        _lang = "en";
        ApplyText();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Install_Click(object sender, RoutedEventArgs e)
    {
        _dir = FolderBox.Text;
        Install();
    }

    void Update_Click(object sender, RoutedEventArgs e) => Install();

    void Options_Click(object sender, RoutedEventArgs e) => Go(Screen.Options);

    void Back_Click(object sender, RoutedEventArgs e) => Go(Screen.Welcome);

    void Change_Click(object sender, RoutedEventArgs e)
    {
        string? parent = IOPath.GetDirectoryName(_dir);
        string? start = Directory.Exists(_dir) ? _dir : parent != null && Directory.Exists(parent) ? parent : null;
        string? picked = FolderPicker.Pick(_hwnd, L("Chọn thư mục cài MusicBar", "Choose where to install MusicBar"), start);
        if (picked == null) return;
        // As Inno does: a folder that isn't MusicBar's own gets a MusicBar folder inside it.
        if (!string.Equals(IOPath.GetFileName(picked.TrimEnd('\\')), "MusicBar", StringComparison.OrdinalIgnoreCase))
            picked = IOPath.Combine(picked, "MusicBar");
        _dir = picked;
        FolderBox.Text = picked;
        ShowSpace();
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_succeeded && _flow != Flow.Uninstall && !_reopened && _flow != Flow.QuietUpdate) Engine.Launch(_dir);
        Close();
    }

    void Remove_Click(object sender, RoutedEventArgs e) => Remove();

    void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_flow == Flow.Uninstall) Remove();
        else Install();
    }

    void Report_Click(object sender, RoutedEventArgs e) => Engine.Open(Repo + "/issues");

    void Portable_Click(object sender, RoutedEventArgs e) => Engine.Open(Site + "/download");
}
