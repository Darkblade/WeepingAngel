using Blink.Models;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DontBlink.Services;

public sealed class LiveViewService(ClipService clips)
{
    public event Action<string>? Log;
    public event Action<string,int>? Diagnostic;
    public event Action<LiveProtocolFrame>? ProtocolObserved;
    internal Func<Uri, CancellationToken, Task<Stream>>? RelayConnector { get; set; }
    public Func<RelayCertificate, CancellationToken, Task<bool>>? ConfirmRelayCertificate { get; set; }
    private readonly RelayTrustStore _relayTrust = new();
    private readonly ConcurrentDictionary<(int,int), string> _lastErrors = new();
    private readonly ConcurrentDictionary<(int,int), LiveCapture> _captures = new();
    private readonly ConcurrentDictionary<string, ((int,int) Camera, RelayWriter Relay)> _audio = new();
    internal async Task TalkAsync(int network, int camera, string session, System.Net.WebSockets.WebSocket socket, int rate, CancellationToken ct)
    {
        if (!_audio.TryGetValue(session,out var target) || target.Camera != (network,camera)) throw new IOException("This microphone does not own the live session. Restart live view.");
        await RelayAudio.BridgeAsync(socket,target.Relay,Math.Clamp(rate,8000,96000),Log,ct);
    }
    private readonly ConcurrentDictionary<(int,int), LiveCaptureStatus> _finishedCaptures = new();
    public LiveCaptureStatus CaptureStatus(int network, int camera) => _captures.TryGetValue((network,camera), out var capture) ? capture.Status : _finishedCaptures.GetValueOrDefault((network,camera)) ?? new(false,null,null);
    public bool CanRecord(int network,int camera) => _captures.ContainsKey((network,camera));
    public async Task<LiveCaptureStatus> SetRecordingAsync(int network,int camera,bool enabled)
    {
        if (!_captures.TryGetValue((network,camera), out var capture)) throw new IOException("Live view is not ready. Start live viewing first.");
        await capture.SetRecordingAsync(enabled); return capture.Status;
    }
    public string? LastError(int network, int camera) => _lastErrors.GetValueOrDefault((network,camera));
    private readonly ConcurrentDictionary<(int, int), byte> _active = new();
    private readonly SemaphoreSlim _slots = new(2);
    public bool IsActive(int network, int camera) => _active.ContainsKey((network, camera));
    public static string FindFfmpeg()
    {
        string? configured = Environment.GetEnvironmentVariable("DONTBLINK_FFMPEG");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tools", "ffmpeg", "bin", "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new IOException("FFmpeg is missing. Set DONTBLINK_FFMPEG to the ffmpeg.exe path.");
    }

