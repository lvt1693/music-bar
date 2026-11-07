using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MusicBar.Services;

public sealed record SessionInfo(string AppId, string AppName, string Title, bool IsPlaying, bool IsSelected);

/// <summary>
/// Follows every app that publishes a Windows media session (Spotify, browsers,
/// Media Player, ...), picks the one to show, exposes its metadata and a
/// smoothly interpolated playback position, and forwards transport commands.
/// All public state is mutated on the UI thread.
/// </summary>
public sealed class MediaService : ObservableObject
{
    sealed class Subscription
    {
        public required GlobalSystemMediaTransportControlsSession Session;
        public required TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> Playback;
        public required TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> Media;
        public required TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> Timeline;
    }

    readonly AppSettings _settings;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<Subscription> _subs = [];
    readonly Dictionary<string, string> _sessionTitles = new();

    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    int _mediaRequest;
    int _pollTicks;
    string _trackKey = "";
    // A browser's new track not taken yet: hovering a video on YouTube plays a preview through the
    // same media session, and that is no reason to switch songs and lyrics.
    string _pendingKey = "";
    DateTime _pendingSince;
    static readonly TimeSpan BrowserHold = TimeSpan.FromSeconds(2.5);

    // Position model: position = anchor + (now - anchorTime) * rate while playing.
    TimeSpan _anchorPos;
    DateTimeOffset _anchorTime = DateTimeOffset.Now;
    double _rate = 1;
    TimeSpan _timelineStart;
    TimeSpan _lastTimelinePos = TimeSpan.MinValue;
    DateTimeOffset _lastTimelineUpdate;

    // Demo mode
    bool _demo;
    int _demoIndex;

    public MediaService(AppSettings settings)
    {
        _settings = settings;
        _settings.PropertyChanged += OnSettingsChanged;
        _poll.Tick += (_, _) => Poll();
        UpdateVlc();
    }

    public event EventHandler? TrackChanged;
    public event EventHandler? ThumbnailChanged;

    bool _hasSession;
    public bool HasSession { get => _hasSession; private set => Set(ref _hasSession, value); }

    string _appId = "";
    public string AppId { get => _appId; private set => Set(ref _appId, value); }

    string _appName = "";
    public string AppName { get => _appName; private set => Set(ref _appName, value); }

    ImageSource? _appIcon;
    public ImageSource? AppIcon { get => _appIcon; private set => Set(ref _appIcon, value); }

    Color? _appColor;
    public Color? AppColor { get => _appColor; private set => Set(ref _appColor, value); }

    string _title = "";
    public string Title { get => _title; private set => Set(ref _title, value); }

    string _artist = "";
    public string Artist { get => _artist; private set => Set(ref _artist, value); }

    string _album = "";
    public string Album { get => _album; private set => Set(ref _album, value); }

    string _albumArtist = "";
    public string AlbumArtist { get => _albumArtist; private set => Set(ref _albumArtist, value); }

    /// <summary>Genres published by the player (often empty).</summary>
    string _genres = "";
    public string Genres { get => _genres; private set => Set(ref _genres, value); }

    int _trackNumber;
    public int TrackNumber { get => _trackNumber; private set => Set(ref _trackNumber, value); }

    int _albumTrackCount;
    public int AlbumTrackCount { get => _albumTrackCount; private set => Set(ref _albumTrackCount, value); }

    BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail { get => _thumbnail; private set => Set(ref _thumbnail, value); }

    TimeSpan _duration;
    public TimeSpan Duration { get => _duration; private set => Set(ref _duration, value); }

    bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }

    bool _canPlayPause;
    public bool CanPlayPause { get => _canPlayPause; private set => Set(ref _canPlayPause, value); }

    bool _canNext;
    public bool CanNext { get => _canNext; private set => Set(ref _canNext, value); }

    bool _canPrevious;
    public bool CanPrevious { get => _canPrevious; private set => Set(ref _canPrevious, value); }

    bool _canSeek;
    public bool CanSeek { get => _canSeek; private set => Set(ref _canSeek, value); }

    public bool IsDemo => _demo;

    // ------------------------------------------------------------------ lifecycle

    public async Task InitializeAsync()
    {
        // Right after signing in, Windows' media service may not answer yet, or fail, or never
        // finish the request. Given up on, MusicBar would sit hidden for the whole run with no
        // music to show: keep asking, each try with a time limit, waiting longer between tries.
        GlobalSystemMediaTransportControlsSessionManager? manager = null;
        int tries = 0;
        // VLC is read without the media service, so the poll runs meanwhile.
        _poll.Start();
        while (manager == null)
        {
            tries++;
            try
            {
                var request = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                if (await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(10))) == request) manager = await request;
                else Log.Error($"Media session manager didn't answer within 10 s (try {tries})");
            }
            catch (Exception ex)
            {
                Log.Error($"Media session manager unavailable (try {tries})", ex);
            }
            if (manager == null) await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * tries)));
        }
        if (tries > 1) Log.Info($"Media session manager ready after {tries} tries");
        _manager = manager;

        _manager.SessionsChanged += (_, _) => _ui.BeginInvoke(RefreshSessions);
        _manager.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(PickSession);
        RefreshSessions();
    }

    void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.PinnedAppId) && !_demo) PickSession();
        else if (e.PropertyName == nameof(AppSettings.ReadVlc)) UpdateVlc();
    }

    public TimeSpan GetPosition()
    {
        if (!HasSession) return TimeSpan.Zero;
        var pos = _anchorPos;
        if (IsPlaying) pos += TimeSpan.FromTicks((long)((DateTimeOffset.Now - _anchorTime).Ticks * _rate));
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && pos > Duration)
        {
            // Well past the end and still playing: the song is on repeat, and many players (Spotify,
            // a browser looping a video) never say so, so the lyrics would sit on the last line.
            // Start over; a real timeline update or the next song's details correct it if wrong.
            if (IsPlaying && (_demo || pos > Duration + LoopGrace))
            {
                var over = TimeSpan.FromTicks((pos - Duration).Ticks % Duration.Ticks);
                _anchorPos = over;
                _anchorTime = DateTimeOffset.Now;
                _wrapped = true;
                _ui.BeginInvoke(() => Restarted?.Invoke(this, EventArgs.Empty));
                return over;
            }
            pos = Duration;
        }
        return pos;
    }

    /// <summary>How long past the end a playing song waits for the next song before it counts as repeating.</summary>
    static readonly TimeSpan LoopGrace = TimeSpan.FromSeconds(2.5);

    /// <summary>How close to the end a reported position counts as "at the end".</summary>
    static readonly TimeSpan EndSlack = TimeSpan.FromSeconds(1);

    // The song was started over here, the player having said nothing; its stale "at the end" is ignored.
    bool _wrapped;

    /// <summary>The same song began again from the top (repeat one, or a jump back from its end).</summary>
    public event EventHandler? Restarted;

    public IReadOnlyList<SessionInfo> GetSessions()
    {
        if (_demo) return [new SessionInfo(AppId, AppName, Title, IsPlaying, true)];
        var list = new List<SessionInfo>();
        // VLC, read through its web interface, is one of the players too.
        if (_vlcStatus is { Stopped: false } vlc)
            list.Add(new SessionInfo(VlcLink.AppId, AppVisualResolver.Resolve(VlcLink.AppId).Name, vlc.Title, vlc.Playing, _followingVlc));
        if (_manager == null) return list;
        try
        {
            list.AddRange(_manager.GetSessions().Select(s =>
            {
                var id = s.SourceAppUserModelId;
                _sessionTitles.TryGetValue(id, out var title);
                return new SessionInfo(id, AppVisualResolver.Resolve(id).Name, title ?? "", IsPlayingSession(s),
                    _session != null && id == _session.SourceAppUserModelId);
            }));
            return list;
        }
        catch (Exception ex)
        {
            Log.Error("Listing sessions failed", ex);
            return list;
        }
    }

    // ------------------------------------------------------------------ session tracking

    void RefreshSessions()
    {
        if (_manager == null) return;
        foreach (var sub in _subs)
        {
            try
            {
                sub.Session.PlaybackInfoChanged -= sub.Playback;
                sub.Session.MediaPropertiesChanged -= sub.Media;
                sub.Session.TimelinePropertiesChanged -= sub.Timeline;
            }
            catch (Exception)
            {
                // Session already gone.
            }
        }
        _subs.Clear();

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = _manager.GetSessions(); }
        catch (Exception ex)
        {
            Log.Error("GetSessions failed", ex);
            return;
        }

        foreach (var s in sessions)
        {
            var sub = new Subscription
            {
                Session = s,
                Playback = (sender, _) => _ui.BeginInvoke(() => OnPlaybackChanged(sender)),
                Media = (sender, _) => _ui.BeginInvoke(() => OnMediaChanged(sender)),
                Timeline = (sender, _) => _ui.BeginInvoke(() => OnTimelineChanged(sender)),
            };
            s.PlaybackInfoChanged += sub.Playback;
            s.MediaPropertiesChanged += sub.Media;
            s.TimelinePropertiesChanged += sub.Timeline;
            _subs.Add(sub);
            _ = CacheSessionTitleAsync(s);
        }
        PickSession();
    }

    void PickSession()
    {
        if (_demo || (_manager == null && _vlc == null)) return;
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> all = [];
        GlobalSystemMediaTransportControlsSession? current = null;
        if (_manager != null)
        {
            try
            {
                all = _manager.GetSessions();
                current = _manager.GetCurrentSession();
            }
            catch (Exception ex)
            {
                Log.Error("Picking session failed", ex);
                return;
            }
        }

        var pinned = _settings.PinnedAppId;
        // VLC, which has no media session: when it is the app pinned, or when it plays (or is paused
        // while on the bar) and no media session plays, unless the app pinned is there.
        bool anotherPlays = all.Any(IsPlayingSession);
        if (_vlcStatus is { Stopped: false } vlc
            && (pinned == VlcLink.AppId
                || ((vlc.Playing || _followingVlc) && !anotherPlays && !all.Any(s => s.SourceAppUserModelId == pinned))))
        {
            if (!_followingVlc) FollowVlc(vlc);
            return;
        }
        if (_followingVlc) _followingVlc = false;

        GlobalSystemMediaTransportControlsSession? target = null;
        if (pinned.Length > 0) target = all.FirstOrDefault(s => s.SourceAppUserModelId == pinned);

        if (target == null)
        {
            var mine = _session == null ? null : all.FirstOrDefault(s => Same(s, _session));
            if (current != null && IsPlayingSession(current)) target = current;
            else if (mine != null && IsPlayingSession(mine)) target = mine;
            else target = all.FirstOrDefault(IsPlayingSession) ?? mine ?? current ?? all.FirstOrDefault();
        }
        SetSession(target);
    }

    static bool Same(GlobalSystemMediaTransportControlsSession a, GlobalSystemMediaTransportControlsSession b)
        => ReferenceEquals(a, b) || a.SourceAppUserModelId == b.SourceAppUserModelId;

    static bool IsPlayingSession(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return false;
        }
    }

    bool IsSelected(GlobalSystemMediaTransportControlsSession s) => _session != null && Same(s, _session);

    void SetSession(GlobalSystemMediaTransportControlsSession? target)
    {
        if (target == null)
        {
            if (_session == null && !HasSession) return;
            _session = null;
            ClearState();
            return;
        }

        if (_session != null && Same(_session, target))
        {
            _session = target;
            return;
        }

        _session = target;
        var visual = AppVisualResolver.Resolve(target.SourceAppUserModelId);
        AppId = target.SourceAppUserModelId;
        AppName = visual.Name;
        AppIcon = visual.Icon;
        AppColor = visual.Color;
        _trackKey = "";
        _pendingKey = "";
        _lastTimelinePos = TimeSpan.MinValue;
        HasSession = true;
        UpdatePlaybackInfo();
        UpdateTimeline(force: true);
        _ = UpdateMediaAsync();
        Log.Info($"Following session {AppId}");
    }

    void ClearState()
    {
        HasSession = false;
        IsPlaying = false;
        Title = Artist = Album = AlbumArtist = Genres = AppName = AppId = "";
        TrackNumber = AlbumTrackCount = 0;
        AppIcon = null;
        AppColor = null;
        Thumbnail = null;
        Duration = TimeSpan.Zero;
        CanPlayPause = CanNext = CanPrevious = CanSeek = false;
        _trackKey = "";
        TrackChanged?.Invoke(this, EventArgs.Empty);
        ThumbnailChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s)
    {
        if (IsSelected(s)) UpdatePlaybackInfo();
        PickSession();
    }

    void OnMediaChanged(GlobalSystemMediaTransportControlsSession s)
    {
        _ = CacheSessionTitleAsync(s);
        if (IsSelected(s)) _ = UpdateMediaAsync();
    }

    void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s)
    {
        if (IsSelected(s)) UpdateTimeline(force: false);
    }

    void Poll()
    {
        // VLC every other second, every second while it is the one on the bar.
        if (_vlc != null && !_vlcBusy && (_followingVlc || _pollTicks % 2 == 0)) _ = PollVlcAsync();
        if (_session == null)
        {
            if (++_pollTicks % 3 == 0) PickSession();
            return;
        }
        UpdatePlaybackInfo();
        UpdateTimeline(force: false);
        if (++_pollTicks % 3 == 0) PickSession();
    }

    async Task CacheSessionTitleAsync(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (p != null) _sessionTitles[s.SourceAppUserModelId] = p.Title ?? "";
        }
        catch (Exception)
        {
        }
    }

    // ------------------------------------------------------------------ state readers

    void UpdatePlaybackInfo()
    {
        var s = _session;
        if (s == null) return;
        // A session going away answers null, or fails part way through being read: everything is read at once.
        bool canPlayPause, canNext, canPrevious, canSeek, playing;
        double rate;
        try
        {
            var info = s.GetPlaybackInfo();
            if (info?.Controls is not { } c) return;
            canPlayPause = c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled;
            (canNext, canPrevious, canSeek) = (c.IsNextEnabled, c.IsPreviousEnabled, c.IsPlaybackPositionEnabled);
            rate = info.PlaybackRate ?? 1.0;
            playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return;
        }

        CanPlayPause = canPlayPause;
        CanNext = canNext;
        CanPrevious = canPrevious;
        CanSeek = canSeek;
        _rate = rate > 0 ? rate : 1.0;

        if (playing != IsPlaying)
        {
            // Freeze/resume from where we visually are, so pause/play never jumps.
            _anchorPos = GetPosition();
            _anchorTime = DateTimeOffset.Now;
            IsPlaying = playing;
        }
    }

    void UpdateTimeline(bool force)
    {
        var s = _session;
        if (s == null) return;
        // A browser preview's timeline would move the song's playhead.
        if (_pendingKey.Length > 0 && !force) return;
        // Every value is read at once and guarded: when an app closes its media session (a browser tab, a
        // player quitting), Windows announces the change while the session is going, and reading its timeline
        // then threw a NullReferenceException out of the session refresh.
        TimeSpan start, end, minSeek, maxSeek, position;
        DateTimeOffset updated;
        try
        {
            var t = s.GetTimelineProperties();
            if (t == null) return;
            (start, end, minSeek, maxSeek, position, updated) = (t.StartTime, t.EndTime, t.MinSeekTime, t.MaxSeekTime, t.Position, t.LastUpdatedTime);
        }
        catch (Exception)
        {
            return;
        }

        _timelineStart = start;
        var duration = end - start;
        if (duration <= TimeSpan.Zero && maxSeek > TimeSpan.Zero) duration = maxSeek - minSeek;
        Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;

        var pos = position - start;
        if (!force && pos == _lastTimelinePos && updated == _lastTimelineUpdate) return;
        _lastTimelinePos = pos;
        _lastTimelineUpdate = updated;

        var now = DateTimeOffset.Now;
        bool sane = updated.Year > 2000 && updated <= now.AddSeconds(2) && now - updated < TimeSpan.FromHours(6);
        // From the last stretch of the song back to its first seconds: it is playing again.
        var before = force ? TimeSpan.Zero : GetPosition();
        // Some players never wind their timeline back when a song repeats: they keep saying "at the
        // end", with a fresh time on every report, and taken at their word the lyrics sit on the last
        // line for good. Playing, a report of the very end leaves the clock running on from where the
        // end was reached (GetPosition then starts the song over), and once it has, it is old news.
        bool atEnd = !force && IsPlaying && Duration > TimeSpan.FromSeconds(20) && pos >= Duration - EndSlack;
        if (atEnd && (_wrapped || before >= Duration - EndSlack)) return;
        _wrapped = false;
        _anchorPos = pos;
        _anchorTime = sane ? updated : now;
        if (!force && Duration > TimeSpan.FromSeconds(20) && before > Duration - TimeSpan.FromSeconds(8) && GetPosition() < TimeSpan.FromSeconds(6))
            Restarted?.Invoke(this, EventArgs.Empty);
    }

    async Task UpdateMediaAsync()
    {
        var s = _session;
        if (s == null) return;
        int request = ++_mediaRequest;

        GlobalSystemMediaTransportControlsSessionMediaProperties? p = null;
        try { p = await s.TryGetMediaPropertiesAsync(); }
        catch (Exception ex) { Log.Error("Reading media properties failed", ex); }
        if (p == null || request != _mediaRequest || !ReferenceEquals(s, _session)) return;

        var title = (p.Title ?? "").Trim();
        var artist = (p.Artist ?? "").Trim();
        if (artist.Length == 0) artist = (p.AlbumArtist ?? "").Trim();
        var album = (p.AlbumTitle ?? "").Trim();

        var key = $"{AppId}\n{title}\n{artist}\n{album}";
        bool trackChanged = key != _trackKey;
        if (!trackChanged)
        {
            // The browser came back to the song before the hold ran out: it was a preview.
            if (_pendingKey.Length > 0) Log.Info("Browser preview ignored");
            _pendingKey = "";
        }
        else if (_trackKey.Length > 0 && IsBrowser(AppId))
        {
            if (key != _pendingKey)
            {
                _pendingKey = key;
                _pendingSince = DateTime.Now;
                _ = LookAgainAsync(key);
                return;
            }
            if (DateTime.Now - _pendingSince < BrowserHold) return;
            _pendingKey = "";
        }
        Title = title;
        Artist = artist;
        Album = album;
        AlbumArtist = (p.AlbumArtist ?? "").Trim();
        Genres = p.Genres is { Count: > 0 } g ? string.Join(", ", g) : "";
        TrackNumber = p.TrackNumber;
        AlbumTrackCount = p.AlbumTrackCount;

        if (trackChanged)
        {
            _trackKey = key;
            if (Thumbnail != null)
            {
                Thumbnail = null;
                ThumbnailChanged?.Invoke(this, EventArgs.Empty);
            }
            UpdateTimeline(force: true);
            TrackChanged?.Invoke(this, EventArgs.Empty);
        }

        if (p.Thumbnail != null && (trackChanged || Thumbnail == null))
        {
            var bmp = await LoadThumbnailAsync(p.Thumbnail);
            if (bmp != null && key == _trackKey)
            {
                Thumbnail = bmp;
                ThumbnailChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Once the hold is over, takes the browser's new track if it is still the one playing.</summary>
    async Task LookAgainAsync(string key)
    {
        await Task.Delay(BrowserHold + TimeSpan.FromMilliseconds(100));
        if (_pendingKey == key) await UpdateMediaAsync();
    }

    /// <summary>Browsers (and their installed web apps), whose one media session follows whatever tab plays last.</summary>
    static bool IsBrowser(string appId)
    {
        var id = appId.ToLowerInvariant();
        foreach (var name in Browsers)
            if (id.Contains(name)) return true;
        return false;
    }

    // Firefox's AppUserModelId is a hash of its install folder; this is the usual one.
    static readonly string[] Browsers =
        ["chrome", "msedge", "firefox", "308046b0af4a39cb", "opera", "brave", "vivaldi", "coccoc", "thorium", "yandex", "floorp", "librewolf"];

    static async Task<BitmapSource?> LoadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            var ms = new MemoryStream();
            using (var ras = await reference.OpenReadAsync())
            using (var stream = ras.AsStreamForRead())
            {
                await stream.CopyToAsync(ms);
            }
            ms.Position = 0;
            return await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.DecodePixelWidth = 300;
                bmp.EndInit();
                bmp.Freeze();
                return (BitmapSource)bmp;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Loading thumbnail failed", ex);
            return null;
        }
    }

    // ------------------------------------------------------------------ commands

    public async Task PlayPauseAsync()
    {
        if (_demo)
        {
            _anchorPos = GetPosition();
            _anchorTime = DateTimeOffset.Now;
            IsPlaying = !IsPlaying;
            return;
        }
        if (_followingVlc)
        {
            await SendVlcAsync("pl_pause");
            return;
        }
        if (_session is { } s) await Try(() => s.TryTogglePlayPauseAsync().AsTask());
    }

    public async Task NextAsync()
    {
        if (_demo)
        {
            LoadDemoTrack(_demoIndex + 1);
            return;
        }
        if (_followingVlc)
        {
            await SendVlcAsync("pl_next");
            return;
        }
        if (_session is { } s) await Try(() => s.TrySkipNextAsync().AsTask());
    }

    public async Task PreviousAsync()
    {
        if (_demo)
        {
            if (GetPosition() > TimeSpan.FromSeconds(3)) await SeekAsync(TimeSpan.Zero);
            else LoadDemoTrack(_demoIndex - 1);
            return;
        }
        if (_followingVlc)
        {
            await SendVlcAsync("pl_previous");
            return;
        }
        if (_session is { } s) await Try(() => s.TrySkipPreviousAsync().AsTask());
    }

    public async Task SeekAsync(TimeSpan target)
    {
        if (!HasSession) return;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && target > Duration) target = Duration;

        var previousAnchor = (_anchorPos, _anchorTime);
        _anchorPos = target;
        _anchorTime = DateTimeOffset.Now;
        if (_demo) return;
        if (_followingVlc)
        {
            // VLC seeks to whole seconds.
            await SendVlcAsync("seek", ((int)target.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        var s = _session;
        if (s == null || !CanSeek) return;
        bool ok = await Try(() => s.TryChangePlaybackPositionAsync((_timelineStart + target).Ticks).AsTask());
        if (!ok && ReferenceEquals(s, _session))
        {
            (_anchorPos, _anchorTime) = previousAnchor;
            UpdateTimeline(force: true);
        }
    }

    static async Task<bool> Try(Func<Task<bool>> action)
    {
        try { return await action(); }
        catch (Exception ex)
        {
            Log.Error("Media command failed", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------ demo

    public void StartDemo()
    {
        _demo = true;
        _poll.Stop();
        LoadDemoTrack(0);
    }

    // The welcome tour plays the demo song while nothing else plays, so each choice shows on the taskbar.
    bool _preview;

    /// <summary>The demo song for the welcome tour, unless music plays already (then that shows the choices).</summary>
    public void StartPreview()
    {
        if (_demo || (HasSession && IsPlaying)) return;
        _preview = true;
        _session = null;
        _followingVlc = false;
        StartDemo();
        Log.Info("Welcome tour: the demo song plays meanwhile");
    }

    /// <summary>The tour is over: back to the players, picked afresh.</summary>
    public void StopPreview()
    {
        if (!_preview) return;
        _preview = false;
        _demo = false;
        _session = null;
        ClearState();
        _poll.Start();
        PickSession();
    }

    void LoadDemoTrack(int index)
    {
        var tracks = DemoContent.Tracks;
        _demoIndex = ((index % tracks.Count) + tracks.Count) % tracks.Count;
        var t = tracks[_demoIndex];
        AppId = DemoContent.AppId;
        AppName = "MusicBar Demo";
        AppIcon = null;
        AppColor = null;
        Title = t.Title;
        Artist = t.Artist;
        Album = t.Album;
        AlbumArtist = t.Artist;
        Genres = t.Genre;
        TrackNumber = _demoIndex + 1;
        AlbumTrackCount = tracks.Count;
        Duration = t.Duration;
        CanPlayPause = CanNext = CanPrevious = CanSeek = true;
        _anchorPos = TimeSpan.Zero;
        _anchorTime = DateTimeOffset.Now;
        _rate = 1;
        IsPlaying = true;
        HasSession = true;
        TrackChanged?.Invoke(this, EventArgs.Empty);
        Thumbnail = DemoContent.CreateArt(t);
        ThumbnailChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ VLC
    // VLC 3 publishes no media session. With Settings → General → VLC on, it is read through its
    // web interface (VlcLink) and stands in for a session: picked like one, played and sought.

    VlcLink? _vlc;
    VlcStatus? _vlcStatus;
    bool _followingVlc, _vlcBusy;
    int _vlcMisses;

    /// <summary>VLC's web interface answers (it is on, and VLC runs).</summary>
    bool _vlcAnswers;
    public bool VlcAnswers { get => _vlcAnswers; private set => Set(ref _vlcAnswers, value); }

    void UpdateVlc()
    {
        if (_settings.ReadVlc && _vlc == null)
        {
            // Turned on by the user: VLC's web interface switched on in its settings, again if a reset
            // of VLC's preferences took it away.
            if (!VlcLink.Enabled) VlcLink.Enable();
            _vlc = new VlcLink();
        }
        else if (!_settings.ReadVlc && _vlc != null)
        {
            _vlc.Dispose();
            _vlc = null;
            _vlcStatus = null;
            VlcAnswers = false;
            if (_followingVlc) PickSession();
        }
    }

    async Task PollVlcAsync()
    {
        var vlc = _vlc;
        if (vlc == null) return;
        _vlcBusy = true;
        try
        {
            var status = await vlc.ReadAsync();
            if (!ReferenceEquals(vlc, _vlc)) return;
            // An answer missed now and then (VLC busy opening a file) is no reason to let it go.
            if (status == null && _vlcStatus != null && ++_vlcMisses < 3) return;
            _vlcMisses = 0;
            bool appeared = _vlcStatus == null && status != null;
            _vlcStatus = status;
            VlcAnswers = status != null;
            if (appeared) Log.Info("VLC answers through its web interface");
            if (_followingVlc)
            {
                if (status is { Stopped: false }) ApplyVlc(status);
                else PickSession();
            }
            else if (status is { Playing: true })
            {
                PickSession();
            }
        }
        finally
        {
            _vlcBusy = false;
        }
    }

    void FollowVlc(VlcStatus status)
    {
        _followingVlc = true;
        _session = null;
        _pendingKey = "";
        _trackKey = "";
        var visual = AppVisualResolver.Resolve(VlcLink.AppId);
        AppId = VlcLink.AppId;
        AppName = visual.Name;
        AppIcon = visual.Icon;
        AppColor = visual.Color;
        CanPlayPause = CanNext = CanPrevious = CanSeek = true;
        HasSession = true;
        Log.Info("Following VLC");
        ApplyVlc(status);
    }

    void ApplyVlc(VlcStatus status)
    {
        string key = $"{VlcLink.AppId}\n{status.ItemId}\n{status.Title}\n{status.Artist}";
        bool changed = key != _trackKey;
        if (status.Playing != IsPlaying)
        {
            _anchorPos = GetPosition();
            _anchorTime = DateTimeOffset.Now;
            IsPlaying = status.Playing;
        }
        Duration = status.Length;
        _rate = status.Rate;
        // VLC says where it is once a second; in between the position runs on by itself, and is set
        // again only when it has drifted, or jumped (a seek in VLC), so the lyrics don't stutter.
        if (changed || !status.Playing || (GetPosition() - status.Position).Duration() > TimeSpan.FromSeconds(0.75))
        {
            _anchorPos = status.Position;
            _anchorTime = DateTimeOffset.Now;
        }
        if (!changed) return;

        _trackKey = key;
        Title = status.Title;
        Artist = status.Artist;
        Album = status.Album;
        AlbumArtist = Genres = "";
        TrackNumber = AlbumTrackCount = 0;
        if (Thumbnail != null)
        {
            Thumbnail = null;
            ThumbnailChanged?.Invoke(this, EventArgs.Empty);
        }
        TrackChanged?.Invoke(this, EventArgs.Empty);
        if (status.ArtUrl.Length > 0 && _vlc is { } vlc) _ = LoadVlcArtAsync(vlc, status.ArtUrl, key);
    }

    async Task LoadVlcArtAsync(VlcLink vlc, string url, string key)
    {
        var art = await vlc.ArtAsync(url);
        if (art == null || key != _trackKey) return;
        Thumbnail = art;
        ThumbnailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A command to VLC, then a fresh look at it so the bar follows at once.</summary>
    async Task<bool> SendVlcAsync(string command, string? value = null)
    {
        if (_vlc is not { } vlc) return false;
        bool ok = await vlc.SendAsync(command, value);
        if (ok && !_vlcBusy) await PollVlcAsync();
        return ok;
    }
}
