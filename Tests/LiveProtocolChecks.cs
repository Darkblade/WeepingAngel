using DontBlink.Services;
using System.Text.Json;
internal static class LiveProtocolChecks
{
    public static void Run()
    {
        var frames=new List<LiveProtocolFrame>();var messages=new List<(string,int)>();
        var observer=new LiveProtocolDiagnostics(2,3,frames.Add,(text,level)=>messages.Add((text,level)));
        observer.Observe("received",0,99,12345);observer.Observe("sent",5,99,2000);
        if(frames.Count!=0)throw new Exception("Media logged per frame");
        observer.Observe("received",0xEF,42,7);observer.Observe("received",0x06,0,0);
        for(int i=0;i<300;i++)observer.Observe("received",0x0A,(uint)i,0);
        if(frames.Count!=LiveProtocolDiagnostics.FrameLimit||messages.Any(m=>m.Item2!=3))throw new Exception("Protocol diagnostic bound/verbosity failed");
        if(frames[0].TypeName!="Unknown"||frames[0].HeaderWord!=42||frames[0].PayloadLength!=7||frames[0].NetworkId!=2||frames[0].CameraId!=3)throw new Exception("Unknown metadata identity lost");
        string summary=observer.Summary();if(!summary.Contains("12345")||!summary.Contains("2000")||!summary.Contains("174 additional")||!summary.Contains("300 frames"))throw new Exception("Protocol summary counts lost");
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(frames));var entry=json.RootElement[0];if(entry.TryGetProperty("Payload",out _)||entry.TryGetProperty("Token",out _)||entry.TryGetProperty("Serial",out _)||entry.TryGetProperty("Server",out _))throw new Exception("Sensitive protocol field exposed");
        Console.WriteLine("PASS bounded live protocol metadata retains unknown types, counts media without frame logs, and excludes credentials/payloads");
    }
}
