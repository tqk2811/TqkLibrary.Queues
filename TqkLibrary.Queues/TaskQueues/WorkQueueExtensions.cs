using System.Threading.Tasks;

namespace TqkLibrary.Queues.TaskQueues
{
    internal static class WorkQueueExtensions
    {
        internal static Task TaskDispose(this IWork work)
        {
            if (work is not null) return Task.Run(work.Dispose);
            return Task.CompletedTask;
        }
        internal static Task TaskCancel(this IWork work)
        {
            if (work is not null) return Task.Run(work.Cancel);
            return Task.CompletedTask;
        }
    }
}