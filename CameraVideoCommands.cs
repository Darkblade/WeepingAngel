using Blink.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DontBlink.Services;

public sealed class CameraRequestConflictException(string message) : InvalidOperationException(message);
public sealed record CameraVideoRequestResult(bool CommandCompleted,string Message,double ElapsedSeconds);

public static class CameraVideoCommands
{
    public static bool IsSupported(CameraOverview camera) => camera.ServiceId=="blink" && camera.Model is "catalina" or "white" or "xt" or "xt2" or "owl" or "lotus";
    public static BlinkCameraType Family(CameraOverview camera) => IsSupported(camera) ? camera.Model switch {
        "owl"=>BlinkCameraType.Mini,"lotus"=>BlinkCameraType.Doorbell,_=>BlinkCameraType.Default
    } : throw new NotSupportedException("On-demand video capture is not supported for this camera model yet.");
}

public sealed partial class ClipService
{
    private readonly ConcurrentDictionary<(int,int),byte> _videoRequests=new();
    public async Task<CameraVideoRequestResult> RequestCameraVideoAsync(int network,int camera,CancellationToken ct)
    {
        if(network<=0||camera<=0)throw new ArgumentException("Choose a valid camera and system.");
        if(!ApiAvailable)throw new BlinkSessionUnavailableException();
        if(!source.SupportsVideoRequest)throw new NotSupportedException("This service does not support on-demand video capture.");
        if(!_videoRequests.TryAdd((network,camera),0))throw new CameraRequestConflictException("A video capture request is already running for this camera.");
        string name=$"Camera {camera}";var watch=Stopwatch.StartNew();
        try
        {
            return await CameraOperationAsync(async provider=>{
                ct.ThrowIfCancellationRequested();
                var selected=(await provider.CamerasAsync()).SingleOrDefault(c=>c.NetworkId==network&&c.Id==camera)??throw new ArgumentException("Camera is not in this system.");
                CameraVideoCommands.Family(selected);name=selected.Name;
                Log?.Invoke(name+": video clip requested; waiting for Blink command completion.");
                await provider.RequestVideoAsync(network,camera);
                string message=name+": Blink reports the capture command complete. Refresh USB clips to check the recording. Normal Auto Archive rules still apply.";
                Log?.Invoke(message+$" ({watch.Elapsed.TotalSeconds:0.0} seconds)");
                return new CameraVideoRequestResult(true,message,watch.Elapsed.TotalSeconds);
            },ct,network);
        }
        catch(Exception ex){Log?.Invoke(name+": video clip request did not complete: "+ex.Message+" Check USB clips before retrying; a sent command may still finish.");throw;}
        finally{_videoRequests.TryRemove((network,camera),out _);}
    }
}

