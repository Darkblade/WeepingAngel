namespace DontBlink.Services;

public record ArchiveProgress(Guid BatchId,int ModuleId,string Stage,int Total,int Processed,int Succeeded,int Failed,int Skipped,int Index,string Camera,long CurrentBytes,long? CurrentTotalBytes,long CompletedBytes,double ElapsedSeconds,double? EtaSeconds,DateTimeOffset UpdatedUtc)
{
    public string Summary=>$"{Stage} · clip {Index}/{Total} · {Processed} processed, {Succeeded} succeeded, {Failed} failed, {Skipped} skipped · {CurrentBytes/1048576d:0.00}"+(CurrentTotalBytes is long n?$"/{n/1048576d:0.00} MB":" MB")+$" · elapsed {TimeSpan.FromSeconds(ElapsedSeconds):hh\\:mm\\:ss} · ETA "+(EtaSeconds is double eta?"~"+TimeSpan.FromSeconds(eta).ToString(@"hh\:mm\:ss"):"estimating");
}

public sealed partial class ClipService
{
    private readonly object _progressLock=new();
    private ArchiveProgress? _progress;
    public ArchiveProgress? Progress {get{lock(_progressLock)return _progress;}}
    public event Action<ArchiveProgress>? ProgressChanged;
    public event Action<string,int>? Diagnostic;
    private void PublishProgress(ArchiveProgress value){lock(_progressLock)_progress=value;ProgressChanged?.Invoke(value);}
}
