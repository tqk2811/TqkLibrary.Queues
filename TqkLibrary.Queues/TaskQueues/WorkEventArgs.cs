using System;

namespace TqkLibrary.Queues.TaskQueues
{
    /// <summary>
    /// 
    /// </summary>
    public class WorkEventArgs<T> : EventArgs
    {
        internal WorkEventArgs(T work)
        {
            this.Work = work ?? throw new ArgumentNullException(nameof(work));
        }
        /// <summary>
        /// 
        /// </summary>
        public T Work { get; }
        /// <summary>
        /// Default true
        /// </summary>
        public bool ShouldDispose { get; set; } = true;
    }
}