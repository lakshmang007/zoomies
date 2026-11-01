using System.Diagnostics;
using System.IO;

namespace DemoRecorder;

/// <summary>Records the primary screen with ffmpeg (gdigrab) into an H.264 MP4 using the GPU encoder.</summary>
internal sealed class ScreenRecorder
{
    private readonly Process _ffmpeg;

    public ScreenRecorder(string ffmpeg, string output, int width, int height, string encoder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        string quality = encoder switch
        {
            "h264_nvenc" => "-preset p6 -tune hq -rc vbr -cq 19 -b:v 0",
            "h264_amf" => "-quality quality -rc cqp -qp_i 18 -qp_p 20",
            _ => "-q:v 3",
        };
        _ffmpeg = Process.Start(new ProcessStartInfo(ffmpeg,
            $"-hide_banner -loglevel error -y -f gdigrab -framerate 30 -draw_mouse 1 -offset_x 0 -offset_y 0 " +
            $"-video_size {width}x{height} -i desktop -c:v {encoder} {quality} -pix_fmt yuv420p -movflags +faststart \"{output}\"")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        })!;
        Thread.Sleep(800); // let capture start before the first frame that matters
    }

    public void Stop()
    {
        try
        {
            _ffmpeg.StandardInput.Write('q');
            _ffmpeg.StandardInput.Flush();
        }
        catch (IOException) { }
        if (!_ffmpeg.WaitForExit(20000)) _ffmpeg.Kill();
    }

    /// <summary>ffmpeg bundled with CapCut has the screen grabber and GPU encoders; prefer the newest copy.</summary>
    public static string? FindFfmpeg()
    {
        string capcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapCut", "Apps");
        if (Directory.Exists(capcut))
        {
            var found = Directory.EnumerateFiles(capcut, "ffmpeg.exe", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .FirstOrDefault();
            if (found is not null) return found;
        }
        return null;
    }
}
