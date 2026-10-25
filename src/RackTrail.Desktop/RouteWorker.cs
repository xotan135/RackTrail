using Avalonia.Threading;
using System.Diagnostics;
using RackTrail.Core;

namespace RackTrail.Desktop;

/// <summary>One worker, one replaceable pending request. Live models never cross the thread boundary.</summary>
internal sealed class RouteWorker : IDisposable
{
    internal sealed record Result(long Revision, RoutingSnapshot Routes, Dictionary<string, ConnectionCaption> Captions,double RoutingMilliseconds,long AllocatedBytes,bool CaptionsOnly);
    private readonly Action<Result> completed;
    private readonly Action<string> failed;
    private readonly object sync = new();
    private CancellationTokenSource? active;
    private sealed record RequestData(long Revision,Project Copy,bool Labels,RoutingSnapshot? Existing=null,string[]? CaptionIds=null,VisualBox[]? Occupied=null);
    private RequestData? pending;
    private long latest;
    private bool running, disposed;
    internal Task Completion {get;private set;}=Task.CompletedTask;
    public RouteWorker(Action<Result> completed, Action<string> failed) { this.completed = completed; this.failed = failed; }
    public void Cancel()
    {
        lock (sync) { latest++; pending = null; active?.Cancel(); }
    }
    public void Request(long revision,Project ownedCopy,bool labels)=>Enqueue(new(revision,ownedCopy,labels));
    public void RequestCaptions(long revision,Project ownedCopy,RoutingSnapshot routes,string[] ids,VisualBox[] occupied)=>Enqueue(new(revision,ownedCopy,false,routes,ids,occupied));
    private void Enqueue(RequestData request)
    {
        lock(sync)
        {
            if(disposed)return;
            latest++;pending=request;active?.Cancel();
            if(running)return;running=true;
        }
        Completion=Task.Run(Process);
    }
    private void Process()
    {
        while (true)
        {
            RequestData request; long ticket; CancellationTokenSource cancellation;
            lock (sync)
            {
                if (disposed || pending == null) { running = false; return; }
                request = pending; pending = null; ticket = latest;
                active = cancellation = new CancellationTokenSource();
            }
            try
            {
                long started=Stopwatch.GetTimestamp(),allocated=GC.GetAllocatedBytesForCurrentThread();
                var routes = request.Existing??ConnectionRouter.Compute(request.Copy, cancellation.Token);
                var captions = ConnectionCaptions.Create(request.Copy,
                    request.Copy.Connections.Where(c => request.CaptionIds==null?ConnectionRouter.ShowLabel(request.Copy):request.CaptionIds.Contains(c.Id,StringComparer.Ordinal)), routes, cancellation.Token,request.Occupied);
                cancellation.Token.ThrowIfCancellationRequested();
                double milliseconds=Stopwatch.GetElapsedTime(started).TotalMilliseconds;long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
                Dispatcher.UIThread.Post(() =>
                {
                    lock (sync) { if (disposed || ticket != latest) return; }
                    completed(new(request.Revision, routes, captions,milliseconds,bytes,request.Existing!=null));
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => { lock (sync) { if (disposed || ticket != latest) return; } failed(ex.Message); });
            }
            finally
            {
                lock (sync) { if (ReferenceEquals(active, cancellation)) active = null; }
                cancellation.Dispose();
            }
        }
    }
    public void Dispose() { lock (sync) { disposed = true; latest++; pending = null; active?.Cancel(); } }
}
