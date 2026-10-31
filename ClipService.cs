using Blink.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DontBlink.Services;

public record ClipItem(string Key, int ModuleId, int NetworkId, string Camera, DateTime CreatedUtc, string Size);
public record ArchiveItem(string Key, string Camera, DateTime CreatedUtc, string FileName, long Bytes, string? Sha256 = null);
public record BatchResult(string Key, bool Success, string Message);

public interface IClipSource
{
    bool IsAuthenticated => true;
    bool SupportsVideoRequest => false;
    Task RequestVideoAsync(int network, int camera) => throw new NotSupportedException("Video capture is unavailable from this source.");
    Task SetMotionAsync(int network, int camera, bool enabled) => throw new NotSupportedException("Motion control is unavailable from this source.");
    Task<CameraOverview[]> CamerasAsync() => Task.FromResult(Array.Empty<CameraOverview>());
    Task<byte[]> CameraImageAsync(int network, int camera, bool capture) => throw new NotSupportedException("Camera images are unavailable from this source.");
    Task<BlinkLiveSession> OpenLiveAsync(int network, int camera) => throw new NotSupportedException("Live view is unavailable from this source.");
    Task<bool> PollLiveAsync(int network, long command) => Task.FromResult(true);
    Task EndLiveAsync(int network, long command) => Task.CompletedTask;
    Task<SyncModule[]> ModulesAsync();
    Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module);
    Task<byte[]> DownloadAsync(BlinkVideoInfo video);
    Task<byte[]> DownloadAsync(BlinkVideoInfo video,Action<long,long?>? progress,CancellationToken ct)=>DownloadAsync(video);
    Task DeleteAsync(BlinkVideoInfo video);
}

public sealed partial class BlinkClipSource(BlinkAuthService auth) : IClipSource
{
    public bool IsAuthenticated => auth.IsAuthenticated;
    public bool SupportsVideoRequest => true;
    public async Task RequestVideoAsync(int network,int camera)
    {
        var item=(await CamerasAsync()).SingleOrDefault(c=>c.NetworkId==network&&c.Id==camera)??throw new ArgumentException("Camera is not in this Blink system.");
        var family=CameraVideoCommands.Family(item);
        if(!await Call(c=>c.RequestNewVideoAsync(network,camera,family)))throw new IOException("Blink did not confirm the video capture command. Check USB clips before retrying.");
    }
    private async Task<T> Call<T>(Func<Blink.BlinkClient,Task<T>> operation){
        try{return await operation(auth.GetClient());}
        catch(Exception ex) when(BlinkSessionUnavailableException.IsAuthenticationFailure(ex)){auth.MarkSessionUnavailable();throw new BlinkSessionUnavailableException(ex);}
    }
    public async Task SetMotionAsync(int network,int camera,bool enabled){bool completed=await Call(c=>enabled?c.EnableMotionDetectionAsync(network,camera):c.DisableMotionDetectionAsync(network,camera));if(!completed)throw new IOException("The camera did not complete the motion detection change.");}
    public Task<byte[]> CameraImageAsync(int network,int camera,bool capture)=>Call(c=>c.GetCameraThumbnailBytesAsync(network,camera,capture));
    public Task<BlinkLiveSession> OpenLiveAsync(int network,int camera)=>Call(c=>c.OpenLiveSessionAsync(network,camera));
    public Task<bool> PollLiveAsync(int network,long command)=>Call(c=>c.IsLiveCommandRunningAsync(network,command));
    public Task EndLiveAsync(int network,long command)=>Task.CompletedTask;
    public Task<CameraOverview[]> CamerasAsync()=>Call(async c=>{var dashboard=await c.GetDashboardAsync();RememberDashboard(dashboard);return dashboard.Cameras.Select(CameraOverview.Parse).OfType<CameraOverview>().ToArray();});
    public Task<SyncModule[]> ModulesAsync()=>Call(async c=>(await c.GetDashboardAsync()).SyncModules);
    public Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module)=>Call(c=>c.GetVideosFromModuleAsync(module));
    public Task<byte[]> DownloadAsync(BlinkVideoInfo video)=>Call(c=>c.GetVideoBytesAsync(video));
    public Task<byte[]> DownloadAsync(BlinkVideoInfo video,Action<long,long?>? progress,CancellationToken ct)=>Call(c=>c.GetVideoBytesWithProgressAsync(video,progress,ct));
    public async Task DeleteAsync(BlinkVideoInfo video)=>await Call(async c=>{await c.DeleteVideoAsync(video);return true;});
}

