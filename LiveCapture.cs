using System.Buffers.Binary;

namespace DontBlink.Services;

public record LiveCaptureStatus(bool Recording, string? SavedFile, string? Error, bool Finalizing = false);

// Records whole fMP4 fragments. Starting part-way through a session prepends
// decoder initialization and begins at the next fragment, not a partial box.
public sealed class LiveCapture(ClipService clips, string camera, Action<string>? log)
{
    private readonly SemaphoreSlim _gate = new(1);
    private byte[] _pending = [];
    private byte[]? _fragment;
    private readonly MemoryStream _initialization = new();
    private FileStream? _file;
    private string? _temporary;
    private DateTime _started;
    private RecordingOverlay _overlay = new();
    private bool _includeFragment, _wroteInitial;
    private LiveCaptureStatus _status = new(false, null, null);
    private Task? _finalization;
    public LiveCaptureStatus Status => Volatile.Read(ref _status);

    public async Task SetRecordingAsync(bool enabled)
    {
        Task? finishing = null;
        await _gate.WaitAsync();
        try
        {
            if (enabled && _file == null)
            {
                if (_finalization is { IsCompleted: false }) throw new IOException("The previous recording is still being finalized.");
                string folder = clips.ArchiveRoot; Directory.CreateDirectory(folder);
                _temporary = Path.Combine(folder, ".live-" + Guid.NewGuid().ToString("N") + ".partial");
                _file = new FileStream(_temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true);
                _started = DateTime.UtcNow; _wroteInitial = false;
                _overlay = (AppSettings.Load().Overlay ?? new()).Validated();
                _status = new(true, _status.SavedFile, null);
                log?.Invoke($"Live recording started for {camera}; waiting for the next complete fragment.");
            }
            else if (!enabled)
            {
                if (_file != null)
                {
                    var file = _file; _file = null; _includeFragment = false;
                    _status = new(false, _status.SavedFile, null, true);
                    _finalization = FinishAsync(file, _temporary!, _started, _overlay);
                }
                finishing = _finalization;
            }
        }
        finally { _gate.Release(); }
        // Rendering overlays must not block delivery of the ongoing live stream.
        if (finishing != null) await finishing;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> bytes)
    {
        await _gate.WaitAsync();
        try
        {
            byte[] combined = new byte[_pending.Length + bytes.Length]; _pending.CopyTo(combined, 0); bytes.Span.CopyTo(combined.AsSpan(_pending.Length)); _pending = combined;
            int offset = 0;
            while (_pending.Length - offset >= 8)
            {
                uint shortSize = BinaryPrimitives.ReadUInt32BigEndian(_pending.AsSpan(offset));
                ulong size = shortSize;
                if (size == 1) { if (_pending.Length - offset < 16) break; size = BinaryPrimitives.ReadUInt64BigEndian(_pending.AsSpan(offset + 8)); }
                if (size < 8 || size > 32 * 1024 * 1024) throw new IOException("Invalid fragmented live recording data.");
                if ((ulong)(_pending.Length - offset) < size) break;
                byte[] box = _pending.AsSpan(offset, (int)size).ToArray(); offset += (int)size;
                string type = System.Text.Encoding.ASCII.GetString(box, 4, 4);
                if (type is "ftyp" or "moov") _initialization.Write(box);
                else if (type == "moof") { _fragment = box; _includeFragment = _file != null; }
                else if (type == "mdat" && _fragment != null)
                {
                    if (_includeFragment && _file != null)
                    {
                        if (!_wroteInitial) { _started = DateTime.UtcNow; await _file.WriteAsync(_initialization.ToArray()); _wroteInitial = true; }
                        await _file.WriteAsync(_fragment); await _file.WriteAsync(box);
                    }
                    _fragment = null; _includeFragment = false;
                }
            }
            _pending = _pending.AsSpan(offset).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_file != null) { await _file.DisposeAsync(); _file = null; }
            _status = new(false, _status.SavedFile, ex.Message);
            log?.Invoke($"Live recording failed for {camera}; partial file retained at {_temporary}: {ex.Message}");
        }
        finally { _gate.Release(); }
    }

    private async Task FinishAsync(FileStream file, string temporary, DateTime started, RecordingOverlay overlay)
    {
        try
        {
            await file.FlushAsync(); file.Flush(true); long length = file.Length; await file.DisposeAsync();
            if (length == 0)
            {
                File.Delete(temporary); _status = new(false, _status.SavedFile, "No complete video fragment was recorded. Record for a few seconds before stopping."); return;
            }
            if (overlay.Enabled) { log?.Invoke("Rendering recording overlays…"); await overlay.ApplyAsync(temporary, camera, started); }
            string saved = await clips.SaveLiveRecordingAsync(temporary, camera, started);
            _status = new(false, Path.GetFileName(saved), null);
            try { await clips.Notifications.PublishAsync(new("recorded", "", camera, DateTime.UtcNow, $"Live recording saved: {Path.GetFileName(saved)}"), CancellationToken.None); } catch (Exception) { }
        }
        catch (Exception ex)
        {
            await file.DisposeAsync(); _status = new(false, _status.SavedFile, ex.Message);
            log?.Invoke($"Live recording could not be finalized; recovery file {temporary}: {ex.Message}");
            throw;
        }
    }
}
