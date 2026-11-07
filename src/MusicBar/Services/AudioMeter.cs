using System;
using System.Runtime.InteropServices;
using System.Threading;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>
/// Listens to what the speakers are playing (WASAPI loopback on the default
/// output) and turns it into a loudness level and a log-spaced spectrum for
/// the seekbar styles that dance to the music. Audio never leaves the process.
/// Runs on its own thread only while some style needs it.
/// </summary>
public sealed class AudioMeter : IDisposable
{
    public const int BandCount = 48;

    /// <summary>Points of the waveform the visualizers draw (the latest ~20 ms, -1 to 1, gain-levelled).</summary>
    public const int WaveCount = 256;

    readonly object _gate = new();
    readonly float[] _bands = new float[BandCount];
    readonly float[] _published = new float[BandCount];
    readonly float[] _wave = new float[WaveCount];
    readonly float[] _publishedWave = new float[WaveCount];
    float _level;
    float _publishedLevel;
    float _beat, _publishedBeat;
    Thread? _thread;
    volatile bool _run;
    // Bumped on every start: a capture thread from an earlier start ends even if capture was turned straight back on.
    volatile int _generation;
    bool _loggedFailure;

    public bool IsCapturing { get; private set; }

    public float Level
    {
        get
        {
            lock (_gate) return _publishedLevel;
        }
    }

    public void CopyBands(float[] destination)
    {
        lock (_gate) Array.Copy(_published, destination, Math.Min(destination.Length, BandCount));
    }

    public void CopyWave(float[] destination)
    {
        lock (_gate) Array.Copy(_publishedWave, destination, Math.Min(destination.Length, WaveCount));
    }

    /// <summary>A kick or a strong onset just now: 1 on the hit, falling away over a quarter second.</summary>
    public float Beat
    {
        get
        {
            lock (_gate) return _publishedBeat;
        }
    }

    public void SetActive(bool active)
    {
        if (active == _run) return;
        _run = active;
        if (active)
        {
            int generation = ++_generation;
            // Light work every few milliseconds; below normal, a busy machine kept it waiting and the bars lagged the beat.
            _thread = new Thread(() => Loop(generation)) { IsBackground = true, Name = "MusicBar.AudioMeter", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
        else
        {
            IsCapturing = false;
            lock (_gate)
            {
                Array.Clear(_published);
                Array.Clear(_publishedWave);
                _publishedLevel = 0;
                _publishedBeat = 0;
            }
        }
    }

    // One meter serves the bar on every taskbar: it listens while any of them needs it.
    readonly System.Collections.Generic.HashSet<object> _wanting = [];

    public void Want(object owner, bool wants)
    {
        if (wants) _wanting.Add(owner);
        else _wanting.Remove(owner);
        SetActive(_wanting.Count > 0);
    }

    /// <summary>A new playback device: stop listening to the old one and pick up the default again.</summary>
    public void Restart()
    {
        if (!_run) return;
        SetActive(false);
        SetActive(true);
    }

    public void Dispose() => SetActive(false);

    void Loop(int generation)
    {
        while (_run && generation == _generation)
        {
            try
            {
                Capture(generation);
            }
            catch (Exception ex)
            {
                IsCapturing = false;
                if (!_loggedFailure)
                {
                    Log.Error("Audio capture failed; seekbar falls back to synthetic motion", ex);
                    _loggedFailure = true;
                }
                for (int i = 0; i < 30 && _run && generation == _generation; i++) Thread.Sleep(100);
            }
        }
    }

    // ------------------------------------------------------------------ WASAPI

    void Capture(int generation)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr format = IntPtr.Zero;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device));
            Check(device.GetId(out var deviceId));
            var clientId = typeof(IAudioClient).GUID;
            Check(device.Activate(ref clientId, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var clientObj));
            client = (IAudioClient)clientObj;
            Check(client.GetMixFormat(out format));

            var wf = Marshal.PtrToStructure<WaveFormatEx>(format);
            bool isFloat = wf.FormatTag == 3
                           || (wf.FormatTag == 0xFFFE && Marshal.PtrToStructure<Guid>(format + 24) == FloatSubtype);
            int channels = Math.Max(1, (int)wf.Channels);
            int bits = wf.BitsPerSample;
            int sampleRate = (int)wf.SamplesPerSec;

