using Blink.Models;
using DontBlink.Services;
using System.Buffers.Binary;
using System.Diagnostics;

internal static class LiveChecks
{
    public static async Task RunAsync(string root)
    {
        string folder = Path.Combine(root, "live-checks"); Directory.CreateDirectory(folder);
        using (var trustResource = typeof(VendorRelayTrust).Assembly.GetManifestResourceStream("DontBlink.Security.immis_ca.pem")!)
        using (var trustReader = new StreamReader(trustResource))
        {
            var certificates = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
            certificates.ImportFromPem(await trustReader.ReadToEndAsync());
            Require(certificates.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>().Any(c => c.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) == "B1BFA71BA445F0DE14172A1382DB19E8848F93B850EB78BD13FDABC1BE7E2481" && VendorRelayTrust.IsKnownCertificate(c)), "published vendor trust bundle matches reported live certificate");
            Require(VendorRelayTrust.TlsIdentity("63.176.98.245") == "*.immedia-semi.com" && VendorRelayTrust.TlsIdentity("relay.immedia-semi.com") == "relay.immedia-semi.com", "IP relays use vendor TLS identity while DNS relays retain hostname");
            foreach (var certificate in certificates) certificate.Dispose();
        }
        var commandHandler = new CommandHandler();
        using (var commandHttp = new HttpClient(commandHandler) { BaseAddress = new Uri("https://test.invalid") })
        {
            var client = new Blink.BlinkClient();
            void Field(string name, object value) => typeof(Blink.BlinkClient).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(client, value);
            Field("_accountId", 123); Field("_tier", "test"); Field("_http", commandHttp);
            Field("_lastLoginResult", new Blink.Models.LoginResult { AccessToken = "synthetic", ValidUntil = DateTime.UtcNow.AddHours(1) });
            Require(await client.IsLiveCommandRunningAsync(2,7) && !await client.IsLiveCommandRunningAsync(2,7), "modern live command endpoint handles running and completed states");
        }
        string input = Path.Combine(folder, "sample.ts");
        await FfmpegAsync("-y", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=15", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=16000", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-f", "mpegts", input);
        byte[] transport = await File.ReadAllBytesAsync(input);
        await MicrophoneChecks.RunAsync();
        string microphone=Path.Combine(folder,"microphone.aac");
        await FfmpegAsync("-f","lavfi","-i","sine=frequency=600:sample_rate=16000","-t","0.25","-c:a","aac","-aac_pns","0","-f","adts",microphone);
        using(var packets=new MemoryStream())
        {
            var writer=new RelayWriter(packets,CancellationToken.None);
            await writer.SendAsync(0x17,3);
            using var audio=File.OpenRead(microphone); await RelayAudio.ForwardAdtsAsync(audio,writer,CancellationToken.None);
            await writer.SendAsync(0x17,4);
            byte[] data=packets.ToArray(); int offset=9,frames=0;
            Require(data[0]==0x17 && BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1))==3,"push-to-talk emits start command on existing relay");
            while(data[offset]==5)
            {
                Require(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset+1))==frames*5760,"microphone AAC timestamps use the 90kHz clock");
                int size=(int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset+5));
                Require(size>7 && data[offset+9]==255 && (data[offset+10]&0xF0)==0xF0,"microphone packets retain ADTS framing");
                offset+=9+size; frames++;
            }
            Require(frames>2 && offset==data.Length-9 && data[offset]==0x17 && BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset+1))==4,"talk release emits stop command after audio frames");
        }
        string tlsFolder=Path.Combine(folder,"https");
        using(var certificate=PhoneHttps.CreateServerCertificate(tlsFolder))
        using(var again=PhoneHttps.CreateServerCertificate(tlsFolder))
        using(var ca=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(Path.Combine(tlsFolder,"DontBlink-phone-CA.cer")))
        using(var chain=new System.Security.Cryptography.X509Certificates.X509Chain())
        {
            chain.ChainPolicy.TrustMode=System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca); chain.ChainPolicy.RevocationMode=System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
            Require(certificate.HasPrivateKey && chain.Build(certificate) && chain.Build(again),"phone HTTPS certificates use a persistent private CA and valid server chain");
            Require(certificate.Extensions.OfType<System.Security.Cryptography.X509Certificates.X509SubjectAlternativeNameExtension>().Single().EnumerateIPAddresses().Contains(System.Net.IPAddress.Loopback),"phone certificate includes current address subject alternatives");
        }
        byte[] packet = new byte[18 + transport.Length]; packet[0] = 6; // Authentication accepted.
        packet[9] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(14), (uint)transport.Length); transport.CopyTo(packet,18);
        var replay = new ReplayRelay(packet);
        var source = new LiveSource();
        var clips = new ClipService(source, Path.Combine(folder, "archive"));
        var live = new LiveViewService(clips) { RelayConnector = (_,_) => Task.FromResult<Stream>(replay) };
        var protocolFrames=new List<LiveProtocolFrame>();live.ProtocolObserved+=protocolFrames.Add;
        using var browserOutput = new MemoryStream();
        await live.StreamAsync(2,3,true,browserOutput,CancellationToken.None);
        Require(protocolFrames.Any(f=>f.Direction=="received"&&f.Type==6)&&protocolFrames.Any(f=>f.Direction=="sent"&&f.Type==10)&&protocolFrames.All(f=>f.Type!=0),"existing live sessions expose decoded metadata without media or auth bytes");
        Require(replay.Written.Length >= 122 && BinaryPrimitives.ReadUInt32BigEndian(replay.Written) == 0x28, "IMMIS authentication header emitted");
        Require(replay.Written.AsSpan(122,9).SequenceEqual(new byte[]{10,0,0,0,0,0,0,0,0}), "initial IMMIS keepalive uses sequence zero");
        Require(browserOutput.Length > 1000 && source.Ended && !live.IsActive(2,3), "live relay remuxes video and tears down session");
        var item = clips.Archive().Single();
        Require(item.FileName.EndsWith(" - Garden.mp4") && item.Sha256 != null, "live recording saved using archive filename format and checksum");
        await FfmpegAsync("-i", clips.ArchivePath(item.Key)!, "-f", "null", "-");
        Console.WriteLine("PASS saved live recording decodes with video and audio");
        var liveBytes = browserOutput.ToArray();
        string overlayFile=Path.Combine(folder,"overlaid.mp4"); await File.WriteAllBytesAsync(overlayFile,liveBytes);
        var overlay=new RecordingOverlay(Enabled:true,Watermark:"Test {\\watermark}",Size:36,Format:"Date and time with seconds");
        string subtitles=overlay.Subtitles("Garden",new DateTime(2026,10,28,12,30,0,DateTimeKind.Utc));
        Require(subtitles.Contains("2026.10.28") && !subtitles.Contains("{\\watermark}"),"overlay timestamp uses dotted dates and escapes subtitle controls");
        await overlay.ApplyAsync(overlayFile,"Garden",DateTime.UtcNow);
        await FfmpegAsync("-i",overlayFile,"-f","null","-");
        var overlaidBytes = await File.ReadAllBytesAsync(overlayFile);
        Require(new FileInfo(overlayFile).Length>1000 && !liveBytes.SequenceEqual(overlaidBytes),"recording overlays produce a new decodable MP4");
        int avcc = System.Text.Encoding.Latin1.GetString(liveBytes).IndexOf("avcC",StringComparison.Ordinal);
        Require(avcc >= 0 && liveBytes[avcc+5] == 66, "live video is encoded as mobile-compatible H.264 Baseline");
        var boxes = new List<byte[]>();
        for(int offset=0;offset+8<=liveBytes.Length;)
        {
            int length=(int)BinaryPrimitives.ReadUInt32BigEndian(liveBytes.AsSpan(offset));
            Require(length>=8 && offset+length<=liveBytes.Length,"live MP4 box boundary");
            boxes.Add(liveBytes.AsSpan(offset,length).ToArray()); offset+=length;
        }
        var midClips = new ClipService(new LiveSource(),Path.Combine(folder,"mid-recording"));
        var capture = new LiveCapture(midClips,"Garden",null);
        int fragments=0;
        foreach(var box in boxes)
        {
            string type=System.Text.Encoding.ASCII.GetString(box,4,4);
            if(type=="moof")
            {
                fragments++;
                if(fragments==2)await capture.SetRecordingAsync(true);
            }
            await capture.WriteAsync(box);
        }
        Require(fragments>=2,"live stream contains periodic recording boundaries");
        byte[] movie = boxes.Single(box => System.Text.Encoding.ASCII.GetString(box,4,4) == "moov");
        Require(System.Text.Encoding.Latin1.GetString(movie).Split("soun").Length-1 == 1, "two-audio-track relay is reduced to one browser audio track");
        await capture.SetRecordingAsync(false);
        Require(!capture.Status.Recording && capture.Status.SavedFile!=null && midClips.Archive().Count==1,"recording can start and stop during an existing live view");
        string frame=Path.Combine(folder,"mid-frame.png");
        await FfmpegAsync("-y","-i",midClips.ArchivePath(midClips.Archive().Single().Key)!,"-an","-frames:v","1",frame);
        Require(File.Exists(frame) && new FileInfo(frame).Length>1000,"mid-session recording starts with a decodable video frame");
        var stoppedSource = new LiveSource();
        var stoppedClips = new ClipService(stoppedSource, Path.Combine(folder, "stopped-archive"));
        var stoppedLive = new LiveViewService(stoppedClips) { RelayConnector = (_,_) => Task.FromResult<Stream>(new ReplayRelay(packet)) };
        using (var cancellation = new CancellationTokenSource())
        using (var stoppedOutput = new StopOnWrite(cancellation))
            await stoppedLive.StreamAsync(2,3,true,stoppedOutput,cancellation.Token);
        Require(stoppedSource.Ended && stoppedClips.Archive().Count == 1, "stopping live playback finalizes recording and releases camera");
        var ongoingSource = new LiveSource();
        var ongoingClips = new ClipService(ongoingSource, Path.Combine(folder, "ongoing-archive"));
        var ongoingLive = new LiveViewService(ongoingClips) { RelayConnector = (_,_) => Task.FromResult<Stream>(new ReplayRelay(packet, true)) };
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
        using (var firstOutput = new StopOnWrite(cancellation))
        {
            var elapsed = Stopwatch.StartNew();
            await ongoingLive.StreamAsync(2,3,false,firstOutput,cancellation.Token);
            Require(elapsed.Elapsed < TimeSpan.FromSeconds(8), "live output starts without waiting for relay EOF");
        }
        var images = new CameraMediaService(clips);
        await images.ImageAsync(2,3,false,default); await images.ImageAsync(2,3,false,default);
        await images.ImageAsync(2,3,true,default); await images.ImageAsync(2,3,true,default);
        Require(source.ImageCalls == 3 && source.Captures == 2, "camera image caching and explicit fresh capture throttling");
    }
    private static async Task FfmpegAsync(params string[] args)
    {
        var start = new ProcessStartInfo(LiveViewService.FindFfmpeg()) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
        start.ArgumentList.Add("-hide_banner"); start.ArgumentList.Add("-loglevel"); start.ArgumentList.Add("error");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Require(process.ExitCode == 0, "FFmpeg integration: " + (await error)); await output;
    }
    private static void Require(bool condition,string name) { if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name); }
    private sealed class LiveSource : IClipSource
    {
        public bool Ended; public int ImageCalls,Captures;
        public Task<CameraOverview[]> CamerasAsync() => Task.FromResult(new[]{new CameraOverview(3,2,"Garden","Outdoor 3","Unknown","ok","Enabled")});
        public Task<BlinkLiveSession> OpenLiveAsync(int network,int camera) => Task.FromResult(new BlinkLiveSession {Server="immis://test.invalid:443/conn123__session?client_id=42",CommandId=7,Serial="TESTSERIAL",PollingSeconds=60});
        public Task EndLiveAsync(int network,long command){Ended=true;return Task.CompletedTask;}
        public Task<byte[]> CameraImageAsync(int network,int camera,bool capture){ImageCalls++;if(capture)Captures++;return Task.FromResult(new byte[]{255,216,255,217});}
        public Task<SyncModule[]> ModulesAsync()=>Task.FromResult(Array.Empty<SyncModule>());
        public Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module)=>Task.FromResult<IEnumerable<BlinkVideoInfo>>([]);
        public Task<byte[]> DownloadAsync(BlinkVideoInfo video)=>throw new NotSupportedException();
        public Task DeleteAsync(BlinkVideoInfo video)=>throw new NotSupportedException();
    }
    private sealed class CommandHandler : HttpMessageHandler
    {
        private int _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/accounts/123/networks/2/commands/7", "live polling uses account-scoped commands endpoint");
            string json = ++_calls == 1 ? "{\"status\":\"running\",\"complete\":false}" : "{\"status\":\"complete\",\"complete\":true}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private sealed class ReplayRelay(byte[] packets, bool holdOpen = false) : Stream
    {
        private readonly MemoryStream _read = new(packets); private readonly MemoryStream _written = new();
        public byte[] Written=>_written.ToArray();
        public override bool CanRead=>true;public override bool CanWrite=>true;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int n)=>_read.Read(b,o,n);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        { if(holdOpen && _read.Position==_read.Length)await Task.Delay(Timeout.Infinite,ct);return await _read.ReadAsync(buffer,ct); }
        public override void Write(byte[] b,int o,int n)=>_written.Write(b,o,n);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default){_written.Write(buffer.Span);return ValueTask.CompletedTask;}
        public override void Flush(){} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
    }
    private sealed class StopOnWrite(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { cancellation.Cancel(); return ValueTask.FromCanceled(cancellation.Token); }
    }
}
