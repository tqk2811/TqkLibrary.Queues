using Nito.AsyncEx;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace TqkLibrary.Queues.TaskQueues
{
    /// <summary>
    /// 
    /// </summary>
    public interface IQueue : IDisposable
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

    /// <summary>
    /// 
    /// </summary>
    public class QueueEventArgs<T> : EventArgs
    {
        internal QueueEventArgs(T queue)
        {
            this.Queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }
        /// <summary>
        /// 
        /// </summary>
        public T Queue { get; }
        /// <summary>
        /// Default true
        /// </summary>
        public bool ShouldDispose { get; set; } = true;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="task"></param>
    /// <param name="queue"></param>

    public delegate Task QueueComplete<T>(Task task, QueueEventArgs<T> queue) where T : IQueue;
    /// <summary>
    /// 
    /// </summary>

    public delegate void RunComplete();

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TaskQueue<T> where T : IQueue
    {
        private readonly List<T> _Queues = new List<T>();
        private readonly List<T> _Runnings = new List<T>();
        /// <summary>
        /// 
        /// </summary>
        public event RunComplete OnRunComplete;
        /// <summary>
        /// 
        /// </summary>
        public event QueueComplete<T> OnQueueComplete;

        private int _MaxRun = 0;

        /// <summary>
        /// 
        /// </summary>
        public int MaxRun
        {
            get { return _MaxRun; }
            set
            {
                bool flag = value > _MaxRun;
                _MaxRun = value;
                if (flag && _Queues.Count != 0)
                    RunNewQueue();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsRunning { get { return RunningCount != 0 || QueueCount != 0; } }

        /// <summary>
        /// 
        /// </summary>
        public List<T> Queues { get { return _Queues.ToList(); } }

        /// <summary>
        /// 
        /// </summary>
        public List<T> Runnings { get { return _Runnings.ToList(); } }

        /// <summary>
        /// 
        /// </summary>
        public int RunningCount
        {
            get { return _Runnings.Count; }
        }

        /// <summary>
        /// 
        /// </summary>
        public int QueueCount
        {
            get { return _Queues.Count; }
        }

        /// <summary>
        /// Non FIFO run
        /// </summary>
        public bool RunRandom { get; set; } = false;

        /// <summary>
        /// 
        /// </summary>
        public TaskScheduler TaskScheduler { get; set; } = TaskScheduler.Default;

        /// <summary>
        /// if true use <see cref="AsyncContext"/> (single thread for asynchronous), default true
        /// </summary>
        public bool UseAsyncContext { get; set; } = true;

        //need lock Queues first
        private int StartQueue(T queue)
        {
            if (queue is not null)
            {
                if (CheckIsLockObject(queue)) return 1;

                _Queues.Remove(queue);
                lock (_Runnings) _Runnings.Add(queue);
                if (UseAsyncContext)
                {
                    Task.Factory.StartNew(
                        () => AsyncContext.Run(async () => await queue.DoWork().ContinueWith(this.ContinueTaskResult, queue, TaskContinuationOptions.ExecuteSynchronously)),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        this.TaskScheduler);
                }
                else
                {
                    Task.Factory.StartNew(
                        () => queue.DoWork(),
                        CancellationToken.None,
                        TaskCreationOptions.None,
                        this.TaskScheduler)
                    .Unwrap()
                    .ContinueWith(this.ContinueTaskResult, queue, TaskContinuationOptions.RunContinuationsAsynchronously);
                }
            }
            return 0;
        }

        /// <summary>
        /// Lock <see cref="_Runnings"/>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        bool CheckIsLockObject(T item)
        {
            var obj_lock = _RunLockObject.Invoke(item);
            if (obj_lock != null)
            {
                lock (_Runnings)
                {
                    return _Runnings.Any(x => obj_lock.Equals(_RunLockObject.Invoke(x)));
                }
            }
            return false;
        }

        private void RunNewQueue()
        {
            int skip = 0;
            lock (_Queues)//Prioritize
            {
                var Prioritizes = _Queues.Where(x => x.IsPrioritize).ToList();
                foreach (var q in Prioritizes) StartQueue(q);
            }

            if (_Queues.Count == 0 && _Runnings.Count == 0)
            {
                OnRunComplete?.Invoke();//on completed
                return;
            }

            if (_Runnings.Count >= MaxRun) return;//other
            else
            {
                lock (_Queues)
                {
                    T queue;
                    if (RunRandom) queue = _Queues.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                    else queue = _Queues.FirstOrDefault();
                    skip += StartQueue(queue);
                }
                if (_Queues.Count > 0 && (_Runnings.Count + skip) < MaxRun) Task.Run(RunNewQueue);
            }
        }

        private void ContinueTaskResult(Task result, object queue_obj) => QueueCompleted(result, (T)queue_obj);

        private async void QueueCompleted(Task result, T queue)
        {
            var queueEventArg = new QueueEventArgs<T>(queue);
            await OnQueueComplete?.Invoke(result, queueEventArg);
            if (queueEventArg.ShouldDispose) queue.Dispose();

            lock (_Runnings) _Runnings.Remove(queue);

            _ = Task.Run(RunNewQueue);//much run on threadpool
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queue"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Add(T queue)
        {
            if (null == queue) throw new ArgumentNullException(nameof(queue));
            lock (_Queues) _Queues.Add(queue);
            RunNewQueue();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queues"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddRange(IEnumerable<T> queues)
        {
            if (null == queues) throw new ArgumentNullException(nameof(queues));
            lock (_Queues) foreach (var queue in queues) _Queues.Add(queue);
            RunNewQueue();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queue"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Cancel(T queue)
        {
            if (null == queue) throw new ArgumentNullException(nameof(queue));
            lock (_Queues) _Queues.RemoveAll(o => o.Equals(queue));
            lock (_Runnings) foreach (var q in _Runnings.Where(x => x.Equals(queue))) q.Cancel();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="func"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Cancel(Func<T, bool> func)
        {
            if (null == func) throw new ArgumentNullException(nameof(func));
            lock (_Queues)
            {
                List<T> removes = new List<T>();
                foreach (var q in _Queues.Where(x => func(x)))
                {
                    q.Dispose();
                    removes.Add(q);
                }
                removes.ForEach(x => _Queues.Remove(x));
            }
            lock (_Runnings)
            {
                foreach (var q in _Runnings.Where(func))
                {
                    q.Cancel();
                    //_Runnings.Remove(q);
                }
            }
        }

        Func<T, object> _RunLockObject = (t) => null;
        /// <summary>
        /// Example: object1 + IQueue1<br></br>
        /// object1 + IQueue2<br></br>
        /// object2 + IQueue3<br></br>
        /// MaxRun=3<br></br>
        /// Then IQueue2 will wait IQueue1 done
        /// </summary>
        /// <param name="func">if return object and equal then wait.<br></br>Warning: Don't access TaskQueue from here</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void SetRunLockObject(Func<T, object> func)
        {
            if (func is null) throw new ArgumentNullException(nameof(func));
            _RunLockObject = func;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queue"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Reset(T queue)
        {
            if (queue is null) throw new ArgumentNullException(nameof(queue));
            Cancel(queue);
            Add(queue);
        }

        /// <summary>
        /// 
        /// </summary>
        public void ShutDown()
        {
            MaxRun = 0;
            lock (_Queues)
            {
                _Queues.ForEach(o => o.Dispose());
                _Queues.Clear();
            }
            lock (_Runnings)
            {
                _Runnings.ForEach(o => o.Cancel());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeOut"></param>
        public bool WaitForShutDown(int timeOut = -1)
            => WaitForShutDownAsync(timeOut).ConfigureAwait(false).GetAwaiter().GetResult();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeOut"></param>
        /// <returns></returns>
        public async Task<bool> WaitForShutDownAsync(int timeOut = -1)
        {
            if (RunningCount > 0)
            {
                TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(timeOut);
                using var register = cancellationTokenSource.Token.Register(() => taskCompletionSource.TrySetResult(false));
                RunComplete runComplete = () => taskCompletionSource.TrySetResult(true);
                try
                {
                    this.OnRunComplete += runComplete;
                    await taskCompletionSource.Task.ConfigureAwait(false);
                }
                finally
                {
                    this.OnRunComplete -= runComplete;
                }
                return RunningCount == 0;
            }
            else return true;
        }
    }
}