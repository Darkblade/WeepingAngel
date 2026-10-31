using System.Text;
using System.Text.Json;
using System.Threading.Channels;
namespace DontBlink.Services;

// Bounded metadata-only writer. Packet processing never waits for disk I/O.
public sealed class TestSessionLog
{
    private readonly object sync=new();
    private Channel<string>? queue;private Task? writer;private string? id,path,error;private long sequence,dropped;private readonly int limit;
    public TestSessionLog(int segmentBytes=5*1024*1024){limit=Math.Max(1024,segmentBytes);}
    public record Status(bool Active,string? SessionId,string? Path,long Dropped,string? Error);
    public Status Snapshot(){lock(sync)return new(queue!=null,id,path,Interlocked.Read(ref dropped),error);}
    public void Start(string folder,string label)
    {
        lock(sync){if(queue!=null)throw new InvalidOperationException("A test session is already active.");if(writer is {IsCompleted:false})throw new InvalidOperationException("Previous test log is still flushing.");
            Directory.CreateDirectory(folder);id=DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N")[..8];path=System.IO.Path.Combine(folder,"current.jsonl");sequence=0;dropped=0;error=null;
            // Opening upfront gives the user an immediate error for an unwritable folder.
            using(var probe=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete)){}
            File.WriteAllText(System.IO.Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new{schemaVersion=1,sessionId=id,startedUtc=DateTimeOffset.UtcNow,label=Clean(label),currentFile=path,segments=new[]{"previous-3.jsonl","previous-2.jsonl","previous-1.jsonl","current.jsonl"},retention="Current session only; four segments of up to 5 MiB by default. Starting a session replaces the previous session.",metadataOnly=true}));
            foreach(string name in new[]{"previous-1.jsonl","previous-2.jsonl","previous-3.jsonl"})File.Delete(System.IO.Path.Combine(folder,name));
            queue=Channel.CreateBounded<string>(new BoundedChannelOptions(8192){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});var owned=queue;writer=Task.Run(()=>WriteAsync(owned,folder,path));
            Record("session_start",new{label=Clean(label),metadataOnly=true,scope="Configured devices: IPv4 TCP/UDP headers visible on the capture interface. No payload, credentials or TLS decryption."});
        }
    }
    public void Record(string kind,object data,DateTimeOffset? observedUtc=null)
    {
        lock(sync){if(queue==null)return;string line=JsonSerializer.Serialize(new{schemaVersion=1,sessionId=id,sequence=++sequence,recordedUtc=DateTimeOffset.UtcNow,observedUtc,kind,data});if(!queue.Writer.TryWrite(line))Interlocked.Increment(ref dropped);}
    }
    public void Marker(string action,string phase="manual",int? network=null,int? camera=null)=>Record("action",new{action=Clean(action),phase,service="blink",networkId=network,cameraId=camera});
    public async Task StopAsync()
    {
        Task? pending;lock(sync){Record("session_end",new{dropped=Interlocked.Read(ref dropped)});queue?.Writer.TryComplete();queue=null;pending=writer;}if(pending!=null)await pending;
    }
    private async Task WriteAsync(Channel<string> owned,string folder,string current)
    {
        StreamWriter? output=null;long bytes=0;
        StreamWriter OpenWriter()=>new(new FileStream(current,FileMode.Create,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete),new UTF8Encoding(false)){AutoFlush=true};
        try{output=OpenWriter();await foreach(string line in owned.Reader.ReadAllAsync()){
            int count=Encoding.UTF8.GetByteCount(line)+Environment.NewLine.Length;if(bytes>0&&bytes+count>limit){await output.DisposeAsync();output=null;File.Delete(System.IO.Path.Combine(folder,"previous-3.jsonl"));for(int n=2;n>=1;n--){string from=System.IO.Path.Combine(folder,$"previous-{n}.jsonl");if(File.Exists(from))File.Move(from,System.IO.Path.Combine(folder,$"previous-{n+1}.jsonl"),true);}File.Move(current,System.IO.Path.Combine(folder,"previous-1.jsonl"),true);output=OpenWriter();bytes=0;}await output.WriteLineAsync(line);bytes+=count;
        }}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){lock(sync){error="Test log write failed: "+ex.Message;if(ReferenceEquals(queue,owned)){queue.Writer.TryComplete();queue=null;}}}finally{if(output!=null)await output.DisposeAsync();}
    }
    private static string Clean(string? text)=>new((text??"").Where(c=>!char.IsControl(c)).Take(300).ToArray());
}