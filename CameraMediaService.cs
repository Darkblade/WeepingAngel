using System.Collections.Concurrent;

namespace DontBlink.Services;

public sealed class CameraMediaService(ClipService clips)
{
    private sealed record CachedFrame(byte[] Bytes, DateTime Fetched, DateTime Captured);
    private readonly ConcurrentDictionary<(int, int), CachedFrame> _images = new();
    private readonly ConcurrentDictionary<(int, int), SemaphoreSlim> _locks = new();
    public byte[]? CachedImage(int network,int camera){
        if(_images.TryGetValue((network,camera),out var cached))return cached.Bytes;
        if(clips.CameraCacheDirectory is not string folder)return null;
        try{string path=Path.Combine(folder,network+"-"+camera+".jpg");if(new FileInfo(path).Length>10*1024*1024)return null;return File.ReadAllBytes(path);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return null;}
    }
    public async Task<byte[]> ImageAsync(int network, int camera, bool capture, CancellationToken ct)
    {
        if(!clips.ApiAvailable){if(capture)throw new BlinkSessionUnavailableException();return CachedImage(network,camera)??throw new FileNotFoundException("No cached image for this camera.");}
        var key = (network, camera);
        var gate = _locks.GetOrAdd(key, _ => new(1));
        await gate.WaitAsync(ct);
        try
        {
            _images.TryGetValue(key, out var cached);
            if (cached != null && !capture && DateTime.UtcNow - cached.Fetched < TimeSpan.FromSeconds(30)) return cached.Bytes;
            var bytes = await clips.CameraOperationAsync(async source => {var image=await source.CameraImageAsync(network,camera,capture);if(image.Length<4)throw new IOException("Camera returned an empty image.");if(capture)clips.RememberSuccessfulCapture(network,camera,DateTimeOffset.UtcNow);return image;}, ct, capture ? network : null);
            if (bytes.Length < 4) throw new IOException("Camera returned an empty image.");
            _images[key] = new(bytes, DateTime.UtcNow, capture ? DateTime.UtcNow : cached?.Captured ?? DateTime.MinValue);
            if(clips.CameraCacheDirectory is string folder){try{Directory.CreateDirectory(folder);await File.WriteAllBytesAsync(Path.Combine(folder,network+"-"+camera+".jpg"),bytes,ct);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}}
            return bytes;
        }
        finally { gate.Release(); }
    }
}

