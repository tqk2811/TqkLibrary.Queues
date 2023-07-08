using System;
using System.Threading.Tasks;

namespace TqkLibrary.Queues.TaskQueues
{
    /// <summary>
    /// 
    /// </summary>
    public interface IWork : IDisposable
    {
        /// <summary>
        /// Prioritize
        /// </summary>
        bool IsPrioritize { get; }

        /// <summary>
        /// Dont use <b>async void</b> inside<br/>
        /// </summary>
        /// <returns></returns>
        Task DoWork();

        /// <summary>
        /// 
        /// </summary>
        void Cancel();
    }
}