            Check(client.Initialize(0 /* shared */, 0x00020000 /* loopback */, 1_000_000 /* 100 ms */, 0, format, IntPtr.Zero));
            var captureId = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref captureId, out var captureObj));
            capture = (IAudioCaptureClient)captureObj;
            Check(client.Start());
            IsCapturing = true;
            _loggedFailure = false;
            Log.Info($"Audio capture started: {sampleRate} Hz, {channels} ch, {(isFloat ? "float" : bits + "-bit")}");

            var analyzer = new Analyzer(sampleRate);
            var ring = new float[analyzer.Size];
            int hop = Math.Max(256, sampleRate / 90);
            int ringPos = 0;
            int sinceAnalysis = 0;
            int quietTicks = 0;
            long checkDeviceAt = Environment.TickCount64 + 1000;

            while (_run && generation == _generation)
            {
                Thread.Sleep(5);
                // Output switched in Windows (sound settings, the volume flyout, a headset plugged in): the old
                // device keeps answering with silence, so the bar went still until MusicBar restarted (#18).
                if (Environment.TickCount64 >= checkDeviceAt)
                {
                    checkDeviceAt = Environment.TickCount64 + 1000;
                    if (DefaultChanged(enumerator, deviceId))
                    {
                        Log.Info("The default playback device changed: listening to the new one");
                        break;
                    }
                }
                bool gotData = false;
                while (true)
                {
                    Check(capture.GetNextPacketSize(out uint packet));
                    if (packet == 0) break;
                    Check(capture.GetBuffer(out var data, out uint frames, out uint flags, out _, out _));
                    bool silent = (flags & 0x2) != 0;
                    for (int f = 0; f < frames; f++)
                    {
                        float sample = 0;
                        if (!silent)
                        {
                            for (int c = 0; c < channels; c++)
                            {
                                int index = f * channels + c;
                                sample += isFloat ? ReadFloat(data, index)
                                    : bits == 16 ? Marshal.ReadInt16(data, index * 2) / 32768f
                                    : bits == 32 ? Marshal.ReadInt32(data, index * 4) / 2147483648f
                                    : 0;
                            }
                            sample /= channels;
                        }
                        ring[ringPos] = sample;
                        ringPos = (ringPos + 1) % ring.Length;
                    }
                    sinceAnalysis += (int)frames;
                    capture.ReleaseBuffer(frames);
                    gotData = true;
                }

                if (sinceAnalysis >= hop)
                {
                    sinceAnalysis = 0;
                    quietTicks = 0;
                    analyzer.Run(ring, ringPos, _bands, ref _level, ref _beat);
                    analyzer.Wave(ring, ringPos, sampleRate, _wave);
                    Publish();
                }
                else if (!gotData && ++quietTicks > 5)
                {
                    // Nothing is rendering: let the visuals settle down.
                    for (int b = 0; b < BandCount; b++) _bands[b] *= 0.85f;
                    for (int w = 0; w < WaveCount; w++) _wave[w] *= 0.8f;
                    _level *= 0.85f;
                    _beat *= 0.8f;
                    Publish();
                }
            }
            client.Stop();
        }
        finally
        {
            IsCapturing = false;
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
            if (device != null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>Windows' default playback device is no longer the one being listened to.</summary>
    static bool DefaultChanged(IMMDeviceEnumerator enumerator, string listening)
    {
        // No default device at the moment: keep the old one; if it went away, the capture fails and starts over.
        if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var current) != 0 || current == null)
            return false;
        try
        {
            return current.GetId(out var id) == 0 && id != listening;
        }
        finally
        {
            Marshal.ReleaseComObject(current);
        }
    }

    static unsafe float ReadFloat(IntPtr data, int index) => ((float*)data)[index];

    void Publish()
    {
        lock (_gate)
        {
            Array.Copy(_bands, _published, BandCount);
            Array.Copy(_wave, _publishedWave, WaveCount);
            _publishedLevel = _level;
            _publishedBeat = _beat;
        }
    }

    static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    // ------------------------------------------------------------------ analysis

    /// <summary>
    /// Hann-windowed FFT folded into log-spaced bands with fast attack and slow
    /// release. The window grows with the sample rate (~45 Hz resolution even at
    /// 192 kHz), and levels are measured against the recent peak (automatic gain)
    /// so the bars keep moving whether the music is loud or quiet.
    /// </summary>
    sealed class Analyzer
    {
        const float SpectrumRange = 40f;
        const float LevelRange = 28f;

        readonly int _n;
        readonly float[] _window;
        readonly float[] _re;
        readonly float[] _im;
        readonly float[] _db = new float[BandCount];
        readonly int[] _bandLo = new int[BandCount];
        readonly int[] _bandHi = new int[BandCount];
        float _peakDb = -40;
        float _peakLevelDb = -30;
        // Beats: the bass against its own recent average, and the time since the last one.
        readonly float _hopSeconds;
        float _bassAverage = 1e-4f;
        float _sinceBeat = 1;
        // The waveform's recent peak, so a quiet song draws as tall as a loud one.
        float _wavePeak = 0.05f;

        public Analyzer(int sampleRate)
        {
            _hopSeconds = Math.Max(256, sampleRate / 90) / (float)sampleRate;
            _n = 1024;
            while (_n < sampleRate / 45 && _n < 8192) _n <<= 1;
            _window = new float[_n];
            _re = new float[_n];
            _im = new float[_n];
            for (int i = 0; i < _n; i++) _window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (_n - 1));

            double lo = 40, hi = Math.Min(16000, sampleRate / 2.0 - 1);
            for (int b = 0; b < BandCount; b++)
            {
                double f0 = lo * Math.Pow(hi / lo, (double)b / BandCount);
                double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / BandCount);
                _bandLo[b] = Math.Clamp((int)(f0 * _n / sampleRate), 1, _n / 2 - 1);
                _bandHi[b] = Math.Clamp((int)Math.Ceiling(f1 * _n / sampleRate), _bandLo[b] + 1, _n / 2);
            }
        }

        public int Size => _n;

        public void Run(float[] ring, int start, float[] bands, ref float level, ref float beat)
        {
            double sumSquares = 0;
            for (int i = 0; i < _n; i++)
            {
                float s = ring[(start + i) % _n];
                sumSquares += s * s;
                _re[i] = s * _window[i];
                _im[i] = 0;
            }
            Fft(_re, _im);

            float frameMax = -120;
            for (int b = 0; b < BandCount; b++)
            {
                float mag = 0;
                for (int k = _bandLo[b]; k < _bandHi[b]; k++)
                    mag = MathF.Max(mag, MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]));
                // Gentle tilt (+~5 dB across the range) so highs are not always dwarfed by the bass.
                _db[b] = 20 * MathF.Log10(mag * 4f / _n + 1e-9f) + b * 0.11f;
                frameMax = MathF.Max(frameMax, _db[b]);
            }

            // Automatic gain: follow peaks instantly, let go slowly; never amplify near-silence.
            _peakDb = MathF.Max(frameMax, MathF.Max(_peakDb - 0.06f, -42));
            for (int b = 0; b < BandCount; b++)
            {
                float v = Math.Clamp((_db[b] - (_peakDb - SpectrumRange)) / SpectrumRange, 0, 1);
                v *= v * (3 - 2 * v) * 0.35f + 0.65f * v;
                bands[b] += (v - bands[b]) * (v > bands[b] ? 0.6f : 0.15f);
            }

            float levelDb = 20 * MathF.Log10((float)Math.Sqrt(sumSquares / _n) + 1e-9f);
            _peakLevelDb = MathF.Max(levelDb, MathF.Max(_peakLevelDb - 0.05f, -38));
            float lv = Math.Clamp((levelDb - (_peakLevelDb - LevelRange)) / LevelRange, 0, 1);
            level += (lv - level) * (lv > level ? 0.5f : 0.12f);

            // A beat: the low end (to about 150 Hz) jumps well above where it has been lately,
            // and not too soon after the last one. The average follows over about a second.
            float bass = 0;
            for (int b = 0; b < 10; b++)
                for (int k = _bandLo[b]; k < _bandHi[b]; k++)
                    bass += _re[k] * _re[k] + _im[k] * _im[k];
            _sinceBeat += _hopSeconds;
            bool hit = bass > _bassAverage * 1.45f && bass > 1e-5f * _n && _sinceBeat > 0.18f;
            _bassAverage += (bass - _bassAverage) * Math.Min(1, _hopSeconds * 1.2f);
            if (hit)
            {
                beat = 1;
                _sinceBeat = 0;
            }
            else
            {
                beat *= MathF.Exp(-_hopSeconds / 0.12f);
            }
        }

        /// <summary>
        /// The latest stretch of sound as <see cref="WaveCount"/> points, starting where it rises
        /// through zero so the trace stands still instead of jittering, and levelled to its recent peak.
        /// </summary>
        public void Wave(float[] ring, int end, int sampleRate, float[] wave)
        {
            int span = Math.Clamp(sampleRate / 50, WaveCount, ring.Length / 3);
            int from = end - 2 * span;
            int start = from;
            for (int i = 1; i < span; i++)
            {
                float a = ring[Wrap(from + i - 1)], b = ring[Wrap(from + i)];
                if (a < 0 && b >= 0)
                {
                    start = from + i;
                    break;
                }
            }
            float peak = 0;
            for (int p = 0; p < WaveCount; p++)
            {
                float s = ring[Wrap(start + (int)((long)p * span / WaveCount))];
                wave[p] = s;
                peak = MathF.Max(peak, MathF.Abs(s));
            }
            _wavePeak = MathF.Max(peak, MathF.Max(_wavePeak * 0.985f, 0.02f));
            float gain = 0.92f / _wavePeak;
            for (int p = 0; p < WaveCount; p++) wave[p] = Math.Clamp(wave[p] * gain, -1, 1);

            int Wrap(int i) => ((i % ring.Length) + ring.Length) % ring.Length;
        }

        static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        float tr = re[b] * cr - im[b] * ci;
                        float ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        float nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ COM

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
