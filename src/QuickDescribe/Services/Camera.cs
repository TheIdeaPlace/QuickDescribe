using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace QuickDescribe.Services;

/// <summary>
/// Takes one picture with the camera: turns it on, lets it settle, keeps a frame and turns it off again, so its light is
/// on only while a picture is taken. A camera's first frames are dark and blurred while its exposure and focus adjust, so
/// frames are watched until their brightness stops changing.
/// </summary>
public static class Camera
{
    /// <summary>A camera with a colour picture, and which side of the PC it's on (null when Windows can't place it: one
    /// plugged in, or a virtual camera such as OBS's).</summary>
    public sealed record Device(string Id, string Name, Panel? Panel);

    /// <summary>The least time from the first frame: exposure is still adjusting before this even when it looks steady.</summary>
    internal static readonly TimeSpan MinWarmUp = TimeSpan.FromSeconds(0.8);

    /// <summary>The most: a scene that keeps changing (someone moving) gets the frame from now.</summary>
    internal static readonly TimeSpan MaxWarmUp = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait for a frame. Some cameras take seconds to start.</summary>
    private static readonly TimeSpan NoFrames = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait for a frame with the camera had alone. With another app showing it (the Camera app), Windows
    /// can let Quick Describe have it alone and then send no frames, so after this it's tried shared. It usually sends its
    /// first frame within a second and a half.</summary>
    private static readonly TimeSpan NoFramesAlone = TimeSpan.FromSeconds(4);

    /// <summary>No frames came in time.</summary>
    private sealed class NoFramesException : Exception;

    /// <summary>One picture at a time: a second request waits for the first to let go of the camera (it was given up, so
    /// that's soon), rather than finding the camera taken by Quick Describe itself.</summary>
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    internal const string NotAllowedText =
        "Quick Describe isn't allowed to use the camera. Turn on camera access for it in Windows Settings, Privacy & security, Camera.";

    internal const string NoCameraText = "This PC has no camera, or Windows can't find it.";

    internal const string NoFramesText = "The camera didn't send a picture. Another app may be using it: close that app and try again.";

    /// <summary>
    /// The camera to use: the one on the back first, since it faces what's in front of you rather than you; then the one
    /// facing you; then one Windows can't place, last because virtual cameras (OBS, NVIDIA Broadcast, phone apps) are
    /// among them and show something other than the room.
    /// </summary>
    internal static Device? Choose(IEnumerable<Device> devices) =>
        devices.OrderBy(d => d.Panel switch
        {
            Panel.Back => 0,
            Panel.Front => 1,
            null or Panel.Unknown => 3,
            _ => 2,
        }).FirstOrDefault();

    /// <summary>Whether the camera has settled: long enough since its first frame, and the brightness hardly changing
    /// between the last frame and this one (brightness from 0 to 1).</summary>
    internal static bool Settled(TimeSpan sinceFirst, double? last, double now) =>
        sinceFirst >= MaxWarmUp || (sinceFirst >= MinWarmUp && last is { } l && Math.Abs(now - l) < 0.01);

    /// <summary>The average brightness of BGRA pixels, from 0 to 1, from a sample of them.</summary>
    internal static double Brightness(byte[] bgra, int pixels)
    {
        const int step = 61; // a prime, so the sample doesn't line up with the picture's columns
        double sum = 0;
        var count = 0;
        for (var i = 0; i < pixels; i += step)
        {
            var at = i * 4;
            sum += 0.114 * bgra[at] + 0.587 * bgra[at + 1] + 0.299 * bgra[at + 2];
            count++;
        }
        return count == 0 ? 0 : sum / count / 255;
    }

