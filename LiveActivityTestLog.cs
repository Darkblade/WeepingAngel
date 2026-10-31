using System.Buffers.Binary;
using System.Net;
namespace DontBlink.Services;
public sealed partial class LiveActivityFollower
{
    public TestSessionLog TestLog{get;}=new();
    private void ObserveTestMetadata(byte[] frame,DateTimeOffset time,int originalLength)
    {
        if(!TestLog.Snapshot().Active||frame.Length<34)return;int ip=14;ushort ether=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(12));if(ether==0x8100&&frame.Length>=38){ether=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(16));ip=18;}if(ether!=0x800||frame.Length<ip+20||(frame[ip]>>4)!=4)return;
        int ihl=(frame[ip]&15)*4,total=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(ip+2));byte protocol=frame[ip+9];if(ihl<20||frame.Length<ip+ihl+8||total<ihl+8||(BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(ip+6))&0x3fff)!=0||protocol is not(6 or 17))return;
        string src=new IPAddress(frame.AsSpan(ip+12,4)).ToString(),dst=new IPAddress(frame.AsSpan(ip+16,4)).ToString(),sm=Convert.ToHexString(frame.AsSpan(6,6)),dm=Convert.ToHexString(frame.AsSpan(0,6));var all=devices();var sender=all.FirstOrDefault(d=>d.IpAddress==src&&d.MacAddress.Replace(":","")==sm);var receiver=all.FirstOrDefault(d=>d.IpAddress==dst&&d.MacAddress.Replace(":","")==dm);if(sender==null&&receiver==null)return;
        int transport=ip+ihl,header=protocol==6?frame.Length>=transport+20?(frame[transport+12]>>4)*4:0:8;if(header<(protocol==6?20:8)||frame.Length<transport+header||total<ihl+header||(originalLength>0&&originalLength<ip+total))return;
        TestLog.Record("packet",new{sourceIp=src,destinationIp=dst,sourcePort=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(transport)),destinationPort=BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(transport+2)),protocol=protocol==6?"TCP":"UDP",sourceDevice=sender,destinationDevice=receiver,frameBytes=originalLength,ipBytes=total,payloadBytes=total-ihl-header,capturedBytes=frame.Length,tcpFlags=protocol==6?(int?)frame[transport+13]:null,sequence=protocol==6?(uint?)BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(transport+4)):null,acknowledgement=protocol==6?(uint?)BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(transport+8)):null},time);
    }
}