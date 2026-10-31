using System.Buffers.Binary;
using System.Net;
namespace DontBlink.Services;

public static class CaptureActivityReader
{
    private sealed class Session(LanDevice device,DateTimeOffset time)
    {
        public readonly LanDevice Device=device;
        public readonly DateTimeOffset Start=time;
        public DateTimeOffset Last=time;
        public long Bytes;
        public bool Closed;
        public readonly HashSet<(uint,int)> Segments=[];
    }
    public static CaptureReplayResult Read(Stream stream,string fileName,LanDevice[] devices)
    {
        using var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true);
        byte[] header=reader.ReadBytes(24);if(header.Length!=24)throw new InvalidDataException("The capture header is incomplete.");
        uint magic=BinaryPrimitives.ReadUInt32LittleEndian(header);bool little=magic is 0xa1b2c3d4 or 0xa1b2cd34 or 0xa1b23c4d;bool big=magic is 0xd4c3b2a1 or 0x34cdb2a1 or 0x4d3cb2a1;
        if(!little&&!big)throw new InvalidDataException("Use a classic PCAP or FRITZ!Box .eth capture. PCAPNG is not supported yet.");
        bool patched=magic is 0xa1b2cd34 or 0x34cdb2a1,nano=magic is 0xa1b23c4d or 0x4d3cb2a1;
        uint U32(ReadOnlySpan<byte> data)=>little?BinaryPrimitives.ReadUInt32LittleEndian(data):BinaryPrimitives.ReadUInt32BigEndian(data);
        if((U32(header.AsSpan(20))&0xffff)!=1)throw new InvalidDataException("Choose an Ethernet capture such as the plain ath0 network interface.");
        int packets=0;long read=24;DateTimeOffset? first=null,last=null;var sessions=new Dictionary<string,Session>();var observedSessions=new List<Session>();
        var cameras=devices.Where(d=>d.Type=="blink-outdoor-3"&&d.ParentId!=null).ToArray();
        while(true){byte[] record=reader.ReadBytes(patched?24:16);if(record.Length==0)break;if(record.Length!=(patched?24:16))throw new InvalidDataException("The capture ended inside a packet header.");
            uint length=U32(record.AsSpan(8));if(length>65536||read+record.Length+length>128L*1024*1024||packets>=1000000)throw new InvalidDataException("Capture is too large or contains an invalid packet length (maximum 128 MB / 1 million packets).");
            byte[] frame=reader.ReadBytes((int)length);if(frame.Length!=length)throw new InvalidDataException("The capture ended inside a packet.");read+=record.Length+length;packets++;
            var time=DateTimeOffset.FromUnixTimeSeconds(U32(record)).AddTicks((long)U32(record.AsSpan(4))*(nano?1:10)/(nano?100:1));first=first==null||time<first?time:first;last=last==null||time>last?time:last;
            int ip=14;if(frame.Length<14)continue;ushort ether=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(12));if(ether==0x8100&&frame.Length>=18){ether=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(16));ip=18;}if(ether!=0x0800||frame.Length<ip+20||(frame[ip]>>4)!=4||frame[ip+9]!=6)continue;
            int ihl=(frame[ip]&15)*4;if(ihl<20||frame.Length<ip+ihl+20)continue;if((BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(ip+6))&0x3fff)!=0)continue;
            string src=new IPAddress(frame.AsSpan(ip+12,4)).ToString(),dst=new IPAddress(frame.AsSpan(ip+16,4)).ToString();int tcp=ip+ihl,th=(frame[tcp+12]>>4)*4;if(th<20||frame.Length<tcp+th)continue;
            int sp=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcp)),dp=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcp+2));byte flags=frame[tcp+13];
            bool incoming=dp==443;var camera=cameras.FirstOrDefault(c=>c.IpAddress==(incoming?src:dst));if(camera==null)continue;var parent=devices.FirstOrDefault(d=>d.Id==camera.ParentId);if(parent?.IpAddress!=(incoming?dst:src)||(incoming?dp:sp)!=443)continue;
            string observed=Convert.ToHexString(frame.AsSpan(incoming?6:0,6));if(observed!=camera.MacAddress.Replace(":",""))continue;
            string key=$"{camera.Id}:{(incoming?sp:dp)}";if(incoming&&(flags&0x12)==0x02){if(!sessions.TryGetValue(key,out var old)||old.Closed){var created=new Session(camera,time);sessions[key]=created;observedSessions.Add(created);}}
            if(!sessions.TryGetValue(key,out var session))continue;session.Last=time;
            int ipLength=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(ip+2));int payload=Math.Max(0,Math.Min(frame.Length,ip+ipLength)-(tcp+th));
            if(incoming&&payload>0){uint seq=BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(tcp+4));if(session.Segments.Add((seq,payload)))session.Bytes+=payload;}
            if((flags&0x05)!=0)session.Closed=true;
        }
        var activities=observedSessions.OrderBy(s=>s.Start).Select(s=>new LocalActivity(s.Device.Id,s.Device.Name,s.Start,s.Last,s.Bytes,s.Closed)).ToArray();
        return new(fileName,first,last,packets,activities,"Historical replay only. Activity is inferred from a camera-initiated TLS connection to its configured module with matching IP/MAC; it does not prove motion or a saved recording. Replay never starts live view or updates live last-seen status.");
    }
}
