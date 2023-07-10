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
    /// <typeparam name="T"></typeparam>
    /// <param name="task"></param>
    /// <param name="workEventArgs"></param>

    public delegate Task WorkComplete<T>(Task task, WorkEventArgs<T> workEventArgs) where T : IWork;
    /// <summary>
    /// 
    /// </summary>

    public delegate void RunComplete();

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class WorkQueue<T> where T : IWork
    {
        private readonly HashSet<T> _Queues = new HashSet<T>();
        private readonly object _lock_queues = new object();

        private readonly HashSet<T> _Runnings = new HashSet<T>();
        private readonly object _lock_runnings = new object();

        private readonly Random _random = new Random();



        /// <summary>
        /// 
        /// </summary>
        public event RunComplete OnRunComplete;
        /// <summary>
        /// 
        /// </summary>
        public event WorkComplete<T> OnWorkComplete;

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
                    RunNewWorkAsync();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsRunning { get { return RunningCount != 0 || QueueCount != 0; } }

        /// <summary>
        /// 
        /// </summary>
        public IReadOnlyCollection<T> Queues { get { return _Queues; } }

        /// <summary>
        /// 
        /// </summary>
        public IReadOnlyCollection<T> Runnings { get { return _Runnings; } }

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
        private bool StartQueue(T queue)
        {
            if (!Monitor.TryEnter(_lock_queues))
            {
                return false;
            }
            if (queue is not null)
            {
                if (CheckIsLockObject(queue)) return false;

                _Queues.Remove(queue);
                lock (_lock_runnings) _Runnings.Add(queue);

                if (UseAsyncContext)
                {
                    Task.Factory.StartNew(
                        () => AsyncContext.Run(async () => await queue.DoWork().ContinueWith(this.ContinueTaskResult, queue, TaskContinuationOptions.ExecuteSynchronously)),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        this.TaskScheduler);
                    return true;
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
                    return true;
                }
            }
            return false;
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
                lock (_lock_runnings)
                {
                    return _Runnings.Any(x => obj_lock.Equals(_RunLockObject.Invoke(x)));
                }
            }
            return false;
        }
        private Task RunNewWorkAsync() => Task.Run(RunNewWork);
        private void RunNewWork()
        {
            int skip = 0;
            lock (_lock_queues)//Prioritize
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
                lock (_lock_queues)
                {
                    T queue = default(T);
                    if (RunRandom) queue = _Queues.Count == 0 ? default(T) : _Queues.Skip(_random.Next(_Queues.Count)).FirstOrDefault();
                    else queue = _Queues.FirstOrDefault();
                    if (!StartQueue(queue)) skip++;
                }
                if (_Queues.Count > 0 && (_Runnings.Count + skip) < MaxRun) RunNewWorkAsync();
            }
        }

        private void ContinueTaskResult(Task result, object work_obj) => WorkCompleted(result, (T)work_obj);

        private async void WorkCompleted(Task result, T work)
        {
            var queueEventArg = new WorkEventArgs<T>(work);

            Task task = OnWorkComplete?.Invoke(result, queueEventArg);
            if (task is not null) await task;

            if (queueEventArg.ShouldDispose) work.Dispose();

            lock (_lock_runnings) _Runnings.Remove(work);

            if (QueueCount > 0) _ = RunNewWorkAsync();//much run on threadpool
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="work"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public bool Add(T work)
        {
            if (work is null) throw new ArgumentNullException(nameof(work));
            lock (_lock_queues)
            {
                if (_Queues.Add(work))
                {
                    RunNewWorkAsync();
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Items was not add</returns>
        /// <param name="works"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public IReadOnlyList<T> AddRange(IEnumerable<T> works)
        {
            if (null == works) throw new ArgumentNullException(nameof(works));
            int addCount = 0;
            List<T> result = new List<T>();
            lock (_lock_queues)
            {
                foreach (var queue in works)
                {
                    if (_Queues.Add(queue))
                    {
                        addCount++;
                    }
                    else
                    {
                        result.Add(queue);
                    }
                }
            }
            if (addCount > 0) RunNewWorkAsync();
            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="work"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public bool Cancel(T work)
        {
            if (null == work) throw new ArgumentNullException(nameof(work));
            lock (_lock_queues)
            {
                if (_Queues.Remove(work))
                {
                    return true;
                }
            }
            lock (_lock_runnings)
            {
                if (_Runnings.Contains(work))
                {
                    work.TaskCancel();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="func"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns>Items removed in <see cref="Queues"/></returns>
        public IReadOnlyList<T> Cancel(Func<T, bool> func)
        {
            if (null == func) throw new ArgumentNullException(nameof(func));
            List<T> removes = new List<T>();
            lock (_lock_queues)
            {
                foreach (var q in _Queues.Where(func))
                {
                    q.TaskDispose();
                    removes.Add(q);
                }
                removes.ForEach(x => _Queues.Remove(x));
            }
            lock (_lock_runnings)
            {
                foreach (var q in _Runnings.Where(func))
                {
                    q.TaskCancel();
                }
            }
            return removes;
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
        public void ShutDown(int maxRun = 0)
        {
            MaxRun = maxRun;
            lock (_lock_queues)
            {
                foreach (var queue in _Queues) queue.TaskDispose();
                _Queues.Clear();
            }
            lock (_lock_runnings)
            {
                foreach (var running in _Runnings) running.TaskCancel();
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