    /// <summary>Takes a picture. Call on the UI thread: the first time, Windows asks the person whether Quick Describe may
    /// use the camera. Throws <see cref="PictureException"/> with a readable message when it can't.</summary>
    public static async Task<Picture> TakeAsync(CancellationToken cancel)
    {
        await OneAtATime.WaitAsync(cancel);
        try
        {
            // Source groups rather than video devices: only those with a colour picture, so an infrared Windows Hello
            // camera is never chosen.
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            cancel.ThrowIfCancellationRequested();
            var devices = groups
                .Select(g => (Group: g, Color: g.SourceInfos.FirstOrDefault(i => i.SourceKind == MediaFrameSourceKind.Color)))
                .Where(c => c.Color is not null)
                .Select(c => new Device(c.Group.Id, c.Group.DisplayName, c.Color!.DeviceInformation?.EnclosureLocation?.Panel))
                .ToList();
            var device = Choose(devices) ?? throw new PictureException(NoCameraText);
            var image = await TakeWithAsync(device.Id, cancel);
            return new Picture(image, devices.Count > 1 ? $"Camera, {device.Name}" : "Camera");
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>The camera for Quick Describe alone if it can be had (so it can choose the picture's size), or shared with
    /// another app that's using it.</summary>
    private static async Task<BitmapSource> TakeWithAsync(string groupId, CancellationToken cancel)
    {
        foreach (var sharing in new[] { MediaCaptureSharingMode.ExclusiveControl, MediaCaptureSharingMode.SharedReadOnly })
        {
            using var capture = await OpenAsync(groupId, sharing);
            if (capture is null) continue; // couldn't be had alone
            cancel.ThrowIfCancellationRequested();
            var source = capture.FrameSources.Values
                .Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color && s.Info.MediaStreamType != MediaStreamType.Photo)
                .OrderBy(s => s.Info.MediaStreamType == MediaStreamType.VideoRecord ? 0 : 1)
                .FirstOrDefault() ?? throw new PictureException("The camera has no picture to take.");
            if (sharing == MediaCaptureSharingMode.ExclusiveControl) await UseLargestAsync(source);

            using var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            var status = await reader.StartAsync();
            if (status == MediaFrameReaderStartStatus.ExclusiveControlNotAvailable && sharing == MediaCaptureSharingMode.ExclusiveControl)
            {
                // Another app is streaming from it: started alone, but frames are only had shared.
                Log.Write("camera", "The camera is in use by another app, so it's shared");
                continue;
            }
            if (status != MediaFrameReaderStartStatus.Success)
            {
                Log.Write("camera", $"The frame reader didn't start: {status}");
                throw new PictureException(status is MediaFrameReaderStartStatus.DeviceNotAvailable or MediaFrameReaderStartStatus.ExclusiveControlNotAvailable
                    ? NoFramesText
                    : $"The camera couldn't be started ({status}).");
            }
            var alone = sharing == MediaCaptureSharingMode.ExclusiveControl;
            try
            {
                return await SettledFrameAsync(reader, alone ? NoFramesAlone : NoFrames, cancel);
            }
            catch (NoFramesException) when (alone)
            {
                Log.Write("camera", "No frames with the camera alone, so it's shared");
            }
            catch (NoFramesException)
            {
                Log.Write("camera", "No frames came from the camera");
                throw new PictureException(NoFramesText);
            }
            finally
            {
                await reader.StopAsync();
            }
        }
        throw new PictureException(NoFramesText);
    }

    /// <summary>Starts the camera, or null when it can't be had alone (probably in use by another app).</summary>
    private static async Task<MediaCapture?> OpenAsync(string groupId, MediaCaptureSharingMode sharing)
    {
        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = await MediaFrameSourceGroup.FromIdAsync(groupId),
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = sharing,
            });
            return capture;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005))
        {
            capture.Dispose();
            Log.Write("camera", "Camera access is turned off for Quick Describe");
            throw new PictureException(NotAllowedText);
        }
        catch (Exception ex) when (sharing == MediaCaptureSharingMode.ExclusiveControl)
        {
            capture.Dispose();
            Log.Write("camera", $"The camera couldn't be had alone, so it's shared: {ex.Message.Trim()}");
            return null;
        }
        catch (Exception ex)
        {
            capture.Dispose();
            Log.Error("camera", "The camera couldn't be started", ex);
            throw new PictureException($"The camera couldn't be started: {ex.Message.Trim()}");
        }
    }

    /// <summary>The largest picture the camera gives at a usable frame rate: more detail for reading text. Its default is
    /// often 640 by 480.</summary>
    private static async Task UseLargestAsync(MediaFrameSource source)
    {
        var largest = source.SupportedFormats
            .Where(f => f.FrameRate.Denominator != 0 && (double)f.FrameRate.Numerator / f.FrameRate.Denominator >= 14)
            .OrderByDescending(f => (long)f.VideoFormat.Width * f.VideoFormat.Height)
            .FirstOrDefault();
        if (largest is null) return;
        try
        {
            await source.SetFormatAsync(largest);
        }
        catch (Exception ex)
        {
            Log.Write("camera", $"The camera's picture size couldn't be chosen, so its own is used: {ex.Message.Trim()}");
        }
    }

    /// <summary>Watches frames until the brightness settles. If frames stop coming, the last one is used once the warm-up
    /// is over, and with none at all it gives up: the camera is never left on waiting.</summary>
    private static async Task<BitmapSource> SettledFrameAsync(MediaFrameReader reader, TimeSpan noFrames, CancellationToken cancel)
    {
        var started = DateTime.UtcNow;
        DateTime? first = null;
        double? last = null;
        byte[]? pixels = null;
        int width = 0, height = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            using (var frame = reader.TryAcquireLatestFrame())
            {
                if (frame?.VideoMediaFrame?.SoftwareBitmap is { } bitmap)
                {
                    // Off the UI thread: a large frame is tens of megabytes. A SoftwareBitmap can be used from any thread.
                    var buffer = pixels;
                    (pixels, width, height) = await Task.Run(() => Copy(bitmap, buffer), cancel);
                    first ??= now;
                    var brightness = Brightness(pixels, width * height);
                    if (Settled(now - first.Value, last, brightness)) return Image(pixels, width, height, started);
                    last = brightness;
                }
                else if (first is { } f && pixels is not null && now - f >= MaxWarmUp)
                {
                    Log.Write("camera", "Frames stopped coming, so the last one is used");
                    return Image(pixels, width, height, started);
                }
                else if (first is null && now - started > noFrames)
                {
                    throw new NoFramesException();
                }
            }
            await Task.Delay(100, cancel);
        }
    }

    private static BitmapSource Image(byte[] pixels, int width, int height, DateTime started)
    {
        Log.Write("camera", $"Took a {width}x{height} picture, {(DateTime.UtcNow - started).TotalSeconds:0.0} s after starting");
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    /// <summary>A frame's pixels as BGRA, into the buffer from the last frame when it's the same size.</summary>
    private static (byte[] Pixels, int Width, int Height) Copy(SoftwareBitmap bitmap, byte[]? buffer)
    {
        var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
        try
        {
            var from = converted ?? bitmap;
            var size = from.PixelWidth * from.PixelHeight * 4;
            if (buffer?.Length != size) buffer = new byte[size];
            from.CopyToBuffer(buffer.AsBuffer());
            return (buffer, from.PixelWidth, from.PixelHeight);
        }
        finally
        {
            converted?.Dispose();
        }
    }
}
