using System.Diagnostics;
namespace DontBlink.Services;

// Metadata only: no auth header, URL, serial, token, media or control payload bytes.
public sealed record LiveProtocolFrame(int NetworkId,int CameraId,string Direction,byte Type,string TypeName,uint HeaderWord,int PayloadLength,double ElapsedSeconds);
internal sealed class LiveProtocolDiagnostics(int network,int camera,Action<LiveProtocolFrame>? observed,Action<string,int>? diagnostic)
{
    internal const int FrameLimit=128;
    private readonly object sync=new();
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly Dictionary<(string,byte),(long Frames,long Bytes)> totals=[];
    private int emitted;private long suppressed;
    internal static string Name(byte type)=>type switch{0x00=>"Media",0x06=>"Authentication acknowledgement",0x08=>"Stop (reported)",0x0A=>"Keepalive",0x12=>"Latency statistics",0x13=>"Session configuration (reported)",0x14=>"Inline command",0x15=>"Accessory message",0x17=>"Session command",0x18=>"Session message",_=>"Unknown"};
    internal void Observe(string direction,byte type,uint word,int length)
    {
        lock(sync){
            var key=(direction,type);var previous=totals.GetValueOrDefault(key);totals[key]=(previous.Frames+1,previous.Bytes+length);
            // Media is counted in the summary, never logged per frame.
            if(type is 0 or 5)return;
            if(emitted>=FrameLimit){suppressed++;return;}emitted++;
            var entry=new LiveProtocolFrame(network,camera,direction,type,Name(type),word,length,Math.Round(clock.Elapsed.TotalSeconds,3));
            observed?.Invoke(entry);
            diagnostic?.Invoke($"Live camera {camera}: IMMIS {direction} 0x{type:X2} {entry.TypeName}; header word {word}; {length} payload bytes; +{entry.ElapsedSeconds:0.000}s.",3);
        }
    }
    internal string Summary(){lock(sync)return $"Live camera {camera}: IMMIS summary · "+string.Join(" · ",totals.OrderBy(t=>t.Key.Item1).ThenBy(t=>t.Key.Item2).Select(t=>$"{t.Key.Item1} 0x{t.Key.Item2:X2}: {t.Value.Frames} frames / {t.Value.Bytes} payload bytes"))+$" · {suppressed} additional metadata frames suppressed (limit {FrameLimit}).";}
}
