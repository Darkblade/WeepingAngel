using System.Buffers.Binary;
using System.Net;

namespace DontBlink.Services;

public record LiveLocalActivity(Guid DeviceId,string Name,DateTimeOffset StartedUtc,DateTimeOffset LastTrafficUtc,long ObservedBytes,string State,bool Pulsing,string Source="Capture");
public record LiveActivitySnapshot(bool Running,string FileName,string State,DateTimeOffset? LastPacketUtc,LiveLocalActivity[] Activities);

// Follows complete Ethernet PCAP records appended after attachment. Never replays old packets.
public sealed partial class LiveActivityFollower(Func<LanDevice[]> devices):IAsyncDisposable
{
    public const int Threshold=32*1024;
    private readonly object sync=new();
    private readonly SemaphoreSlim lifecycle=new(1,1);
    private CancellationTokenSource? stop;
    private Task? worker;
    private bool running;
    private string file="",state="Not connected";
    private DateTimeOffset? lastPacket;
    private readonly Dictionary<string,Connection> connections=[];
    private sealed class Connection(LanDevice camera,DateTimeOffset time,uint sequence)
    {
        public readonly LanDevice Camera=camera;
        public readonly DateTimeOffset Started=time;
        public DateTimeOffset Last=time;
        public readonly uint Base=sequence;
        public bool Established,Closed,Refused;
        public string Source="Capture";
        public long Bytes;
        public readonly List<(long Start,long End)> Ranges=[];
        public void Add(uint seq,int count){long start=unchecked(seq-Base),end=start+count;if(start>int.MaxValue||end>256L*1024*1024)return;long extra=count;foreach(var r in Ranges)extra-=Math.Max(0,Math.Min(end,r.End)-Math.Max(start,r.Start));if(extra<=0)return;if(Ranges.Count>=4096){Closed=true;return;}Bytes+=extra;int i=0;while(i<Ranges.Count&&Ranges[i].End<start)i++;while(i<Ranges.Count&&Ranges[i].Start<=end){start=Math.Min(start,Ranges[i].Start);end=Math.Max(end,Ranges[i].End);Ranges.RemoveAt(i);}Ranges.Insert(i,(start,end));}
    }
    public LiveActivitySnapshot Snapshot(){lock(sync){var now=DateTimeOffset.UtcNow;return new(running,file,running&&state.StartsWith("Following")&&lastPacket is {} t&&now-t>TimeSpan.FromSeconds(15)?"No fresh packets · check capture is still running":state,lastPacket,connections.Values.Where(c=>now-c.Last<TimeSpan.FromSeconds(30)).Select(c=>new LiveLocalActivity(c.Camera.Id,c.Camera.Name,c.Started,c.Last,c.Bytes,c.Refused?"Connection refused":c.Closed?"Connection ended":c.Established&&c.Bytes>=Threshold?"Possible recording":c.Established?"Local connection":"Connection attempt",running&&c.Established&&!c.Closed&&c.Bytes>=Threshold&&now-c.Last<TimeSpan.FromSeconds(8),c.Source)).ToArray());}}
    public async Task StartAsync(string path)
    {
        await lifecycle.WaitAsync();try{await StopCoreAsync();FileStream? stream=null;try{stream=new(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.Asynchronous);if(stream.Length<24)throw new InvalidDataException("Wait for the download to contain its PCAP header, then attach again.");byte[] header=new byte[24];await stream.ReadExactlyAsync(header);var format=Format(header);long baseline=stream.Length;lock(sync){connections.Clear();file=Path.GetFileName(path);state="Following new packets · existing history ignored";running=true;lastPacket=DateTimeOffset.UtcNow;}stop=new();var owned=stream;stream=null;worker=Task.Run(()=>FollowAsync(owned,format,baseline,stop.Token));}finally{stream?.Dispose();}}finally{lifecycle.Release();}
    }
    public async Task StopAsync(){await lifecycle.WaitAsync();try{await StopCoreAsync();}finally{lifecycle.Release();}}
    private async Task StopCoreAsync(){externalMode=false;stop?.Cancel();if(worker!=null)await worker;stop?.Dispose();stop=null;worker=null;lock(sync){running=false;state="Stopped";connections.Clear();}}
    public async ValueTask DisposeAsync(){await StopAsync();await TestLog.StopAsync();}
    private readonly record struct PcapFormat(bool Little,bool Patched,bool Nano)
    {
        public uint U32(ReadOnlySpan<byte> bytes)=>Little?BinaryPrimitives.ReadUInt32LittleEndian(bytes):BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
    private static PcapFormat Format(byte[] h)
    {
        uint m=BinaryPrimitives.ReadUInt32LittleEndian(h);bool little=m is 0xa1b2c3d4 or 0xa1b2cd34 or 0xa1b23c4d;
        if(!little&&m is not(0xd4c3b2a1 or 0x34cdb2a1 or 0x4d3cb2a1))throw new InvalidDataException("Choose an Ethernet .eth / classic PCAP download; PCAPNG is not supported.");
        var f=new PcapFormat(little,m is 0xa1b2cd34 or 0x34cdb2a1,m is 0xa1b23c4d or 0x4d3cb2a1);if((f.U32(h.AsSpan(20))&65535)!=1)throw new InvalidDataException("Choose the Ethernet ath0 capture.");return f;
    }
    private async Task FollowAsync(FileStream stream,PcapFormat format,long baseline,CancellationToken ct)
    {
        await using(stream){try{int recordSize=format.Patched?24:16;byte[] record=new byte[recordSize];while(true){ct.ThrowIfCancellationRequested();long position=stream.Position,length=stream.Length;if(length<position||length<baseline)throw new IOException("Capture file was truncated. Attach the new capture again.");if(length-position<recordSize){await Task.Delay(100,ct);continue;}await stream.ReadExactlyAsync(record,ct);uint count=format.U32(record.AsSpan(8));if(count>262144)throw new InvalidDataException("Invalid capture packet length; monitoring stopped.");if(length-stream.Position<count){stream.Position=position;await Task.Delay(100,ct);continue;}if(position<baseline){stream.Position+=count;continue;}byte[] packet=new byte[count];await stream.ReadExactlyAsync(packet,ct);var time=DateTimeOffset.FromUnixTimeSeconds(format.U32(record)).AddTicks((long)format.U32(record.AsSpan(4))*(format.Nano?1:10)/(format.Nano?100:1));if((DateTimeOffset.UtcNow-time).Duration()>TimeSpan.FromSeconds(15))continue;lock(sync){lastPacket=time;state="Following live capture · inferred activity";Observe(packet,time,(int)format.U32(record.AsSpan(12)));}}}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){}
            catch(Exception ex){lock(sync){state=ex is EndOfStreamException?"Capture stopped: file was truncated during reading. Attach again.":"Capture stopped: "+ex.Message;connections.Clear();}}
            finally{lock(sync)running=false;}}
    }
    private void Observe(byte[] frame,DateTimeOffset time,int originalLength=0)
    {
        ObserveTestMetadata(frame,time,originalLength);
        int ip=14;if(frame.Length<14)return;ushort ether=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(12));if(ether==0x8100&&frame.Length>=18){ether=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(16));ip=18;}if(ether!=0x800||frame.Length<ip+20||frame[ip+9]!=6||(frame[ip]>>4)!=4)return;
        int ihl=(frame[ip]&15)*4;if(ihl<20||frame.Length<ip+ihl+20||(BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(ip+6))&0x3fff)!=0)return;int tcp=ip+ihl,th=(frame[tcp+12]>>4)*4,ipLength=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(ip+2));if(th<20||ipLength<ihl+th||frame.Length<tcp+th||(originalLength==0?frame.Length:originalLength)<ip+ipLength)return;
        string src=new IPAddress(frame.AsSpan(ip+12,4)).ToString(),dst=new IPAddress(frame.AsSpan(ip+16,4)).ToString();int sp=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcp)),dp=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcp+2));bool incoming=dp==443;if(!incoming&&sp!=443)return;var all=devices();var camera=all.FirstOrDefault(d=>d.Type=="blink-outdoor-3"&&d.IpAddress==(incoming?src:dst));var parent=all.FirstOrDefault(d=>d.Id==camera?.ParentId);if(camera==null||parent==null||parent.IpAddress!=(incoming?dst:src))return;
        string sm=Convert.ToHexString(frame.AsSpan(6,6)),dm=Convert.ToHexString(frame.AsSpan(0,6));if(sm!=(incoming?camera:parent).MacAddress.Replace(":","")||dm!=(incoming?parent:camera).MacAddress.Replace(":",""))return;
        byte flags=frame[tcp+13];uint seq=BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(tcp+4));string key=$"{camera.Id}:{(incoming?sp:dp)}";
        if(incoming&&(flags&0x12)==2){if(!connections.TryGetValue(key,out var old)||old.Closed||time-old.Last>TimeSpan.FromSeconds(15)){if(connections.Count>=256)connections.Remove(connections.MinBy(c=>c.Value.Last).Key);connections[key]=new(camera,time,unchecked(seq+1));}}
        if(!connections.TryGetValue(key,out var connection))return;connection.Last=time;if(!incoming&&(flags&0x12)==0x12)connection.Established=true;
        int payload=ipLength-ihl-th;if(incoming&&connection.Established&&!connection.Closed&&payload>0)connection.Add(seq,payload);
        if((flags&5)!=0){connection.Closed=true;connection.Refused=!connection.Established&&(flags&4)!=0;}
    }
}
