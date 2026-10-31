using DontBlink.Services;
using System.Diagnostics;
using System.Net.WebSockets;

internal static class MicrophoneChecks
{
    public static async Task RunAsync()
    {
        using var output=new AudioOutput();var relay=new RelayWriter(output,CancellationToken.None) {Ready=true};
        using var socket=new Microphone(output);
        await RelayAudio.BridgeAsync(socket,relay,48000,null,CancellationToken.None);
        if(!output.Audio || !output.Stopped || !relay.TryTalk())throw new Exception("Microphone bridge did not transmit and release its relay.");
        relay.ReleaseTalk();Console.WriteLine("PASS microphone PCM is encoded and transmitted before input EOF; release sends stop and frees channel");
    }
    private sealed class AudioOutput:MemoryStream
    {
        public volatile bool Audio,Stopped;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default)
        {
            if(buffer.Span[0]==5)Audio=true;
            if(buffer.Span[0]==0x17 && buffer.Span[4]==4)Stopped=true;
            return base.WriteAsync(buffer,ct);
        }
    }
    private sealed class Microphone(AudioOutput output):WebSocket
    {
        private readonly Stopwatch _clock=Stopwatch.StartNew();
        public override WebSocketCloseStatus? CloseStatus=>null;
        public override string? CloseStatusDescription=>null;
        public override string? SubProtocol=>null;
        public override WebSocketState State=>WebSocketState.Open;
        public override void Abort(){}
        public override void Dispose(){}
        public override Task CloseAsync(WebSocketCloseStatus status,string? description,CancellationToken ct)=>Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status,string? description,CancellationToken ct)=>Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> bytes,WebSocketMessageType type,bool end,CancellationToken ct)=>Task.CompletedTask;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> bytes,CancellationToken ct)
        {
            if(output.Audio)return new(0,WebSocketMessageType.Close,true);
            if(_clock.Elapsed>TimeSpan.FromSeconds(3))throw new IOException("Microphone encoder buffered three seconds without transmitting.");
            await Task.Delay(43,ct);Array.Clear(bytes.Array!,bytes.Offset,4096);return new(4096,WebSocketMessageType.Binary,true);
        }
    }
}
