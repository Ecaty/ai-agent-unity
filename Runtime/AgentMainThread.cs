using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ecaty.Agents
{
    public sealed class AgentMainThread
    {
        private readonly SynchronizationContext context = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the dispatcher on Unity's main thread.");
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        public Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested) return Task.FromCanceled<T>(cancellation);
            if (Thread.CurrentThread.ManagedThreadId == thread)
            { try { return Task.FromResult(action()); } catch (Exception e) { return Task.FromException<T>(e); } }
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellation.Register(() => completion.TrySetCanceled(cancellation));
            context.Post(_ => {
                try { if (cancellation.IsCancellationRequested) completion.TrySetCanceled(cancellation); else completion.TrySetResult(action()); }
                catch (Exception e) { completion.TrySetException(e); }
                finally { registration.Dispose(); }
            }, null);
            return completion.Task;
        }
    }
}
