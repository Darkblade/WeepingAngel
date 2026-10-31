namespace DontBlink.Services;

public record ExternalActivityEvent(Guid DeviceId,string EventId,string Kind,long UploadedBytes,DateTimeOffset ObservedUtc,string Source);
public sealed partial class LiveActivityFollower
{
    private bool externalMode;
    private readonly HashSet<string> externalIds=[];
    public async Task StartRouterAsync(FritzCaptureOptions options,HttpMessageHandler? handler=null)
    {
        options.BaseUri();await lifecycle.WaitAsync();try{await StopCoreAsync();lock(sync){connections.Clear();file="FRITZ!Box / "+options.Interface;state="Connecting to router capture";running=true;lastPacket=null;}stop=new();var token=stop.Token;worker=Task.Run(()=>RouterLoopAsync(options,handler,token));}finally{lifecycle.Release();}
    }
    private async Task RouterLoopAsync(FritzCaptureOptions options,HttpMessageHandler? handler,CancellationToken ct)
    {
        int failures=0;try{while(!ct.IsCancellationRequested){DateTime started=DateTime.UtcNow;try{await using var source=await FritzCaptureConnection.OpenAsync(options,ct,handler);lock(sync){state="Following router stream · short snapshots · no capture file";connections.Clear();lastPacket=DateTimeOffset.UtcNow;}await ReadNetworkAsync(source.Stream,ct);throw new IOException("Router stream ended.");}
                catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
                catch(HttpRequestException ex)when(ex.HttpRequestError==HttpRequestError.SecureConnectionError){lock(sync){state="Router TLS verification failed; check the router address and trusted certificate. Monitoring paused.";connections.Clear();}return;}
                catch(Exception ex)when(ex is RouterLoginException or InvalidDataException or ArgumentException){lock(sync){state=ex is RouterLoginException?ex.Message:"Capture format unsupported; monitoring paused.";connections.Clear();}return;}
                catch(Exception){if(DateTime.UtcNow-started>TimeSpan.FromSeconds(60))failures=0;int delay=Math.Min(30,1<<Math.Min(failures++,5));lock(sync){connections.Clear();state=$"Router disconnected · reconnecting in {delay}s";}await Task.Delay(TimeSpan.FromSeconds(delay),ct);}
            }}catch(OperationCanceledException)when(ct.IsCancellationRequested){}finally{lock(sync)running=false;}
    }
    private async Task ReadNetworkAsync(Stream stream,CancellationToken ct)
    {
        byte[] header=new byte[24];await stream.ReadExactlyAsync(header,ct);var format=Format(header);byte[] record=new byte[format.Patched?24:16];while(true){int count=await stream.ReadAsync(record.AsMemory(0,1),ct);if(count==0)return;await stream.ReadExactlyAsync(record.AsMemory(1),ct);uint length=format.U32(record.AsSpan(8));if(length>262144)throw new InvalidDataException("Invalid packet size.");byte[] frame=new byte[length];await stream.ReadExactlyAsync(frame,ct);var time=DateTimeOffset.FromUnixTimeSeconds(format.U32(record)).AddTicks((long)format.U32(record.AsSpan(4))*(format.Nano?1:10)/(format.Nano?100:1));if((DateTimeOffset.UtcNow-time).Duration()>TimeSpan.FromSeconds(15))continue;lock(sync){lastPacket=time;Observe(frame,time,(int)format.U32(record.AsSpan(12)));}}
    }
    public async Task StartExternalAsync()
    {
        await lifecycle.WaitAsync();try{await StopCoreAsync();lock(sync){externalIds.Clear();connections.Clear();externalMode=true;running=true;file="External network events";state="Waiting for paired external event producer";lastPacket=null;}}finally{lifecycle.Release();}
    }
    public void AcceptExternal(ExternalActivityEvent input)
    {
        if((DateTimeOffset.UtcNow-input.ObservedUtc).Duration()>TimeSpan.FromSeconds(15)||input.UploadedBytes is <0 or >268435456||(input.EventId?.Length??0) is <1 or >100||(input.Source?.Length??0) is <1 or >80||(input.Source?.Any(char.IsControl)??false)||input.Kind is not("connection" or "upload" or "end" or "refused"))throw new ArgumentException("Invalid or stale external activity event.");
        var camera=devices().FirstOrDefault(d=>d.Id==input.DeviceId&&d.Type=="blink-outdoor-3")??throw new ArgumentException("Event camera is not registered.");lock(sync){if(!externalMode||!running)throw new InvalidOperationException("Enable External network events in Windows first.");string id=input.DeviceId+":"+input.EventId;if(externalIds.Contains(id))return;if(externalIds.Count>=1024)externalIds.Clear();externalIds.Add(id);var connection=new Connection(camera,input.ObservedUtc,0){Last=input.ObservedUtc,Established=input.Kind is "upload" or "end",Closed=input.Kind is "end" or "refused",Refused=input.Kind=="refused",Bytes=input.UploadedBytes,Source="External report: "+input.Source};connections["external:"+input.DeviceId]=connection;lastPacket=input.ObservedUtc;state="Receiving external reports · producer assertions are not packet verification";}
    }
}