    // IMMIS framing follows blinkpy's documented interoperability implementation.
    public static byte[] BuildHandshake(BlinkLiveSession session)
    {
        var uri = new Uri(session.Server);
        var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        if (!query.TryGetValue("client_id", out var client) || !uint.TryParse(client, out uint id)) throw new IOException("Live relay did not provide a valid client ID.");
        using var data = new MemoryStream();
        void Number(uint number) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, number); data.Write(bytes); }
        void Field(string value, int width)
        { Number((uint)width); byte[] bytes = new byte[width]; var valueBytes = Encoding.UTF8.GetBytes(value); valueBytes.AsSpan(0, Math.Min(width, valueBytes.Length)).CopyTo(bytes); data.Write(bytes); }
        Number(0x28); Field(session.Serial, 16); Number(id); data.Write([1, 8]); Field("", 64);
        Field(uri.AbsolutePath.Split('/').Last().Split("__")[0], 16); Number(1);
        return data.ToArray();
    }

    public async Task StreamAsync(int network, int camera, bool record, Stream output, CancellationToken clientCancellation, string? talkSession = null)
    {
        string executable = FindFfmpeg();
        if (!await _slots.WaitAsync(0, clientCancellation)) throw new IOException("Two live sessions are already active. Stop one before opening another.");
        if (!_active.TryAdd((network, camera), 0)) { _slots.Release(); throw new IOException("This camera already has an active live session."); }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(clientCancellation);
        stop.CancelAfter(TimeSpan.FromMinutes(5));
        BlinkLiveSession? session = null;
        IDisposable? networkReservation = null;
        LiveCapture? capture = null;
        string cameraName = $"Camera {camera}";
        var protocol=new LiveProtocolDiagnostics(network,camera,frame=>ProtocolObserved?.Invoke(frame),(message,level)=>Diagnostic?.Invoke(message,level));
        try
        {
            networkReservation = await clips.ReserveLiveNetworkAsync(network, stop.Token);
            _lastErrors.TryRemove((network,camera), out _);
            var info = (await clips.CamerasAsync(stop.Token)).SingleOrDefault(c => c.Id == camera && c.NetworkId == network)
                ?? throw new IOException("Camera no longer exists on this account.");
            cameraName = info.Name;
            capture = new LiveCapture(clips, cameraName, message => Log?.Invoke(message));
            _captures[(network,camera)] = capture;
            _finishedCaptures.TryRemove((network,camera), out _);
            if (record) await capture.SetRecordingAsync(true);
            session = await clips.CameraOperationAsync(source => source.OpenLiveAsync(network, camera), stop.Token);
            var target = new Uri(session.Server);
            if (target.Scheme != "immis" || target.Port <= 0) throw new IOException("This camera returned an unsupported live relay transport.");
            await using var tls = await (RelayConnector ?? ConnectRelayAsync)(target, stop.Token);
            Log?.Invoke($"Live camera {camera}: TLS connected; sending relay authentication.");
            await tls.WriteAsync(BuildHandshake(session), stop.Token);
            var writer = new RelayWriter(tls,stop.Token,protocol.Observe);
            await writer.SendAsync(10,0);
            if (talkSession?.Length is >=32 and <=64 && !_audio.TryAdd(talkSession,((network,camera),writer))) throw new IOException("Live session identifier already in use.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-fflags", "+genpts", "-probesize", "1000000", "-analyzeduration", "1000000", "-f", "mpegts", "-i", "pipe:0", "-map", "0:v:0", "-map", "0:a:0?", "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-profile:v", "baseline", "-level:v", "4.0", "-pix_fmt", "yuv420p", "-r", "15", "-g", "15", "-keyint_min", "15", "-sc_threshold", "0", "-c:a", "aac", "-f", "mp4", "-movflags", "frag_keyframe+empty_moov+default_base_moof", "-frag_duration", "1000000", "-flush_packets", "1", "pipe:1" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the live video converter.");
            var errors = ReadConverterErrorsAsync(process.StandardError, camera);
            var feed = FeedAsync(tls, process.StandardInput.BaseStream, stop.Token, writer, protocol);
            var maintain = MaintainAsync(writer, stop);
            var poll = PollAsync(network, session, stop);
            var deliver = DeliverAsync(process.StandardOutput.BaseStream, output, capture, stop, clientCancellation, camera);
            try
            {
                await Task.WhenAny(feed, deliver);
                stop.Cancel();
                try { await feed; } catch (OperationCanceledException) { }
                await deliver.WaitAsync(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                if (process.ExitCode != 0 && !clientCancellation.IsCancellationRequested)
                    throw new IOException("Live video conversion failed: " + (await errors)[..Math.Min((await errors).Length, 500)]);
            }
            finally
            {
                stop.Cancel();
                if (!process.HasExited) process.Kill(true);
                try { await Task.WhenAll(maintain, poll); } catch (OperationCanceledException) { }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _lastErrors[(network,camera)] = ex.Message; Log?.Invoke($"Live camera {camera}: {ex.Message}"); throw; }
        finally
        {
            stop.Cancel();
            if (talkSession != null && _audio.TryGetValue(talkSession,out var owned) && owned.Camera == (network,camera)) _audio.TryRemove(talkSession,out _);
            try
            {
                if (capture != null)
                {
                    try { await capture.SetRecordingAsync(false); }
                    catch (Exception ex) { Log?.Invoke($"Live recording finalization failed: {ex.Message}"); }
                    _finishedCaptures[(network,camera)] = capture.Status;
                    _captures.TryRemove((network,camera), out _);
                }
            }
            finally
            {
                if (session != null)
                {
                    try { await clips.CameraOperationAsync(async source => { await source.EndLiveAsync(network, session.CommandId); return true; }, CancellationToken.None); }
                    catch (Exception ex) { Log?.Invoke($"Live cleanup for camera {camera}: {ex.Message}"); }
                }
                Diagnostic?.Invoke(protocol.Summary(),2);
                networkReservation?.Dispose();
                _active.TryRemove((network, camera), out _); _slots.Release();
            }
        }
    }
    internal async Task<Stream> ConnectRelayAsync(Uri target, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(target.Host, target.Port, ct);
            var stream = new SslStream(tcp.GetStream(), false, (_, certificate, _, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)) return false;
                using var cert = new X509Certificate2(certificate);
                if (VendorRelayTrust.IsKnownCertificate(cert)) return true;
                var details = new RelayCertificate(target.Host, cert.GetCertHashString(HashAlgorithmName.SHA256), cert.Subject, cert.Issuer,
                    cert.NotBefore.ToUniversalTime(), cert.NotAfter.ToUniversalTime(), errors.ToString());
                if (_relayTrust.IsTrusted(details)) return true;
                if (details.ValidFromUtc > DateTime.UtcNow || details.ValidToUtc <= DateTime.UtcNow || ConfirmRelayCertificate == null) return false;
                Log?.Invoke($"Live relay certificate needs approval in the Windows app. Host: {details.Host}; SHA-256: {details.Fingerprint}; {errors}");
                try
                {
                    if (!ConfirmRelayCertificate(details, ct).GetAwaiter().GetResult()) return false;
                    _relayTrust.Approve(details); return true;
                }
                catch (Exception) { return false; }
            });
            try { await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = VendorRelayTrust.TlsIdentity(target.Host) }, ct); return stream; }
            catch { await stream.DisposeAsync(); throw; }
        }
        catch { tcp.Dispose(); throw; }
    }
    private async Task FeedAsync(Stream relay, Stream input, CancellationToken ct, RelayWriter writer, LiveProtocolDiagnostics protocol)
    {
        bool authenticated = false, firstVideo = true;
        try
        {
            byte[] header = new byte[9];
            while (true)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                byte[] payload;
                uint length;
                try
                {
                    await relay.ReadExactlyAsync(header, deadline.Token);
                    length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5));
                    if (length > 1024 * 1024) throw new IOException("Invalid live relay packet length.");
                    payload = new byte[(int)length];
                    if (length > 0) await relay.ReadExactlyAsync(payload, deadline.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new IOException("Live relay stalled for 25 seconds while waiting for authentication or video."); }
                protocol.Observe("received",header[0],BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1)),(int)length);
                if (header[0] == 6 && length == 0)
                { authenticated = true; writer.Ready = true; Log?.Invoke("Live relay authentication accepted."); continue; }
                if (header[0] != 0 || length == 0) continue;
                if (!authenticated) throw new IOException("Live relay sent media before authentication was accepted.");
                if (payload[0] != 0x47) throw new IOException("Unsupported live relay video framing.");
                if (firstVideo) { Log?.Invoke("Live relay video received; converting for browser playback."); firstVideo = false; }
                await input.WriteAsync(payload, ct);
            }
        }
        catch (EndOfStreamException) { Log?.Invoke(authenticated ? "Live relay connection ended." : "Live relay closed before authentication was accepted."); }
        finally { await input.DisposeAsync(); }
    }
    private static async Task MaintainAsync(RelayWriter relay, CancellationTokenSource stop)
    {
        uint sequence = 0;
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stop.Token);
                await relay.SendAsync(10, ++sequence);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { stop.Cancel(); }
    }
    private async Task PollAsync(int network, BlinkLiveSession session, CancellationTokenSource stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(session.PollingSeconds), stop.Token);
                if (!await clips.CameraOperationAsync(source => source.PollLiveAsync(network, session.CommandId), stop.Token)) { stop.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke($"Live command polling failed: {ex.Message}"); stop.Cancel(); }
    }
    private async Task<string> ReadConverterErrorsAsync(StreamReader reader, int camera)
    {
        var messages = new Queue<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            string bounded = line[..Math.Min(line.Length, 500)];
            messages.Enqueue(bounded); while (messages.Count > 8) messages.Dequeue();
            Log?.Invoke($"Live converter camera {camera}: {bounded}");
        }
        return string.Join(" ", messages);
    }
    private async Task DeliverAsync(Stream input, Stream output, LiveCapture capture, CancellationTokenSource stop, CancellationToken ct, int camera)
    {
        byte[] buffer = new byte[81920]; bool connected = true; long converted = 0, sent = 0;
        try
        {
        while (true)
        {
            int count = await input.ReadAsync(buffer);
            if (count == 0) return;
            if (converted == 0) Log?.Invoke($"Live camera {camera}: converter produced first {count} MP4 bytes.");
            converted += count;
            await capture.WriteAsync(buffer.AsMemory(0, count));
            if (connected)
            {
                try
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), ct); await output.FlushAsync(ct);
                    if (sent == 0) Log?.Invoke($"Live camera {camera}: MP4 bytes are being delivered to the browser.");
                    sent += count;
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { connected = false; stop.Cancel(); }
            }
        }
        }
        finally { Log?.Invoke($"Live camera {camera}: converter output {converted} bytes; browser received {sent} bytes; client disconnected: {!connected || ct.IsCancellationRequested}."); }
    }
}
