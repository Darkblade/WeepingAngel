using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DontBlink.Services;

public sealed record RecordingOverlay(bool Enabled = false, bool Timestamp = true, bool Camera = true,
    string Watermark = "DontBlink", string Placement = "Bottom left", string Font = "Segoe UI", int Size = 24, string Format = "Date and time with seconds", bool Utc = false)
{
    public static readonly string[] Placements = ["Top left", "Top right", "Bottom left", "Bottom right"];
    public static readonly string[] Fonts = ["Segoe UI", "Arial", "Consolas", "Tahoma"];
    public static readonly string[] Formats = ["Date and time", "Date and time with seconds", "Date only"];
    public RecordingOverlay Validated() => this with {
        Watermark = (Watermark ?? "")[..Math.Min(120, (Watermark ?? "").Length)],
        Placement = Placements.Contains(Placement) ? Placement : Placements[2],
        Font = Fonts.Contains(Font) ? Font : Fonts[0], Size = Math.Clamp(Size,12,72),
        Format = Formats.Contains(Format) ? Format : Formats[1]
    };
    private static string Escape(string text) => text.Replace("\\", "＼").Replace("{", "（").Replace("}", "）").Replace("\r", " ").Replace("\n", " ");
    internal string Subtitles(string camera, DateTime startedUtc)
    {
        var o = Validated();
        int alignment = o.Placement switch { "Top left"=>7,"Top right"=>9,"Bottom right"=>3,_=>1 };
        var script = new StringBuilder("[Script Info]\nScriptType: v4.00+\nPlayResX: 1280\nPlayResY: 720\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n");
        script.Append(CultureInfo.InvariantCulture, $"Style: Default,{o.Font},{o.Size},&H00FFFFFF,&H00FFFFFF,&H80000000,&H80000000,0,0,0,0,100,100,0,0,3,1,0,{alignment},20,20,20,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        string Time(int seconds) => TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);
        for(int second=0;second<360;second++)
        {
            var date = startedUtc.AddSeconds(second); if (!o.Utc) date = date.ToLocalTime();
            string format = o.Format switch { "Date only"=>"yyyy.MM.dd", "Date and time"=>"yyyy.MM.dd HH:mm",_=>"yyyy.MM.dd HH:mm:ss" };
            var parts = new List<string>();
            if(o.Timestamp) parts.Add(date.ToString(format,CultureInfo.InvariantCulture)+(o.Utc?" UTC":""));
            if(o.Camera) parts.Add(Escape(camera)); if(!string.IsNullOrWhiteSpace(o.Watermark)) parts.Add(Escape(o.Watermark));
            script.Append($"Dialogue: 0,{Time(second)},{Time(second+1)},Default,,0,0,0,,{string.Join(" · ",parts)}\n");
        }
        return script.ToString();
    }
    public async Task ApplyAsync(string input, string camera, DateTime startedUtc)
    {
        if(!Enabled) return;
        string folder=Path.Combine(Path.GetTempPath(),"DontBlink-overlay-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string output=input+".overlay.partial";
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder,"overlay.ass"),Subtitles(camera,startedUtc),new UTF8Encoding(false));
            var start=new ProcessStartInfo(LiveViewService.FindFfmpeg()) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,WorkingDirectory=folder };
            foreach(string arg in new[]{"-hide_banner","-loglevel","error","-i",input,"-map","0:v:0","-map","0:a:0?","-vf","setpts=PTS-STARTPTS,ass=overlay.ass","-c:v","libx264","-preset","veryfast","-crf","20","-pix_fmt","yuv420p","-af","asetpts=PTS-STARTPTS","-c:a","aac","-movflags","+faststart","-f","mp4",output})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("Could not start recording overlay renderer.");
            var errors=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
            if(process.ExitCode!=0)throw new IOException("Recording overlay failed: "+await errors);
            Mp4Validation.Validate(await File.ReadAllBytesAsync(output)); File.Move(output,input,true);
        }
        finally { if(File.Exists(output))File.Delete(output); Directory.Delete(folder,true); }
    }
}
