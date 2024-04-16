using System.Threading;
using System.Threading.Tasks;

namespace TqkLibrary.Queues.TaskQueues
{
    /// <summary>
    /// 
    /// </summary>
    public abstract class BaseWork : IWork
    {
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        /// <summary>
        /// 
        /// </summary>
        protected CancellationToken CancellationToken { get { return _cancellationTokenSource.Token; } }

        /// <summary>
        /// 
        /// </summary>
        public virtual bool IsPrioritize { get; set; } = false;

        /// <summary>
        /// 
        /// </summary>
        public virtual void Cancel()
        {
            _cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// 
        /// </summary>
        public virtual void Dispose()
        {
            _cancellationTokenSource.Dispose();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public abstract Task DoWorkAsync();
    }
}