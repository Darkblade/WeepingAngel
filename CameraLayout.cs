namespace DontBlink.Services;

internal static class CameraLayout
{
    internal static string[] Reorder(IEnumerable<string> keys,string source,string target)
    {
        var order=keys.ToList();int from=order.IndexOf(source),to=order.IndexOf(target);if(from<0||to<0||from==to)return order.ToArray();order.RemoveAt(from);order.Insert(to,source);return order.ToArray();
    }
    internal static string Key(CameraOverview camera) => camera.ServiceId=="blink"?$"{camera.NetworkId}:{camera.Id}":$"{camera.ServiceId}:{camera.NetworkId}:{camera.Id}";
    internal static IEnumerable<CameraOverview> Order(IEnumerable<CameraOverview> cameras, IEnumerable<string>? saved)
    {
        var positions=(saved??[]).Distinct().Select((key,index)=>(key,index)).ToDictionary(pair=>pair.key,pair=>pair.index);
        // Stable ordering leaves newly discovered cameras at the end and tolerates removed cameras.
        return cameras.OrderBy(camera=>positions.GetValueOrDefault(Key(camera),int.MaxValue));
    }
}
