using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.WebSockets;

namespace DontBlink.Services;

// One serialized writer for keepalives, live commands and microphone packets.
internal sealed class RelayWriter(Stream stream,CancellationToken lifetime,Action<string,byte,uint,int>? observed=null)
{
    private readonly SemaphoreSlim _writes = new(1);
    public bool Ready { get; set; }
    public CancellationToken Lifetime => lifetime;
    public async Task SendAsync(byte type, uint sequence, ReadOnlyMemory<byte> payload = default)
    {
        byte[] packet = new byte[9 + payload.Length]; packet[0] = type;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(1), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(5), (uint)payload.Length); payload.CopyTo(packet.AsMemory(9));
        await _writes.WaitAsync(lifetime);
        try { await stream.WriteAsync(packet,lifetime); await stream.FlushAsync(lifetime); observed?.Invoke("sent",type,sequence,payload.Length); }
        finally { _writes.Release(); }
    }
    private int _talking;
    public bool TryTalk() => Interlocked.CompareExchange(ref _talking,1,0)==0;
    public void ReleaseTalk() => Volatile.Write(ref _talking,0);
}

internal static class RelayAudio
{
    // Blink uses AAC-LC 16kHz mono with ADTS framing on a 90kHz clock.
    // Framing reference: Teethree89/ha-blink-live-view-proxy (MIT), ptt.py/blink.py.
    internal static async Task ForwardAdtsAsync(Stream audio, RelayWriter relay, CancellationToken ct)
    {
        uint timestamp=0; byte[] header=new byte[7];
        while(true)
        {
            int first=await audio.ReadAsync(header.AsMemory(0,1),ct); if(first==0)return;
            await audio.ReadExactlyAsync(header.AsMemory(1),ct);
            if(header[0]!=255 || (header[1]&0xF6)!=0xF0)throw new IOException("Invalid microphone AAC framing.");
            int size=((header[3]&3)<<11)|(header[4]<<3)|(header[5]>>5), extra=(header[1]&1)==0?2:0;
            if(size<7+extra || size>8191)throw new IOException("Invalid microphone AAC length.");
            byte[] payload=new byte[size]; header.CopyTo(payload,0); await audio.ReadExactlyAsync(payload.AsMemory(7),ct);
            await relay.SendAsync(5,timestamp,payload); timestamp+=5760;
        }
    }
    public static async Task BridgeAsync(WebSocket socket, RelayWriter relay, int rate, Action<string>? log, CancellationToken aborted)
    {
        if(!relay.Ready || !relay.TryTalk())throw new IOException("Camera audio is not ready or is already in use.");
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(aborted,relay.Lifetime);
        stop.CancelAfter(TimeSpan.FromSeconds(30));
        Process? encoder=null; Task? forward=null; bool started=false;
        try
        {
            var start=new ProcessStartInfo(LiveViewService.FindFfmpeg()) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-hide_banner","-loglevel","error","-probesize","32","-analyzeduration","0","-f","s16le","-ar",rate.ToString(System.Globalization.CultureInfo.InvariantCulture),"-ac","1","-i","pipe:0","-ac","1","-ar","16000","-c:a","aac","-profile:a","aac_low","-aac_pns","0","-b:a","40k","-flush_packets","1","-f","adts","pipe:1"})start.ArgumentList.Add(arg);
            encoder=Process.Start(start)??throw new IOException("Could not start microphone encoder.");
            var errors=encoder.StandardError.ReadToEndAsync();
            await relay.SendAsync(0x17,3); started=true; log?.Invoke("Push to talk started (maximum 30 seconds per press).");
            forward=ForwardAdtsAsync(encoder.StandardOutput.BaseStream,relay,stop.Token);
            _=forward.ContinueWith(_=>stop.Cancel(),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            byte[] bytes=new byte[8192]; long total=0;var clock=Stopwatch.StartNew();
            while(socket.State==WebSocketState.Open)
            {
                var packet=await socket.ReceiveAsync(bytes.AsMemory(),stop.Token);
                if(packet.MessageType==WebSocketMessageType.Close)break;
                if(packet.MessageType!=WebSocketMessageType.Binary || packet.Count%2!=0)throw new IOException("Microphone expects 16-bit mono PCM.");
                total+=packet.Count;
                if(total>rate*2*(clock.Elapsed.TotalSeconds+2))throw new IOException("Microphone data arrived too quickly.");
                await encoder.StandardInput.BaseStream.WriteAsync(bytes.AsMemory(0,packet.Count),stop.Token);
            }
        }
        catch(OperationCanceledException) { }
        catch(WebSocketException) { }
        finally
        {
            stop.Cancel();
            if(encoder!=null){ if(!encoder.HasExited)encoder.Kill(true); if(forward!=null)try{await forward;}catch(Exception){} encoder.Dispose(); }
            try{if(started)await relay.SendAsync(0x17,4);}catch(Exception){}
            relay.ReleaseTalk();log?.Invoke("Push to talk stopped; microphone channel released.");
            socket.Abort();
        }
    }
}
