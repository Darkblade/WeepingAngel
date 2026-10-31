using System.Text.Json;
using System.Reflection;
using DontBlink.Services;
internal static class TestSessionLogChecks
{
    private static string Read(string path){using var reader=new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete));return reader.ReadToEnd();}
    public static async Task RunAsync(string root)
    {
        string folder=Path.Combine(root,"test-session-log");var log=new TestSessionLog(2048);log.Start(folder,"baseline");string session=log.Snapshot().SessionId!;log.Marker("Walk past Garden camera");
        var deadline=DateTime.UtcNow.AddSeconds(5);while(!Read(log.Snapshot().Path!).Contains("Walk past Garden camera")){if(DateTime.UtcNow>deadline)throw new Exception("Live marker was not flushed");await Task.Delay(20);}
        Check(true,"test markers can be read while a session is running");
        Parallel.For(0,100,i=>log.Record("sample",new{index=i}));await log.StopAsync();
        var files=Directory.GetFiles(folder,"*.jsonl");Check(files.Length<=4&&files.All(p=>new FileInfo(p).Length<=2048),"metadata logs rotate within four bounded segments");
        var records=files.SelectMany(p=>Read(p).Split('\n',StringSplitOptions.RemoveEmptyEntries)).Select(line=>JsonDocument.Parse(line)).OrderBy(d=>d.RootElement.GetProperty("sequence").GetInt64()).ToArray();
        try{Check(records.All(d=>d.RootElement.GetProperty("sessionId").GetString()==session)&&records.Select(d=>d.RootElement.GetProperty("sequence").GetInt64()).Distinct().Count()==records.Length,"concurrent records retain one session and unique ordered sequence numbers");Check(records.Last().RootElement.GetProperty("kind").GetString()=="session_end","stopping flushes the final session record");}finally{foreach(var record in records)record.Dispose();}
        log.Start(folder,"next");await log.StopAsync();Check(log.Snapshot().SessionId!=session&&Directory.GetFiles(folder,"previous-*.jsonl").Length==0,"new session clears old rolling segments and gets a new ID");
        var registry=new DeviceRegistry(Path.Combine(root,"test-log-devices.json"));await using var follower=new LiveActivityFollower(registry.List);var camera=registry.List().Single(d=>d.Type=="blink-outdoor-3");var hub=registry.List().Single(d=>d.Type=="blink-sync-2");
        string packetFolder=Path.Combine(root,"packet-log");follower.TestLog.Start(packetFolder,"packet fixture");byte[] pcap=LiveActivityChecks.Packet(camera,hub,60000,0x18,123,80);byte[] frame=pcap[16..];Array.Fill(frame,(byte)0xAB,54,80);
        typeof(LiveActivityFollower).GetMethod("Observe",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(follower,[frame,DateTimeOffset.UtcNow,frame.Length]);await follower.TestLog.StopAsync();var lines=Read(Path.Combine(packetFolder,"current.jsonl")).Split('\n',StringSplitOptions.RemoveEmptyEntries);using var packet=JsonDocument.Parse(lines.Single(l=>l.Contains("\"kind\":\"packet\"")));var data=packet.RootElement.GetProperty("data");Check(data.GetProperty("sourceIp").GetString()==camera.IpAddress&&data.GetProperty("payloadBytes").GetInt32()==80&&data.GetProperty("sequence").GetUInt32()==123,"packet metadata preserves device identity, payload length and TCP sequence");Check(!packet.RootElement.ToString().Contains("q6ur")&&!data.TryGetProperty("payload",out _),"packet payload bytes are excluded from diagnostic records");
        Check(!follower.TestLog.Snapshot().Active,"stopped logging leaves capture processing independent");
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
}