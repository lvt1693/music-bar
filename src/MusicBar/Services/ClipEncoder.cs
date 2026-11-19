using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace MusicBar.Services;

/// <summary>
/// A short silent MP4 (H.264) from frames the app drew, with Windows' own encoder (Media Foundation, through WinRT):
/// the gallery's animated preview of a look, made where the look runs. Nothing extra ships: every Windows 10 and 11
/// has the encoder (the N editions only with the Media Feature Pack; there the look goes with its picture alone).
/// </summary>
public static class ClipEncoder
{
    /// <summary>
    /// frames: width × height BGRA pixels each, top row first. Null when Windows can't encode (logged), never throws.
    /// </summary>
    public static async Task<byte[]?> Mp4Async(IReadOnlyList<byte[]> frames, int width, int height, int fps, CancellationToken ct = default)
    {
        if (frames.Count == 0 || width % 2 != 0 || height % 2 != 0) return null;
        try
        {
            var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
            input.FrameRate.Numerator = (uint)fps;
            input.FrameRate.Denominator = 1;
            var step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
            var source = new MediaStreamSource(new VideoStreamDescriptor(input))
            {
                BufferTime = TimeSpan.Zero,
                CanSeek = false,
                Duration = step * frames.Count,
            };
            int next = 0;
            source.SampleRequested += (_, e) =>
            {
                if (next >= frames.Count)
                {
                    e.Request.Sample = null; // the end of the stream
                    return;
                }
                var sample = MediaStreamSample.CreateFromBuffer(BottomUp(frames[next], width, height).AsBuffer(), step * next);
                sample.Duration = step;
                e.Request.Sample = sample;
                next++;
            };

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            profile.Audio = null;
            profile.Video.Width = (uint)width;
            profile.Video.Height = (uint)height;
            profile.Video.FrameRate.Numerator = (uint)fps;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.Bitrate = 1_500_000;

            using var output = new InMemoryRandomAccessStream();
            // The software encoder: a graphics card's encoder may refuse a strip only 84 pixels tall.
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = false };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, profile).AsTask(ct);
            if (!prepared.CanTranscode)
            {
                Log.Info($"No preview clip: Windows can't encode it ({prepared.FailureReason})");
                return null;
            }
            await prepared.TranscodeAsync().AsTask(ct);

            var bytes = new byte[output.Size];
            using var reader = new DataReader(output.GetInputStreamAt(0));
            await reader.LoadAsync((uint)output.Size).AsTask(ct);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("Encoding the preview clip failed", ex);
            return null;
        }
    }

    /// <summary>Media Foundation reads uncompressed RGB bottom row first (a DIB's order): the frame turned over for it.</summary>
    static byte[] BottomUp(byte[] topDown, int width, int height)
    {
        int stride = width * 4;
        var flipped = new byte[topDown.Length];
        for (int y = 0; y < height; y++)
            System.Buffer.BlockCopy(topDown, y * stride, flipped, (height - 1 - y) * stride, stride);
        return flipped;
    }
}
