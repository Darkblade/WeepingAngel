using System.Text.Json;

namespace DontBlink.Services;

public sealed partial class ClipService
{
    private ManagementSnapshot? _lastManagement;
    private string? ManagementPath=>CameraCacheDirectory is string folder?Path.Combine(Path.GetDirectoryName(folder)!,"systems.json"):null;
    public Task<ManagementSnapshot> ManagementAsync(bool refresh,CancellationToken ct)=>CameraOperationAsync(async s=>{
        if(_lastManagement==null&&ManagementPath is string path){try{_lastManagement=JsonSerializer.Deserialize<ManagementSnapshot>(File.ReadAllText(path));}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){}}
        if(!ApiAvailable||s is not IDeviceManagement manager)return (_lastManagement??new(DeviceFeatures.Services(false),[],true,null)) with{Services=DeviceFeatures.Services(ApiAvailable),Cached=true,Message=ApiAvailable?"This service does not expose device management.":BlinkSessionUnavailableException.UserMessage};
        _lastManagement=await manager.ManagementAsync(refresh,ct);
        if(ManagementPath is string save){try{Directory.CreateDirectory(Path.GetDirectoryName(save)!);File.WriteAllText(save+".partial",JsonSerializer.Serialize(_lastManagement));File.Move(save+".partial",save,true);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}}
        return _lastManagement;
    },ct);
    public async Task<ManagementSnapshot> SetArmedAsync(string service,int system,bool armed,CancellationToken ct)
    {
        if(service!="blink")throw new NotSupportedException("This service does not support system arm/disarm yet.");
        await CameraOperationAsync(async s=>{if(!ApiAvailable)throw new BlinkSessionUnavailableException();if(s is not IDeviceManagement manager)throw new NotSupportedException("System controls are not supported.");var current=await manager.ManagementAsync(false,ct);if(!current.Systems.Any(n=>n.ServiceId==service&&n.Id==system))throw new ArgumentException("System not found in the selected service.");Log?.Invoke($"Blink system {system}: {(armed?"Arm":"Disarm")} requested.");await manager.SetArmedAsync(system,armed,ct);return true;},ct);
        var updated=await ManagementAsync(true,ct);var state=updated.Systems.FirstOrDefault(n=>n.Id==system)?.Armed;
        Log?.Invoke($"Blink system {system}: reported state {(state==true?"Armed":state==false?"Disarmed":"Unknown")}. ");return updated;
    }
    public Task<CameraSettings> CameraSettingsAsync(string service,int network,int camera,CancellationToken ct)=>CameraOperationAsync(async s=>{
        if(service!="blink")throw new NotSupportedException("This camera service is not implemented yet.");
        if(!ApiAvailable)throw new BlinkSessionUnavailableException();return s is IDeviceManagement manager?await manager.CameraSettingsAsync(network,camera,ct):throw new NotSupportedException("Camera settings are unavailable for this service.");
    },ct);
    public async Task<CameraSettings> SetCameraSettingAsync(string service,int network,int camera,string setting,string value,CancellationToken ct)
    {
        var details=await CameraSettingsAsync(service,network,camera,ct);if(!details.Settings.Any(s=>s.Id==setting&&s.Editable))throw new NotSupportedException("This setting is a placeholder or is not writable for this camera.");
        await CameraOperationAsync(async s=>{if(setting=="enabled"){if(value is not("On" or "Off"))throw new ArgumentException("Choose On or Off.");await s.SetMotionAsync(network,camera,value=="On");}
            else if(setting=="illuminator_enable"&&s is IDeviceManagement manager)await manager.SetNightVisionAsync(network,camera,value,ct);
            else throw new NotSupportedException("This setting is not implemented.");return true;},ct,network);
        return await CameraSettingsAsync(service,network,camera,ct);
    }
}
