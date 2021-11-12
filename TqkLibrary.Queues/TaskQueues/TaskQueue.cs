using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TqkLibrary.Queues.TaskQueues
{
  public interface IQueue : IDisposable
  {
    bool IsPrioritize { get; }
    bool ReQueue { get; }
    bool ReQueueAfterRunComplete { get; }

    /// <summary>
    /// Dont use <b>async void</b> inside<br/>
    /// Use Task.Start(function_return_void) or async Task 
    /// </summary>
    /// <returns></returns>
    Task DoWork();

    void Cancel();
  }

  public delegate void QueueComplete<T>(Task task, T queue) where T : IQueue;

  public delegate void RunComplete(bool isRequeue);

  public delegate void QueueNextParty();

  public delegate void TaskException<T>(AggregateException ae,T queue) where T : IQueue;

  public class TaskQueue<T> where T : IQueue
  {
    private readonly List<T> _Queues = new List<T>();
    private readonly List<T> _Runnings = new List<T>();
    private readonly List<T> _ReQueues = new List<T>();
    private readonly ManualResetEvent manualResetEvent = new ManualResetEvent(false);

    public event RunComplete OnRunComplete;
    public event QueueComplete<T> OnQueueComplete;
    public event QueueNextParty OnQueueNextParty;

    private int _MaxRun = 0;
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

    public bool IsRunning { get { return RunningCount != 0 || QueueCount != 0 || ReQueues.Count != 0; } }

    public List<T> Queues { get { return _Queues.ToList(); } }
    public List<T> Runnings { get { return _Runnings.ToList(); } }
    public List<T> ReQueues { get { return _ReQueues.ToList(); } }

    public int RunningCount
    {
      get { return _Runnings.Count; }
    }

    public int QueueCount
    {
      get { return _Queues.Count; }
    }
    public int ReQueueCount
    {
      get { return _ReQueues.Count; }
    }

    public bool RunRandom { get; set; } = false;

    /// <summary>
    /// Ex: Add 10 items, MaxRun = 2. Then 2 next threads will run after 2 threads end
    /// </summary>
    public bool RunAsParty { get; set; } = false;


    //need lock Queues first
    private void StartQueue(T queue)
    {
      if (null != queue)
      {
        _Queues.Remove(queue);
        lock (_Runnings) _Runnings.Add(queue);
        Task.Factory.StartNew(() => queue.DoWork().Wait(),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default)
          .ContinueWith(ContinueTaskResult, queue);
      }
    }

    private void RunNewQueue()
    {
      lock (_Queues)//Prioritize
      {
        var Prioritizes = _Queues.Where(x => x.IsPrioritize).ToList();
        foreach (var q in Prioritizes) StartQueue(q);
      }

      if (_Queues.Count == 0 && _Runnings.Count == 0)
      {
        OnRunComplete?.Invoke(_ReQueues.Count > 0);//on completed
        if (_ReQueues.Count > 0)
        {
          lock (_Queues) _Queues.AddRange(_ReQueues);
          lock (_ReQueues) _ReQueues.Clear();
          RunNewQueue();
        }
        else manualResetEvent.Set();
        return;
      }
      else manualResetEvent.Reset();

      if (_Runnings.Count >= MaxRun) return;//other
      else
      {
        lock (_Queues)
        {
          T queue;
          if (RunRandom) queue = _Queues.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
          else queue = _Queues.FirstOrDefault();
          StartQueue(queue);
        }
        if (_Queues.Count > 0 && _Runnings.Count < MaxRun) RunNewQueue();
      }
    }

    private void ContinueTaskResult(Task result, object queue_obj) => QueueCompleted(result, (T)queue_obj);

    private void QueueCompleted(Task result, T queue)
    {
      if (queue.ReQueue)
      {
        lock (_Queues)
        {
          if (queue.IsPrioritize)
          {
            StartQueue(queue);
          }
          else if(_Queues.IndexOf(queue) == -1) _Queues.Add(queue);
        }
      }
      if (queue.ReQueueAfterRunComplete && _ReQueues.IndexOf(queue) == -1) lock (_ReQueues) _ReQueues.Add(queue);
      OnQueueComplete?.Invoke(result, queue);
      if (!queue.ReQueue && !queue.ReQueueAfterRunComplete) lock (_Runnings) queue.Dispose();

      lock (_Runnings)
      {
        _Runnings.Remove(queue);
        if (RunAsParty && (RunningCount > 0 || MaxRun == 0)) return;
      }
      if (RunAsParty) OnQueueNextParty?.Invoke();
      RunNewQueue();
    }

    //public void RunGroup<TGroup>(Func<T,TGroup> func)
    //{
    //  //_Queues.gro
    //}

    public void Add(T queue)
    {
      if (null == queue) throw new ArgumentNullException(nameof(queue));
      lock (_Queues) _Queues.Add(queue);
      RunNewQueue();
    }

    public void AddRange(IEnumerable<T> queues)
    {
      if (null == queues) throw new ArgumentNullException(nameof(queues));
      lock (_Queues) foreach (var queue in queues) _Queues.Add(queue);
      RunNewQueue();
    }

    public void Cancel(T queue)
    {
      if (null == queue) throw new ArgumentNullException(nameof(queue));
      lock (_Queues) _Queues.RemoveAll(o => o.Equals(queue));
      lock (_Runnings) foreach (var q in _Runnings.Where(x => x.Equals(queue))) q.Cancel();
    }

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
        foreach(var q in _Runnings.Where(func))
        {
          q.Cancel();
          //_Runnings.Remove(q);
        }
      }
    }

    public void Reset(T queue)
    {
      if (null == queue) throw new ArgumentNullException(nameof(queue));
      Cancel(queue);
      Add(queue);
    }

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
      lock (_ReQueues) _ReQueues.Clear(); 
    }

    public void WaitForShutDown(int timeOut = -1)
    {
      if (RunningCount > 0)
      {
        manualResetEvent.WaitOne(timeOut);
      }
    }

    public Task WaitForShutDownAsync(int timeOut = -1) 
      => Task.Run(() =>WaitForShutDown(timeOut));
  }
}