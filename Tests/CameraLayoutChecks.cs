using DontBlink.Services;

internal static class CameraLayoutChecks
{
    internal static void Run()
    {
        var a=new CameraOverview(1,2,"Garden","Outdoor","online","ok","Enabled");
        var b=a with{NetworkId=3};var c=a with{Id=8,Name="Door"};
        Check(CameraLayout.Reorder(["a","b","c"],"a","c").SequenceEqual(["b","c","a"]),"drop moves a camera to a later grid position");
        Check(CameraLayout.Reorder(["a","b","c"],"c","a").SequenceEqual(["c","a","b"]),"drop moves a camera to an earlier grid position");
        Check(CameraLayout.Reorder(["a","b"],"unknown","b").SequenceEqual(["a","b"]),"unknown drag data leaves camera order intact");
        string path=Path.Combine(Path.GetTempPath(),"WeepingAngel-order-"+Guid.NewGuid()+".json");
        try{
            new AppSettings(CameraOrder:[CameraLayout.Key(b),CameraLayout.Key(a)]).Save(path);
            var saved=AppSettings.Load(path).CameraOrder;
            Check(CameraLayout.Order([a,b,c],saved).SequenceEqual([b,a,c]),"camera order survives settings reload and distinguishes module IDs");
            Check(CameraLayout.Order([a with{Name="Renamed"},c],saved).First().Id==a.Id,"camera order survives renames and missing cameras");
            Check(CameraLayout.Order([a,b,c],[CameraLayout.Key(b),CameraLayout.Key(b)]).SequenceEqual([b,a,c]),"duplicate saved camera IDs do not break ordering");
        }finally{if(File.Exists(path))File.Delete(path);}
    }
    private static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
}