public sealed partial class ClipService(IClipSource source, string archiveRoot, string? cameraCachePath = null)
{
    // All manifest operations share a queue, including mobile and background requests.
    private readonly PriorityOperationGate _gate = new();
    private readonly SemaphoreSlim _batchGate=new(1,1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int,int> _moduleNetworks=new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _liveNetworks = new();
    private readonly Dictionary<string, ArchiveItem> _archive = new();
    private string _root = Path.GetFullPath(archiveRoot);
    public string ArchiveRoot => _root;
    private readonly CameraSnapshotStore _cameraSnapshot = new(cameraCachePath);
    public bool ApiAvailable => source.IsAuthenticated;
    public string? CameraCacheDirectory => cameraCachePath == null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cameraCachePath))!, "camera-images");
    public CameraOverview[] LastKnownCameras => _cameraSnapshot.Read();
    public event Action<string>? Log;
    public ArchiveNotifications Notifications { get; } = new();
    public async Task<T> CameraOperationAsync<T>(Func<IClipSource, Task<T>> action, CancellationToken ct, int? snapshotNetwork = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (snapshotNetwork.HasValue && _liveNetworks.ContainsKey(snapshotNetwork.Value))
                throw new IOException("This Sync Module is serving a live session. Stop live viewing before requesting a camera capture.");
            return await action(source);
        }
        finally { _gate.Release(); }
    }
    public async Task<IDisposable> ReserveLiveNetworkAsync(int network, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_liveNetworks.TryAdd(network, 0)) throw new IOException("Another camera on this Sync Module is live. Stop that live view first.");
            return new NetworkReservation(_liveNetworks, network);
        }
        finally { _gate.Release(); }
    }
    private sealed class NetworkReservation(System.Collections.Concurrent.ConcurrentDictionary<int, byte> networks, int network) : IDisposable
    { public void Dispose() => networks.TryRemove(network, out _); }

    // Called under the camera-operation gate only after a fresh capture returns image bytes.
    internal void RememberSuccessfulCapture(int network,int camera,DateTimeOffset completedUtc)
    {
        _cameraSnapshot.Write(LastKnownCameras.Select(c=>c.ServiceId=="blink"&&c.NetworkId==network&&c.Id==camera?c with{ThumbnailCapturedUtc=completedUtc,ThumbnailTimeEstimated=true}:c).ToArray());
    }
    public async Task<CameraOverview[]> CamerasAsync(CancellationToken ct) => (await CameraOverviewAsync(ct)).Cameras;
    public async Task<CameraSnapshot> CameraOverviewAsync(CancellationToken ct){
        await _gate.WaitAsync(ct);
        try{
            if(!ApiAvailable)return new(LastKnownCameras,false,true,BlinkSessionUnavailableException.UserMessage);
            try{var cameras=await source.CamerasAsync();var saved=LastKnownCameras; cameras=cameras.Select(c=>{var previous=saved.FirstOrDefault(p=>p.ServiceId==c.ServiceId&&p.NetworkId==c.NetworkId&&p.Id==c.Id);return previous?.ThumbnailCapturedUtc is {} known&&(c.ThumbnailCapturedUtc==null||previous.ThumbnailTimeEstimated&&c.ThumbnailCapturedUtc<known)?c with{ThumbnailCapturedUtc=known,ThumbnailTimeEstimated=previous.ThumbnailTimeEstimated}:c;}).ToArray();_cameraSnapshot.Write(cameras);return new(cameras,true,false,null);}
            catch(BlinkSessionUnavailableException){return new(LastKnownCameras,false,true,BlinkSessionUnavailableException.UserMessage);}
        }finally{_gate.Release();}
    }

    public async Task ChangeArchiveRootAsync(string folder, CancellationToken ct)
    {
        string full = Path.GetFullPath(folder);
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(full);
            string probe = Path.Combine(full, ".dontblink-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            lock (_archive) { _root = full; _archive.Clear(); }
        }
        finally { _gate.Release(); }
    }

    private string NewFileName(DateTime utc, string camera, string? folder = null)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new(camera.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        safe = safe.Trim().TrimEnd('.');
        if (safe.Length > 100) safe = safe[..100];
        if (safe.Length == 0) safe = "Camera";
        string stem = $"{utc.ToLocalTime():yyyy.MM.dd - HH-mm-ss} - {safe}";
        string name = stem + ".mp4";
        for (int suffix = 2; File.Exists(Path.Combine(folder ?? _root, name)); suffix++) name = $"{stem} ({suffix}).mp4";
        return name;
    }

    private static bool SafeFileName(string name) => !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    public async Task<SyncModule[]> ModulesAsync(CancellationToken ct, bool background=false)
    {
        if(background)await _gate.WaitBackgroundAsync(ct);else await _gate.WaitAsync(ct);
        try { return await source.ModulesAsync(); }
        finally { _gate.Release(); }
    }

    public async Task<List<ClipItem>> ListAsync(int moduleId, CancellationToken ct, bool background=false)
    {
        if(background)await _gate.WaitBackgroundAsync(ct);else await _gate.WaitAsync(ct);
        try { return (await FreshAsync(moduleId, ct)).Values.Select(ToItem).OrderByDescending(x => x.CreatedUtc).ToList(); }
        finally { _gate.Release(); }
    }

    public async Task<List<BatchResult>> BatchAsync(int moduleId, IEnumerable<string> keys, bool delete, CancellationToken ct, bool removeAfterArchive = false)
    {
        var requested = keys.Distinct().ToArray();
        if (requested.Length > 1000) throw new ArgumentException("Select at most 1000 clips per batch.");
        await _batchGate.WaitAsync(ct);
        try
        {
            var results = new List<BatchResult>();var batchId=Guid.NewGuid();var watch=System.Diagnostics.Stopwatch.StartNew();int skipped=0,index=0;long completedBytes=0,currentBytes=0;long? currentTotal=null;string currentCamera="",stage="Starting";double lastDetail=-10,lastTrace=-10;
            void Report(string nextStage,long? bytes=null,long? total=null){stage=nextStage;if(bytes.HasValue)currentBytes=bytes.Value;if(total.HasValue)currentTotal=total;
                double elapsed=watch.Elapsed.TotalSeconds;int processed=results.Count;double? eta=processed>0?Math.Max(0,elapsed/processed*(requested.Length-processed)):null;
                var update=new ArchiveProgress(batchId,moduleId,stage,requested.Length,processed,results.Count(r=>r.Success),results.Count(r=>!r.Success),skipped,index,currentCamera,currentBytes,currentTotal,completedBytes,elapsed,eta,DateTimeOffset.UtcNow);PublishProgress(update);
                if(nextStage!="Downloading"||elapsed-lastDetail>=5){Diagnostic?.Invoke(update.Summary,2);lastDetail=elapsed;}if(elapsed-lastTrace>=1){Diagnostic?.Invoke($"Module {moduleId} / {currentCamera}: {stage}; received {currentBytes} bytes; total {(currentTotal?.ToString()??"unknown")}; batch {batchId}.",3);lastTrace=elapsed;}}
            Report("Starting");if(requested.Length>0)Log?.Invoke($"Archive batch: {requested.Length} clips on module {moduleId}; {(delete?"delete":"copy / verify")}{(removeAfterArchive?" / remove USB":"")}.");
            Dictionary<string, BlinkVideoInfo>? fresh = null;
            DateTime expires = DateTime.MinValue;
            foreach (var key in requested)
            {
                ct.ThrowIfCancellationRequested();
                Report("Waiting for archive turn");
                await WaitForArchiveTurnAsync(moduleId,ct);
                try
                {
                // Any intervening foreground command may invalidate a manifest's IDs.
                fresh=null;
                index++;currentBytes=0;currentTotal=null;currentCamera="";Report("Reading manifest");
                bool notified = false;
                string camera = "Camera";
                try
                {
                    // IDs belong to a manifest. Re-resolve stable metadata before using them.
                    if (fresh == null || DateTime.UtcNow >= expires || delete)
                    {
                        fresh = await FreshAsync(moduleId, ct);
                        expires = DateTime.UtcNow.AddMinutes(2);
                    }
                    if (!fresh.TryGetValue(key, out var video)) throw new IOException("Clip is no longer in the current manifest.");
                    camera = video.CameraName;currentCamera=camera;Report("Preparing clip");
                    bool alreadyArchived = !delete && ArchivePath(key) != null;
                    string message;
                    if (delete)
                    {
                        Report("Deleting USB clip");await source.DeleteAsync(video);
                        fresh = null;
                        message = "Deleted from USB";
                    }
                    else
                    {
                        if (removeAfterArchive)
                        {
                            await Notifications.PublishAsync(new("starting", key, video.CameraName, DateTime.UtcNow,
                                alreadyArchived ? $"{video.CameraName}: existing archive will be verified before USB cleanup."
                                : $"{video.CameraName}: copying recording to archive, then verifying before USB cleanup."), ct);
                            notified = true;
                            Log?.Invoke($"Archive notice: {video.CameraName}. Notify complete; proceeding to copy/verification before USB deletion.");
                            ct.ThrowIfCancellationRequested();
                        }
                        Report(alreadyArchived?"Verifying existing archive":"Waiting for Blink video");long copied=await DownloadAsync(key,video,ct,(next,bytes,total)=>Report(next,bytes,total));completedBytes+=copied;if(alreadyArchived)skipped++;
                        message = alreadyArchived ? "Already archived; download skipped" : "Archived";
                        if (removeAfterArchive)
                        {
                            // Keep the verified file locked against writes/removal until
                            // the USB deletion completes. Cancellation never deletes early.
                            Report("Verifying saved copy");await using var verified = await OpenVerifiedArchiveAsync(key, ct);
                            fresh = await FreshAsync(moduleId, ct);
                            ct.ThrowIfCancellationRequested();
                            if (fresh.TryGetValue(key, out var current))
                            {
                                try { Report("Removing USB original");await source.DeleteAsync(current); }
                                catch (Exception ex) { throw new IOException($"Archive copy is safe; USB deletion failed and can be retried without downloading again: {ex.Message}", ex); }
                                message += "; removed from USB";
                            }
                            else message += "; USB clip is no longer listed";
                            fresh = null;
                        }
                    }
                    var result = new BatchResult(key, true, message);
                    results.Add(result);Report("Clip complete");
                    Log?.Invoke($"{video.CameraName} {video.CreatedAt.ToLocalTime():yyyy.MM.dd HH:mm:ss}: {result.Message}");
                    if (removeAfterArchive && !delete)
                    {
                        try { await Notifications.PublishAsync(new("completed", key, video.CameraName, DateTime.UtcNow, $"{video.CameraName}: {message}."), ct); }
                        catch (Exception ex) { Log?.Invoke($"Archive completed; client status update failed: {ex.Message}"); }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    fresh = null;
                    results.Add(new(key, false, ex.Message));Report("Clip failed");
                    Log?.Invoke($"Clip operation failed: {ex.Message}");
                    if (notified)
                    {
                        try { await Notifications.PublishAsync(new("failed", key, camera, DateTime.UtcNow, $"{camera}: archiving or USB cleanup failed. See the Windows app log; saved copies are retained."), ct); }
                        catch (Exception) { }
                    }
                }
            }
                finally { _gate.Release(); }
            }
            Report("Complete");if(requested.Length>0)Log?.Invoke($"Archive batch complete: {results.Count(r=>r.Success)}/{requested.Length} succeeded, {results.Count(r=>!r.Success)} failed, {skipped} skipped; {completedBytes/1048576d:0.00} MB copied in {watch.Elapsed.TotalSeconds:0.0} seconds.");return results;
        }
        catch(OperationCanceledException){if(Progress is {} stopped)PublishProgress(stopped with{Stage="Cancelled",EtaSeconds=null,UpdatedUtc=DateTimeOffset.UtcNow});throw;}
        finally { _batchGate.Release(); }
    }

    private async Task WaitForArchiveTurnAsync(int module,CancellationToken ct)
    {
        bool reported=false;
        while(true){await _gate.WaitBackgroundAsync(ct);if(!_moduleNetworks.TryGetValue(module,out int network)||!_liveNetworks.ContainsKey(network))return;_gate.Release();if(!reported){Log?.Invoke($"Archive module {module}: paused for live view; remaining clips stay queued.");reported=true;}await Task.Delay(250,ct);}
    }
    private async Task<Dictionary<string, BlinkVideoInfo>> FreshAsync(int moduleId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var module = (await source.ModulesAsync()).SingleOrDefault(m => m.Id == moduleId)
            ?? throw new InvalidOperationException("Sync Module not found. Reload modules.");
        _moduleNetworks[moduleId]=module.NetworkId;
        if (_liveNetworks.ContainsKey(module.NetworkId)) throw new IOException("USB processing is paused while this Sync Module is serving live view.");
        var videos = await source.ListAsync(module);
        ct.ThrowIfCancellationRequested();
        // Ambiguous metadata must never resolve to an arbitrary clip, especially for deletion.
        return videos.GroupBy(StableKey).ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single()
            : throw new InvalidOperationException("Two clips have identical metadata; operation stopped to avoid selecting the wrong clip."));
    }

    public static string StableKey(BlinkVideoInfo video) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{video.NetworkId}|{video.ModuleId}|{video.CameraName}|{video.CreatedAt.ToUniversalTime():O}|{video.Size}")));
    private static ClipItem ToItem(BlinkVideoInfo video) => new(StableKey(video), video.ModuleId, video.NetworkId,
        video.CameraName, video.CreatedAt.ToUniversalTime(), video.Size);

    private async Task<long> DownloadAsync(string key, BlinkVideoInfo video, CancellationToken ct,Action<string,long?,long?> report)
    {
        Directory.CreateDirectory(_root);
        string metadata = Path.Combine(_root, key + ".json");
        if (ArchivePath(key) != null) return 0;
        string fileName = NewFileName(video.CreatedAt.ToUniversalTime(), video.CameraName);
        string final = Path.Combine(_root, fileName);
        var bytes = await source.DownloadAsync(video,(received,total)=>report("Downloading",received,total),ct);report("Validating MP4",bytes.LongLength,bytes.LongLength);
        ct.ThrowIfCancellationRequested();
        Mp4Validation.Validate(bytes);
        if (long.TryParse(video.Size, out var size) && size > 0 && bytes.LongLength != size)
            Log?.Invoke($"Clip size differs from manifest: reported {size}, received {bytes.LongLength} bytes. MP4 structure passed validation; archiving the received file.");
        string temporary = final + ".partial";
        try
        {
            report("Writing archive",bytes.LongLength,bytes.LongLength);await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, final, false);
            var item = new ArchiveItem(key, video.CameraName, video.CreatedAt.ToUniversalTime(), fileName, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
            await File.WriteAllTextAsync(metadata + ".partial", JsonSerializer.Serialize(item), ct);
            File.Move(metadata + ".partial", metadata, true);
            lock (_archive) _archive[key] = item;return bytes.LongLength;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(metadata + ".partial")) File.Delete(metadata + ".partial");
        }
    }

    private async Task<FileStream> OpenVerifiedArchiveAsync(string key, CancellationToken ct)
    {
        string path = ArchivePath(key) ?? throw new IOException("Archive verification failed. USB original was kept.");
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        try
        {
            var metadata = JsonSerializer.Deserialize<ArchiveItem>(await File.ReadAllTextAsync(Path.Combine(_root, key + ".json"), ct))
                ?? throw new IOException("Archive metadata is missing. USB original was kept.");
            if (file.Length != metadata.Bytes) throw new IOException("Archive size verification failed. USB original was kept.");
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            Mp4Validation.Validate(bytes);
            if (metadata.Sha256 != null && !string.Equals(metadata.Sha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Archive checksum verification failed. USB original was kept.");
            ct.ThrowIfCancellationRequested();
            return file;
        }
        catch { await file.DisposeAsync(); throw; }
    }

    public async Task<string> SaveLiveRecordingAsync(string temporary, string camera, DateTime startedUtc)
    {
        await _gate.WaitAsync();
        try
        {
            var bytes = await File.ReadAllBytesAsync(temporary);
            Mp4Validation.Validate(bytes);
            string key = Convert.ToHexString(SHA256.HashData(bytes));
            string directory = Path.GetDirectoryName(Path.GetFullPath(temporary))!;
            // Keep the destination captured when recording started, even if settings changed.
            string name = NewFileName(startedUtc, camera, directory);
            string final = Path.Combine(directory, name);
            File.Move(temporary, final, false);
            var item = new ArchiveItem(key, camera, startedUtc, name, bytes.LongLength, key);
            string sidecar = Path.Combine(directory, key + ".json");
            await File.WriteAllTextAsync(sidecar + ".partial", JsonSerializer.Serialize(item));
            File.Move(sidecar + ".partial", sidecar, true);
            Log?.Invoke($"Live recording saved: {final}");
            return final;
        }
        finally { _gate.Release(); }
    }

    public List<ArchiveItem> Archive()
    {
        lock (_archive)
        {
            _archive.Clear();
            if (Directory.Exists(_root))
                foreach (string file in Directory.EnumerateFiles(_root, "*.json"))
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<ArchiveItem>(File.ReadAllText(file));
                        if (item != null && ValidKey(item.Key) && SafeFileName(item.FileName)
                            && item.Bytes > 0 && File.Exists(Path.Combine(_root, item.FileName))
                            && new FileInfo(Path.Combine(_root, item.FileName)).Length == item.Bytes) _archive[item.Key] = item;
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
                }
            return _archive.Values.OrderByDescending(x => x.CreatedUtc).ToList();
        }
    }

    public void DeleteArchive(string key)
    {
        lock(_archive)
        {
            string path=ArchivePath(key)??throw new FileNotFoundException("Archived recording is missing or changed. Reload the archive.");
            File.Delete(path);
            File.Delete(Path.Combine(_root,key+".json"));
            _archive.Remove(key);
            try{File.Delete(Path.Combine(_root,".thumbnails",key+".jpg"));}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    public string? ArchivePath(string key)
    {
        lock (_archive)
        {
        if (!ValidKey(key)) return null;
        try
        {
            var item = JsonSerializer.Deserialize<ArchiveItem>(File.ReadAllText(Path.Combine(_root, key + ".json")));
            if (item == null || !SafeFileName(item.FileName)) return null;
            string path = Path.Combine(_root, item.FileName);
            return item.Key == key && item.Bytes > 0
                && File.Exists(path) && new FileInfo(path).Length == item.Bytes ? path : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }
    }
    internal static bool ValidKey(string? key) => key?.Length == 64 && key.All(Uri.IsHexDigit);

    public async Task<List<BatchResult>> ImportAsync(string folder, CancellationToken ct)
    {
        var results = new List<BatchResult>();
        string sourceRoot = Path.GetFullPath(folder);
        if (sourceRoot.Equals(_root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose the USB drive or Pi export folder, not the archive itself.");
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_root);
            // Freeze the input list so creating an archive inside a chosen parent cannot recurse.
            var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFullPath(f).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();
                string? temporary = null;
                try
                {
                    var info = new FileInfo(file);
                    long length = info.Length;
                    DateTime modified = info.LastWriteTimeUtc;
                    if (length == 0) throw new IOException("Empty MP4 skipped.");
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    string key = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
                    if (ArchivePath(key) != null) { results.Add(new(key, true, "Already imported")); continue; }
                    string camera = "Imported · " + (Path.GetFileName(Path.GetDirectoryName(file)) ?? "USB");
                    string fileName = NewFileName(modified, camera);
                    string final = Path.Combine(_root, fileName);
                    temporary = final + ".partial";
                    input.Position = 0;
                    await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                        await input.CopyToAsync(output, ct);
                    info.Refresh();
                    if (info.Length != length || info.LastWriteTimeUtc != modified) throw new IOException("Source changed during import; export a stable copy first.");
                    File.Move(temporary, final, false);
                    var item = new ArchiveItem(key, camera, modified, fileName, length, key);
                    string metadata = Path.Combine(_root, key + ".json");
                    temporary = metadata + ".partial";
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(item), ct);
                    File.Move(temporary, metadata, true);
                    results.Add(new(key, true, "Imported"));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { results.Add(new(file, false, ex.Message)); Log?.Invoke($"Import failed: {file}: {ex.Message}"); }
                finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            }
            return results;
        }
        finally { _gate.Release(); }
    }
}
