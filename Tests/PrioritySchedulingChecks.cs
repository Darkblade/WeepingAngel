using Blink.Models;
using DontBlink.Services;
internal static class PrioritySchedulingChecks
{
    public static async Task RunAsync(string root)
    {
        var gate=new PriorityOperationGate();await gate.WaitAsync();var low=gate.WaitBackgroundAsync(default);var high=gate.WaitAsync();gate.Release();await high.WaitAsync(TimeSpan.FromSeconds(2));Require(!low.IsCompleted,"foreground overtakes an already queued background request");gate.Release();await low;gate.Release();
        await gate.WaitAsync();using(var cancelled=new CancellationTokenSource()){var waiting=gate.WaitAsync(cancelled.Token);cancelled.Cancel();try{await waiting;throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}}gate.Release();await gate.WaitAsync();gate.Release();Require(true,"cancelled foreground wait does not leak the operation gate");
        var source=new Source();var clips=new ClipService(source,Path.Combine(root,"priority"));var keys=(await clips.ListAsync(1,default)).Select(c=>c.Key).ToArray();
        var batch=clips.BatchAsync(1,keys,false,default,removeAfterArchive:true);await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var user=clips.CameraOperationAsync(async s=>{source.Events.Add("user");await s.SetMotionAsync(2,3,true);return true;},default);
        source.Resume.SetResult();await user.WaitAsync(TimeSpan.FromSeconds(3));var results=await batch.WaitAsync(TimeSpan.FromSeconds(3));
        Require(results.All(r=>r.Success)&&string.Join(",",source.Events)=="copy2,delete2,user,copy1,delete1","archive finishes verified clip, serves user action, then resumes next clip");
        Require(source.MaxActive==1,"foreground and archive commands never overlap");
        var liveSource=new Source();var liveClips=new ClipService(liveSource,Path.Combine(root,"priority-live"));var liveKeys=(await liveClips.ListAsync(1,default)).Select(c=>c.Key).ToArray();var liveBatch=liveClips.BatchAsync(1,liveKeys,false,default);await liveSource.Started.Task;
        var reservation=liveClips.ReserveLiveNetworkAsync(2,default);liveSource.Resume.SetResult();using(var lease=await reservation){await Task.Delay(350);Require(liveSource.Copies==1&&!liveBatch.IsCompleted,"archive keeps remaining clips queued while user live view is active");}
        Require((await liveBatch.WaitAsync(TimeSpan.FromSeconds(3))).All(r=>r.Success),"archive resumes after live view closes");
    }
    private static void Require(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
    private sealed class Source:IClipSource
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Resume=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<string> Events=[];public int Copies,MaxActive;private int active;
        public Task<SyncModule[]> ModulesAsync()=>Task.FromResult(new[]{new SyncModule{Id=1,NetworkId=2}});
        public Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule m)=>Task.FromResult<IEnumerable<BlinkVideoInfo>>(Enumerable.Range(1,2).Select(i=>new BlinkVideoInfo{Id=i.ToString(),ManifestId="m",ModuleId=1,NetworkId=2,CameraName="Garden",CreatedAt=new DateTime(2026,10,31,8,0,i,DateTimeKind.Utc),Size="44"}));
        public async Task<byte[]> DownloadAsync(BlinkVideoInfo v){MaxActive=Math.Max(MaxActive,Interlocked.Increment(ref active));try{Copies++;Events.Add("copy"+v.Id);if(Copies==1){Started.SetResult();await Resume.Task;}return [0,0,0,16,102,116,121,112,109,112,52,50,0,0,0,0,0,0,0,16,109,111,111,118,0,0,0,8,102,114,101,101,0,0,0,12,109,100,97,116,1,2,3,4];}finally{Interlocked.Decrement(ref active);}}
        public Task DeleteAsync(BlinkVideoInfo v){Events.Add("delete"+v.Id);return Task.CompletedTask;}
        public Task SetMotionAsync(int n,int c,bool enabled){MaxActive=Math.Max(MaxActive,Interlocked.Increment(ref active));Interlocked.Decrement(ref active);return Task.CompletedTask;}
    }
}