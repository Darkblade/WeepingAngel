using Blink.Models;
using DontBlink.Services;
using System.Net;
using System.Net.Http.Json;

internal static class Checks
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Path.GetTempPath(), "DontBlink-checks-" + Guid.NewGuid());
        try
        {
            if(args.Length==2&&args[0]=="--capture-replay-check"){DeviceRegistryChecks.ReplayFile(args[1],root);return 0;}
            if (args.Length == 1 && args[0] == "--console-layout-check") return ConsoleLayoutChecks.Run();
            if (args.Length == 1 && args[0] == "--grid-interaction-check") return GridInteractionChecks.Run();
            if (args.Length == 1 && args[0] == "--device-table-check") return DeviceTableLinkChecks.Run();
            if (args.Length == 1 && args[0] == "--camera-view-check") return CameraViewChecks.Run();
            if (args.Length == 1 && args[0] == "--web-ui-check") return BrowserUiChecks.Run();
            if (args.Length == 2 && args[0] == "--browser-check") return BrowserPlaybackChecks.Run(args[1]);
            if (args.Length == 2 && args[0] == "--relay-tls")
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var relay = new LiveViewService(new ClipService(new FakeSource(), root));
                using var connection = relay.ConnectRelayAsync(new Uri($"immis://{args[1]}:443"), deadline.Token).GetAwaiter().GetResult();
                Check(connection.CanRead, "live relay TLS handshake with published Blink certificate and vendor identity");
                return 0;
            }
            // Construct the actual Windows form without showing or signing into Blink.
            using (var form = new DontBlink.Form1())
                Check(form.Controls.Count == 1, "Windows form construction");
            TableSortingChecks.Run();CameraLayoutChecks.Run();
            SynchronizationContext.SetSynchronizationContext(null);
            RunAsync(root).GetAwaiter().GetResult();
            Console.WriteLine("All DontBlink checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        LiveProtocolChecks.Run();
        await CameraVideoChecks.RunAsync(root);
        await TestSessionLogChecks.RunAsync(root);
        await RouterMonitoringChecks.RunAsync(root);
        await LiveActivityChecks.RunAsync(root);
        await PrioritySchedulingChecks.RunAsync(root);
        await OfflineChecks.RunAsync(root);
        await DeviceRegistryChecks.RunAsync(root);
        await DeviceConfigurationChecks.RunAsync(root);
        string safeLog = DebugLogFormatter.Format(["Bearer test-bearer-secret", "{\"access_token\":\"test-token-secret\",\"password\":\"test-password-secret\"}", "Blink code 307: System is busy"]);
        Check(!safeLog.Contains("test-bearer-secret") && !safeLog.Contains("test-token-secret") && !safeLog.Contains("test-password-secret") && safeLog.Contains("code 307"), "clipboard debug log redacts credentials and preserves diagnostics");
        var pinStore = new RelayTrustStore(Path.Combine(root, "relay-pins.json"));
        var certificate = new RelayCertificate("relay.example", "AABBCC", "CN=relay", "CN=test", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(2), "RemoteCertificateChainErrors");
        Check(!pinStore.IsTrusted(certificate), "unapproved relay certificate is rejected");
        pinStore.Approve(certificate);
        Check(pinStore.IsTrusted(certificate) && !pinStore.IsTrusted(certificate with { Fingerprint = "DDEEFF" })
            && !pinStore.IsTrusted(certificate with { Host = "another.example" }) && !pinStore.IsTrusted(certificate with { ValidToUtc = DateTime.UtcNow.AddDays(-1) }), "relay pin is limited to the approved host, certificate and validity period");
        await LiveChecks.RunAsync(root);
        var filterDay = new DateTime(2026, 10, 28);
        ArchiveItem Sample(string camera, DateTime local) => new(new string('A',64), camera, DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(), "sample.mp4", 100);
        var filterSamples = new[] { Sample("Garden", filterDay.AddHours(1)), Sample("Drive", filterDay.AddHours(23)), Sample("Garden", filterDay.AddDays(1)) };
        Check(ArchiveFilter.Apply(filterSamples, "Garden", filterDay).Count == 1, "camera and exact local day filters combine");
        Check(ArchiveFilter.Apply(filterSamples, from: filterDay, to: filterDay).Count == 2, "date range includes the whole end day");
        Check(ArchiveFilter.Apply(filterSamples, from: filterDay.AddDays(1)).Count == 1, "open-ended date range");
        try { ArchiveFilter.Apply(filterSamples, from: filterDay.AddDays(1), to: filterDay); throw new Exception("Invalid range accepted"); }
        catch (ArgumentException) { Check(true, "reversed date range rejected"); }
        string settingsPath = Path.Combine(root, "settings.json");
        new AppSettings(Path.Combine(root, "preferred"), "Ocean").Save(settingsPath);
        Check(AppSettings.Load(settingsPath) == new AppSettings(Path.Combine(root, "preferred"), "Ocean"), "archive location and theme survive restart");
        string clientPath = Path.Combine(root, "clients.json");
        var access = new ClientAccessStore(clientPath);
        string trusted = access.Pair("S21", "192.168.50.20", true);
        string temporary = access.Pair("Workstation", "192.168.50.21", false);
        var restored = new ClientAccessStore(clientPath);
        Check(restored.Authenticate(trusted, "192.168.50.22")?.Name == "S21", "trusted browser survives restart and DHCP change");
        Check(restored.Authenticate(temporary, "192.168.50.21") == null, "untrusted browser does not survive restart");
        Check(!File.ReadAllText(clientPath).Contains(trusted), "trusted browser tokens are stored as hashes");
        var identity = restored.Clients().Single().Id;
        restored.Revoke(identity);
        Check(new ClientAccessStore(clientPath).Authenticate(trusted, "192.168.50.22") == null, "revocation persists across restart");
        var transientId = access.Authenticate(temporary, "192.168.50.21")!.Id;
        access.SetTrusted(transientId, true);
        Check(new ClientAccessStore(clientPath).Authenticate(temporary, "192.168.50.21")?.Trusted == true, "Windows can trust a paired browser");
        using (var cameraJson = System.Text.Json.JsonDocument.Parse("{\"id\":123,\"network_id\":2,\"name\":\"Garden\",\"battery\":\"ok\",\"enabled\":true}"))
        {
            var camera = CameraOverview.Parse(cameraJson.RootElement)!;
            Check(camera.Name == "Garden" && camera.Id == 123 && camera.Status == "Unknown" && camera.Motion == "Enabled", "camera overview preserves identity without inventing missing status");
        }
        string credentialFile = Path.Combine(root, "credentials.dat");
        var store = new CredentialStore(credentialFile);
        store.Save("test@example.invalid", "synthetic-password-for-check");
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(credentialFile)).Contains("synthetic-password-for-check"), "credentials encrypted at rest");
        Check(new CredentialStore(credentialFile).Load() == new CredentialStore.Credentials("test@example.invalid", "synthetic-password-for-check"), "credentials restored for Windows user");
        store.Delete();
        Check(store.Load() == null, "unchecking save removes credentials");
        foreach (var challenge in new[] { HttpStatusCode.Accepted, HttpStatusCode.PreconditionFailed })
        {
            var handler = new OAuthHandler(challenge);
            var client = new Blink.BlinkClient { OAuthHttpClientFactory = () => new HttpClient(handler) };
            Check(await client.TryLoginAsync("test@example.invalid", "test-password"), $"HTTP {(int)challenge} preserves SMS challenge");
            try
            {
                await client.TryVerifyPinAsync("123456");
                throw new Exception("Expected test to stop before real token exchange");
            }
            catch (Blink.Exceptions.BlinkClientException ex) when (ex.Message.Contains("missing authorization code"))
            { Check(handler.Verified, "SMS verified in the original OAuth session"); }
        }
        foreach(var status in new[]{HttpStatusCode.BadRequest,HttpStatusCode.Forbidden,HttpStatusCode.NotAcceptable,HttpStatusCode.TooManyRequests,HttpStatusCode.Unauthorized}) {
            var client=new Blink.BlinkClient{OAuthHttpClientFactory=()=>new HttpClient(new OAuthHandler(status))};
            try { await client.TryLoginAsync("test@example.invalid","test-password"); throw new Exception("Expected explicit login failure"); }
            catch(Blink.Exceptions.BlinkClientException ex) { Check(ex.Message.Contains("HTTP "+(int)status) && !ex.Message.Contains("test-password"),"OAuth rejection reports safe HTTP status "+(int)status); }
        }
        var clock=DateTimeOffset.UtcNow;
        string cooldownFile=Path.Combine(root,"login-cooldown.json");
        var cooldown=new LoginCooldown(cooldownFile,()=>clock);
        using(var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("{}")}) {
            response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(75));
            var limit=await Blink.Exceptions.BlinkLoginRateLimitException.FromResponseAsync(response);
            Check(limit.ServerSpecified && Math.Abs((limit.RetryAtUtc-DateTimeOffset.UtcNow).TotalSeconds-75)<2,"429 honors Retry-After seconds");
            cooldown.RateLimited(limit);
        }
        var displayCooldown=new LoginCooldown(Path.Combine(root,"display-cooldown.json"),()=>clock);
        displayCooldown.RateLimited(new Blink.Exceptions.BlinkLoginRateLimitException(clock.AddDays(2).AddHours(3).AddMinutes(4).AddSeconds(5),true));
        Check(displayCooldown.Display=="002:03:04:05","countdown displays DDD:HH:MM:SS");
        displayCooldown.Succeeded();Check(displayCooldown.Display=="000:00:00:00","expired countdown displays zero in every field");
        var restoredCooldown=new LoginCooldown(cooldownFile,()=>clock);
        Check(restoredCooldown.Remaining>TimeSpan.Zero,"login cooldown survives app restart");
        try {restoredCooldown.EnsureAllowed();throw new Exception("Expected early login to be blocked");}catch(InvalidOperationException){Check(true,"early login is blocked locally");}
        clock=clock.AddMinutes(2);restoredCooldown.EnsureAllowed();Check(restoredCooldown.Remaining==TimeSpan.Zero,"login unblocks after countdown expires without sending requests");
        using(var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("{\"next_time_in_secs\":120}")}) {
            response.Headers.Date=DateTimeOffset.UtcNow;
            response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(60));
            var limit=await Blink.Exceptions.BlinkLoginRateLimitException.FromResponseAsync(response);
            Check(limit.ServerSpecified && (limit.RetryAtUtc-DateTimeOffset.UtcNow).TotalSeconds>118,"longer server body retry timing takes precedence over date header");
        }
        using(var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("not json")}) {
            var limit=await Blink.Exceptions.BlinkLoginRateLimitException.FromResponseAsync(response);
            Check(!limit.ServerSpecified && (limit.RetryAtUtc-DateTimeOffset.UtcNow).TotalMinutes>9.9,"429 without timing uses labelled ten-minute precaution");
        }
        var failures=new LoginCooldown(Path.Combine(root,"failed-logins.json"),()=>clock);
        for(int i=0;i<3;i++)failures.Rejected();Check(failures.Remaining==TimeSpan.Zero,"first three credential failures do not trigger precaution");
        failures.Rejected();Check(failures.Remaining==TimeSpan.FromMinutes(10),"four rejected credentials trigger precaution before Blink's five-attempt threshold");
        failures.Succeeded();Check(failures.Remaining==TimeSpan.Zero,"successful authentication clears precaution");
        using(var diagnosticResponse=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("{\"next_time_in_secs\":86400,\"password\":\"synthetic-secret\",\"csrf-token\":\"synthetic-csrf\",\"error\":\"rate_limited\"}")}) {
            diagnosticResponse.Headers.TryAddWithoutValidation("Retry-After","86400");
            diagnosticResponse.Headers.TryAddWithoutValidation("Set-Cookie","synthetic-cookie-secret");
            string diagnostic=await Blink.OAuthDiagnostics.DescribeAsync(diagnosticResponse);
            Check(diagnostic.Contains("86400") && diagnostic.Contains("rate_limited") && !diagnostic.Contains("synthetic-"),"verbose OAuth diagnostics retain rate timing and redact secrets and cookies");
        }
        var sessionStore=new BlinkSessionStore(Path.Combine(root,"session.dat"));
        var sessionIdentity=sessionStore.LoadOrCreate();
        sessionStore.Save(sessionIdentity with{RefreshToken="synthetic-refresh-one",Email="test@example.invalid"});
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"session.dat"))).Contains("synthetic-refresh"),"saved session is encrypted for the Windows user");
        var refreshHandler=new RefreshHandler();
        BlinkAuthService Auth()=>new(sessionStore,()=>new Blink.BlinkClient{OAuthHttpClientFactory=()=>new HttpClient(refreshHandler,false)});
        var restoredAuth=Auth();
        Check(await restoredAuth.RestoreSessionAsync(),"saved authorization restores without password or SMS");
        Check(sessionStore.LoadOrCreate().RefreshToken=="synthetic-refresh-two" && refreshHandler.Hardware==sessionIdentity.HardwareId,"rotated token is saved with stable hardware identity");
        Check(await Auth().RestoreSessionAsync(),"rotated authorization survives another app launch");
        refreshHandler.Fail=true;
        try {await Auth().RestoreSessionAsync();throw new Exception("Expected temporary refresh failure");}catch(Blink.Exceptions.BlinkClientException){Check(sessionStore.LoadOrCreate().RefreshToken=="synthetic-refresh-two","temporary refresh failure retains saved token without SMS fallback");}
        refreshHandler.Fail=false;refreshHandler.Revoked=true;
        Check(!await Auth().RestoreSessionAsync() && sessionStore.LoadOrCreate().RefreshToken==null,"revoked authorization requires manual login and removes invalid token");
        Check(sessionStore.LoadOrCreate().HardwareId==sessionIdentity.HardwareId,"revoked token does not reset device identity");
        var source = new FakeSource();
        var orderedSource = new FakeSource();
        var orderedService = new ClipService(orderedSource, Path.Combine(root, "ordered-archive"));
        var orderedKey = (await orderedService.ListAsync(1, default)).Single().Key;
        var order = new List<string>();
        orderedSource.OnDownload = () => order.Add("copy"); orderedSource.OnDelete = () => order.Add("delete");
        using (var listener = orderedService.Notifications.Subscribe())
        {
            var operation = orderedService.BatchAsync(1, [orderedKey], false, default, removeAfterArchive: true);
            var starting = await listener.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Check(starting.Notice.Stage == "starting" && orderedSource.Downloads == 0 && orderedSource.Deletes == 0, "notification precedes copying and USB deletion");
            order.Add("notify"); starting.Sent.SetResult();
            var complete = await listener.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            order.Add("complete"); complete.Sent.SetResult();
            Check((await operation).Single().Success && string.Join(",", order) == "notify,copy,delete,complete", "notify-copy-verify-delete sequence enforced");
        }
        var blockedSource = new FakeSource();
        var blockedService = new ClipService(blockedSource, Path.Combine(root, "blocked-notice"));
        using (var listener = blockedService.Notifications.Subscribe())
        using (var stop = new CancellationTokenSource(100))
        {
            try { await blockedService.BatchAsync(1, [orderedKey], false, stop.Token, removeAfterArchive: true); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { Check(blockedSource.Downloads == 0 && blockedSource.Deletes == 0, "undelivered notification prevents copy and deletion"); }
        }
        var service = new ClipService(source, root); var observedProgress=new List<ArchiveProgress>(); service.ProgressChanged+=observedProgress.Add;
        using (await service.ReserveLiveNetworkAsync(2, default))
        {
            try { await service.ListAsync(1, default); throw new Exception("USB commands were not paused"); }
            catch (IOException ex) when (ex.Message.Contains("paused")) { Check(true, "live session pauses USB commands on the same Sync Module"); }
            try { await service.CameraOperationAsync(s => s.CameraImageAsync(2,123,true), default, snapshotNetwork: 2); throw new Exception("Snapshot was not blocked"); }
            catch (IOException ex) when (ex.Message.Contains("Sync Module")) { Check(true, "snapshot cannot overlap a live session on its Sync Module"); }
        }
        var first = (await service.ListAsync(1, default)).Single();
        var result = await service.BatchAsync(1, [first.Key], false, default);
        Check(service.Progress is {Total:1,Processed:1,Succeeded:1,Failed:0} && observedProgress.Any(p=>p.Stage=="Validating MP4") && service.Progress.CompletedBytes>0 && service.Progress.Summary.Contains("ETA"), "archive progress reports real counts, transfer stages, bytes and ETA");
        Check(DeviceFeatures.ThumbnailTime("unknown")==null && DeviceFeatures.Age(DateTimeOffset.UtcNow.AddHours(-2),DateTimeOffset.UtcNow)=="2h ago", "thumbnail age preserves unknown values and reports elapsed time");
        Check(result.Single().Success && source.Downloads == 1, "download with refreshed manifest ID");
        Check(service.ArchivePath(first.Key) != null, "archive committed");
        string readable = service.Archive().Single().FileName;
        Check(readable.StartsWith(new DateTime(2026,10,28,12,0,0,DateTimeKind.Utc).ToLocalTime().ToString("yyyy.MM.dd - HH-mm-ss") + " - ")
            && !readable.Contains(':') && !readable.Contains('/'), "readable Windows-safe archive filenames");
        var restarted = new ClipService(source, root);
        Check(restarted.Archive().Single().Key == first.Key, "archive survives restart");
        await restarted.BatchAsync(1, [first.Key], false, default);
        Check(source.Downloads == 1, "restart does not redownload complete archive");
        var moveSource = new FakeSource();
        var moves = new ClipService(moveSource, Path.Combine(root, "move-archive"));
        var moveKey = (await moves.ListAsync(1, default)).Single().Key;
        moveSource.FailDeletes = true;
        var moveResult = await moves.BatchAsync(1, [moveKey], false, default, removeAfterArchive: true);
        Check(!moveResult.Single().Success && moves.ArchivePath(moveKey) != null && moveSource.Deletes == 0, "delete failure preserves the completed archive");
        moveSource.FailDeletes = false;
        moveResult = await moves.BatchAsync(1, [new string('F',64), moveKey], false, default, removeAfterArchive: true);
        Check(moveResult.Count == 2 && !moveResult[0].Success && moveResult[1].Success && moveSource.Downloads == 1 && moveSource.Deletes == 1,
            "retry skips existing download and continues after another clip failure");
        byte[] tampered = await File.ReadAllBytesAsync(moves.ArchivePath(moveKey)!);
        tampered[^1] ^= 1;
        await File.WriteAllBytesAsync(moves.ArchivePath(moveKey)!, tampered);
        moveResult = await moves.BatchAsync(1, [moveKey], false, default, removeAfterArchive: true);
        Check(!moveResult.Single().Success && moveResult.Single().Message.Contains("checksum") && moveSource.Deletes == 1, "checksum mismatch keeps USB original");
        var failedSource = new FakeSource { InvalidSize = true };
        var failedMove = new ClipService(failedSource, Path.Combine(root, "failed-move"));
        moveResult = await failedMove.BatchAsync(1, [moveKey], false, default, removeAfterArchive: true);
        Check(!moveResult.Single().Success && failedSource.Deletes == 0, "failed download never deletes USB original");
        string changedRoot = Path.Combine(root, "new-archive");
        await restarted.ChangeArchiveRootAsync(changedRoot, default);
        Check(restarted.ArchiveRoot == changedRoot && restarted.Archive().Count == 0 && File.Exists(service.ArchivePath(first.Key)), "changing destination preserves existing recordings");
        await restarted.ChangeArchiveRootAsync(root, default);
        Check(restarted.Archive().Count == 1, "returning to archive restores its index");
        var collisionSource = new FakeSource();
        var collisionService = new ClipService(collisionSource, Path.Combine(root, "collisions"));
        var collisionKey = (await collisionService.ListAsync(1, default)).Single().Key;
        await collisionService.BatchAsync(1, [collisionKey], false, default);
        collisionSource.ReportedSize = "5";
        collisionKey = (await collisionService.ListAsync(1, default)).Single().Key;
        await collisionService.BatchAsync(1, [collisionKey], false, default);
        Check(collisionService.Archive().Count == 2 && collisionService.Archive().Select(x => x.FileName).Distinct().Count() == 2, "same-second camera filenames do not overwrite");
        Check(service.ArchivePath("../escape") == null, "reject path traversal");

        source.Missing = true;
        result = await service.BatchAsync(1, [first.Key], true, default);
        Check(!result.Single().Success && source.Deletes == 0, "missing clip never deleted");
        source.Missing = false; source.Duplicate = true;
        result = await service.BatchAsync(1, [first.Key], true, default);
        Check(!result.Single().Success && source.Deletes == 0, "ambiguous clip never deleted");
        source.Duplicate = false;
        result = await service.BatchAsync(1, [first.Key], true, default);
        Check(result.Single().Success && source.Deletes == 1 && service.ArchivePath(first.Key) != null, "source delete keeps archive");

        source.InvalidSize = true;
        var invalid = new ClipService(source, Path.Combine(root, "invalid"));
        result = await invalid.BatchAsync(1, [first.Key], false, default);
        Check(!result.Single().Success && invalid.Archive().Count == 0 && invalid.ArchivePath(first.Key) == null, "truncated download never committed");
        source.InvalidSize = false;
        source.Payload = System.Text.Encoding.UTF8.GetBytes("{\"error\":\"not a video\"}");
        result = await invalid.BatchAsync(1, [first.Key], false, default);
        Check(!result.Single().Success && invalid.Archive().Count == 0, "error response is not archived as MP4");
        source.Payload = [];
        result = await invalid.BatchAsync(1, [first.Key], false, default);
        Check(!result.Single().Success && result.Single().Message.Contains("empty"), "empty download reported separately");
        source.Payload = null;
        result = await invalid.BatchAsync(1, [first.Key], false, default);
        Check(result.Single().Success && invalid.Archive().Single().Bytes != 4, "complete MP4 accepted when manifest size differs");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await service.ListAsync(1, cancelled.Token); throw new Exception("Cancellation not honored"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS cancellation"); }
        await Task.WhenAll(service.ListAsync(1, default), service.ListAsync(1, default));
        Check(source.MaxConcurrent == 1, "Blink manifest operations serialized");

        string importFolder = Path.Combine(root, "usb");
        Directory.CreateDirectory(importFolder);
        await File.WriteAllBytesAsync(Path.Combine(importFolder, "local.mp4"), [5, 6, 7, 8]);
        var importer = new ClipService(source, Path.Combine(root, "imported"));
        Check((await importer.ImportAsync(importFolder, default)).Single().Success && importer.Archive().Count == 1, "USB/Pi folder import without Blink API");
        await importer.ImportAsync(importFolder, default);
        Check(importer.Archive().Count == 1 && File.Exists(Path.Combine(importFolder, "local.mp4")), "folder import deduplicates and preserves source");
        var imported = importer.Archive().Single();
        string deletedPath=importer.ArchivePath(imported.Key)!;importer.DeleteArchive(imported.Key);Check(!File.Exists(deletedPath)&&importer.Archive().Count==0&&File.Exists(Path.Combine(importFolder,"local.mp4")),"archive deletion removes recording and metadata while keeping import source");
        Check(importer.ArchivePath(imported.Key) == null, "uncommitted MP4 without metadata is not complete");

        await using var server = new MobileServer(service, 0, new ClientAccessStore(Path.Combine(root, "http-clients.json")));
        await server.StartAsync(false, default);
        var cookies = new CookieContainer();
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = new Uri(server.Addresses) };
        Check((await http.GetStringAsync("/")).Contains("DontBlink"), "embedded phone interface");
        using (var page = await http.GetAsync("/"))
            Check(page.Headers.GetValues("Content-Security-Policy").Single().Contains("blob:"), "live player security policy permits MediaSource URLs");
        using (var asset = await http.GetAsync("/assets/live-player.js"))
            Check(asset.IsSuccessStatusCode && (await asset.Content.ReadAsStringAsync()).Contains("addSourceBuffer"), "shared streaming player is served before pairing");
        using (var asset = await http.GetAsync("/assets/talk.js"))
            Check(asset.IsSuccessStatusCode && (await asset.Content.ReadAsStringAsync()).Contains("getUserMedia"), "microphone helper is available on the initial pairing page");
        Check((await http.GetAsync("/api/archive")).StatusCode == HttpStatusCode.Unauthorized, "phone API requires pairing");
        Check((await http.PostAsJsonAsync("/api/pair", new { code = server.PairingCode })).StatusCode == HttpStatusCode.Forbidden, "cross-origin form rejected");
        http.DefaultRequestHeaders.Add("X-DontBlink", "1");
        Check((await http.PostAsJsonAsync("/api/pair", new { code = "wrong" })).StatusCode == HttpStatusCode.Unauthorized, "wrong pairing code rejected");
        Check((await http.PostAsJsonAsync("/api/pair", new { code = server.PairingCode })).IsSuccessStatusCode, "phone paired");
        using (var livePage = await http.GetAsync("/live?network=2&camera=123"))
            Check(livePage.IsSuccessStatusCode && livePage.Content.Headers.ContentType?.CharSet == "utf-8"
                && (await livePage.Content.ReadAsStringAsync()).Contains("window.chrome.webview.postMessage('stop-live')"), "native live page uses UTF-8 and supports host Stop command");
        using (var streamRequest = new HttpRequestMessage(HttpMethod.Get, "/api/events"))
        using (var streamResponse = await http.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead))
        using (var streamReader = new StreamReader(await streamResponse.Content.ReadAsStreamAsync()))
        {
            Check(streamResponse.IsSuccessStatusCode && streamResponse.Content.Headers.ContentType?.MediaType == "text/event-stream", "paired browser notification stream");
            var publish = service.Notifications.PublishAsync(new ArchiveNotice("starting", first.Key, first.Camera, DateTime.UtcNow, "Archiving begins"), default);
            string? line;
            do { line = await streamReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)); } while (line != null && !line.StartsWith("data: "));
            Check(line?.Contains("Archiving begins") == true, "browser receives pre-copy notice");
            await publish;
        }
        using (var modules = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("/api/modules")))
        {
            var module = modules.RootElement[0];
            Check(module.GetProperty("id").GetInt32() == 1 && module.GetProperty("networkId").GetInt32() == 2, "phone module JSON contract");
        }
        Check((await http.GetFromJsonAsync<ClipItem[]>("/api/clips/1"))!.Length == 1, "paired phone lists USB clips");
        Check((await http.GetFromJsonAsync<CameraOverview[]>("/api/cameras"))!.Single().Id == 123, "paired phone camera overview");
        Check((await http.GetFromJsonAsync<ArchiveItem[]>("/api/archive"))!.Length == 1, "paired archive access");
        Check((await http.GetAsync("/thumbnail/not-a-key")).StatusCode == HttpStatusCode.NotFound, "thumbnail route rejects invalid archive IDs");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/media/" + first.Key);
        request.Headers.Range = new(0, 1);
        using var range = await http.SendAsync(request);
        Check(range.StatusCode == HttpStatusCode.PartialContent && (await range.Content.ReadAsByteArrayAsync()).Length == 2, "video range requests for phone seeking");
        var remoteBatch = await http.PostAsJsonAsync("/api/batch", new { moduleId = 1, keys = new[] { first.Key }, delete = false });
        Check(remoteBatch.IsSuccessStatusCode && (await remoteBatch.Content.ReadFromJsonAsync<BatchResult[]>())!.Single().Success, "paired phone batch operation");
        string httpTrustFile = Path.Combine(root, "http-trust.json");
        var trustCookies = new CookieContainer();
        await using (var trustedServer = new MobileServer(service, 0, new ClientAccessStore(httpTrustFile)))
        {
            await trustedServer.StartAsync(false, default);
            using var browser = new HttpClient(new HttpClientHandler { CookieContainer = trustCookies }) { BaseAddress = new Uri(trustedServer.Addresses) };
            browser.DefaultRequestHeaders.Add("X-DontBlink", "1");
            Check((await browser.PostAsJsonAsync("/api/pair", new { code = trustedServer.PairingCode, name = "Trusted phone", trust = true })).IsSuccessStatusCode,
                "phone can opt into trusted pairing");
        }
        await using (var nextServer = new MobileServer(service, 0, new ClientAccessStore(httpTrustFile)))
        {
            await nextServer.StartAsync(false, default);
            using var browser = new HttpClient(new HttpClientHandler { CookieContainer = trustCookies }) { BaseAddress = new Uri(nextServer.Addresses) };
            Check((await browser.GetAsync("/api/modules")).IsSuccessStatusCode, "trusted phone reconnects without pairing after server restart");
        }
    }
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name);
    }

    private sealed class RefreshHandler : HttpMessageHandler
    {
        public bool Fail,Revoked; public string? Hardware;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.AbsolutePath=="/oauth/token") {
                string form=await request.Content!.ReadAsStringAsync(ct);
                Hardware=Uri.UnescapeDataString(form.Split('&').Single(x=>x.StartsWith("hardware_id=")).Substring(12));
                Check(form.Contains("grant_type=refresh_token")&&!form.Contains("password"),"restore sends refresh grant only");
                if(Fail)return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{}")};
                if(Revoked)return new(HttpStatusCode.BadRequest){Content=new StringContent("{\"error\":\"invalid_grant\"}")};
                return new(HttpStatusCode.OK){Content=new StringContent("{\"access_token\":\"synthetic-access\",\"refresh_token\":\"synthetic-refresh-two\",\"expires_in\":3600}")};
            }
            if(request.RequestUri.AbsolutePath.EndsWith("tier_info"))return new(HttpStatusCode.OK){Content=new StringContent("{\"tier\":\"test\",\"account_id\":1}")};
            throw new Exception("Unexpected request; session restore must not request SMS");
        }
    }
    private sealed class OAuthHandler(HttpStatusCode challenge) : HttpMessageHandler
    {
        public bool Verified;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/authorize")) return new(HttpStatusCode.OK) { Content = new StringContent("") };
            if (path.EndsWith("/signin") && request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent("<script id=\"oauth-args\">{\"csrf-token\":\"test-csrf\"}</script>") };
            if (path.EndsWith("/signin")) return new(challenge) { Content = new StringContent("{\"tsv_state\":\"required\"}") };
            if (path.EndsWith("/2fa/verify"))
            {
                string form = await request.Content!.ReadAsStringAsync(ct);
                Check(form.Contains("2fa_code=123456") && form.Contains("csrf-token=test-csrf"), "PIN and original CSRF submitted together");
                Verified = true;
                return new(HttpStatusCode.Created) { Content = new StringContent("{\"status\":\"auth-completed\"}") };
            }
            throw new Exception("Unexpected OAuth request: " + path);
        }
    }

    private sealed class FakeSource : IClipSource
    {
        public bool Missing, Duplicate, InvalidSize;
        public bool FailDeletes;
        public Action? OnDownload, OnDelete;
        public string ReportedSize = "4";
        public byte[]? Payload;
        public int Downloads, Deletes, MaxConcurrent;
        private int _manifest, _active;
        public Task<CameraOverview[]> CamerasAsync() => Task.FromResult(new[] { new CameraOverview(123,2,"Garden","Outdoor","Unknown","ok","Enabled") });
        public Task<SyncModule[]> ModulesAsync() => Task.FromResult(new[] { new SyncModule { Id = 1, NetworkId = 2 } });
        public async Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _active));
            try
            {
                await Task.Delay(10);
                _manifest++;
                if (Missing) return [];
                var video = new BlinkVideoInfo { Id = "clip-" + _manifest, ManifestId = _manifest.ToString(), ModuleId = 1, NetworkId = 2,
                    CameraName = "../camera:unsafe", CreatedAt = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), Size = ReportedSize };
                return Duplicate ? [video, video] : [video];
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public Task<byte[]> DownloadAsync(BlinkVideoInfo video)
        {
            OnDownload?.Invoke();
            Check(video.Id == "clip-" + _manifest, "current download ID");
            Downloads++;
            // Structurally complete synthetic ISO BMFF, not a decodable movie.
            byte[] mp4 = [0,0,0,16, 102,116,121,112, 109,112,52,50, 0,0,0,0,
                0,0,0,16, 109,111,111,118, 0,0,0,8, 102,114,101,101,
                0,0,0,12, 109,100,97,116, 1,2,3,4];
            return Task.FromResult(Payload ?? (InvalidSize ? mp4[..^1] : mp4));
        }
        public Task DeleteAsync(BlinkVideoInfo video)
        {
            OnDelete?.Invoke();
            Check(video.Id == "clip-" + _manifest, "current delete ID");
            if (FailDeletes) throw new IOException("Simulated USB deletion failure");
            Deletes++; return Task.CompletedTask;
        }
    }
}
