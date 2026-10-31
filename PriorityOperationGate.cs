namespace DontBlink.Services;

// Commands already in flight finish normally. Waiting foreground work wins the next turn.
internal sealed class PriorityOperationGate
{
    private readonly object sync=new();
    private bool held;
    private readonly LinkedList<Waiter> foreground=new(),background=new();
    private sealed class Waiter(CancellationToken token)
    {
        public readonly CancellationToken Token=token;
        public readonly TaskCompletionSource<bool> Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node;
    }
    public Task WaitAsync(CancellationToken ct=default)=>EnterAsync(false,ct);
    public Task WaitBackgroundAsync(CancellationToken ct)=>EnterAsync(true,ct);
    private async Task EnterAsync(bool low,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();Waiter waiter;
        lock(sync){if(!held){held=true;return;}waiter=new(ct);waiter.Node=(low?background:foreground).AddLast(waiter);}
        using var registration=ct.Register(()=>{lock(sync){if(waiter.Node?.List is {} list){list.Remove(waiter.Node);waiter.Node=null;waiter.Ready.TrySetCanceled(ct);}}});
        await waiter.Ready.Task.ConfigureAwait(false);
    }
    public void Release()
    {
        lock(sync){if(!held)throw new SemaphoreFullException();while(true){var queue=foreground.Count>0?foreground:background;if(queue.First is not {} node){held=false;return;}queue.RemoveFirst();var waiter=node.Value;waiter.Node=null;if(waiter.Token.IsCancellationRequested){waiter.Ready.TrySetCanceled(waiter.Token);continue;}waiter.Ready.TrySetResult(true);return;}}
    }
}