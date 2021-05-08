using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TqkLibrary.Queues.TaskQueues
{
  public interface IQueue : IDisposable
  {
    bool IsPrioritize { get; }
    bool ReQueue { get; }

    /// <summary>
    /// Dont use async void inside
    /// </summary>
    /// <returns></returns>
    Task DoWork();

    bool CheckEquals(IQueue queue);

    void Cancel();
  }

  public delegate void QueueComplete<T>(Task task, T queue) where T : IQueue;

  public delegate void RunComplete();

  public class TaskQueue<T> where T : IQueue
  {
    private readonly List<T> _Queues = new List<T>();
    private readonly List<T> _Runnings = new List<T>();

    [Browsable(false), DefaultValue((string)null)]
    public Dispatcher Dispatcher { get; set; }

    public event RunComplete OnRunComplete;

    public event QueueComplete<T> OnQueueComplete;

    private int _MaxRun = 0;

    public int MaxRun
    {
      get { return _MaxRun; }
      set
      {
        bool flag = value > _MaxRun;
        _MaxRun = value;
        if (flag && _Queues.Count != 0) RunNewQueue();
      }
    }

    public bool IsRunning { get { return RunningCount != 0 || QueueCount != 0; } }

    public List<T> Queues { get { return _Queues.ToList(); } }
    public List<T> Runnings { get { return _Runnings.ToList(); } }

    public int RunningCount
    {
      get { return _Runnings.Count; }
    }

    public int QueueCount
    {
      get { return _Queues.Count; }
    }

    public bool RunRandom { get; set; } = false;

    //need lock Queues first
    private void StartQueue(T queue)
    {
      if (null != queue)
      {
        _Queues.Remove(queue);
        lock (_Runnings) _Runnings.Add(queue);
        queue.DoWork().ContinueWith(ContinueTaskResult, queue);
      }
    }

    private void RunNewQueue()
    {
      lock (_Queues)//Prioritize
      {
        foreach (var q in _Queues.Where(x => x.IsPrioritize)) StartQueue(q);
      }

      if (_Queues.Count == 0 && _Runnings.Count == 0 && OnRunComplete != null)
      {
        if (Dispatcher != null && !Dispatcher.CheckAccess()) Dispatcher.Invoke(OnRunComplete);
        else OnRunComplete.Invoke();//on completed
        autoResetEvent.Set();
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
          StartQueue(queue);
        }
        if (_Queues.Count > 0 && _Runnings.Count < MaxRun) RunNewQueue();
      }
    }

    private void ContinueTaskResult(Task result, object queue_obj) => QueueCompleted(result, (T)queue_obj);

    private void QueueCompleted(Task result, T queue)
    {
      try
      {
        if (queue.ReQueue) lock (_Queues) _Queues.Add(queue);
        if (OnQueueComplete != null)
        {
          if (Dispatcher != null && !Dispatcher.CheckAccess()) Dispatcher.Invoke(OnQueueComplete, new object[] { result, queue });
          else OnQueueComplete.Invoke(result, queue);
        }
        if (!queue.ReQueue) queue.Dispose();
      }
      finally
      {
        lock (_Runnings) _Runnings.Remove(queue);
        RunNewQueue();
      }
    }

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
      lock (_Queues) _Queues.RemoveAll(o => o.CheckEquals(queue));
      lock (_Runnings) _Runnings.ForEach(o => { if (o.CheckEquals(queue)) o.Cancel(); });
    }

    public void Cancel(Func<T, bool> func)
    {
      if (null == func) throw new ArgumentNullException(nameof(func));
      lock (_Queues)
      {
        _Queues.Where(func).ToList().ForEach(x => _Queues.RemoveAll(o => o.CheckEquals(x)));
      }
      lock (_Runnings)
      {
        _Runnings.Where(func).ToList().ForEach(x =>
        {
          x.Cancel();
          _Runnings.RemoveAll(o => o.CheckEquals(x));
        });
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
      lock (_Runnings) _Runnings.ForEach(o => o.Cancel());
    }

    private readonly AutoResetEvent autoResetEvent = new AutoResetEvent(false);

    public void WaitForShutDown()
    {
      WaitForShutDown(TimeSpan.MaxValue);
    }

    public void WaitForShutDown(TimeSpan Timeout)
    {
      if (RunningCount > 0)
      {
        autoResetEvent.Reset();
        autoResetEvent.WaitOne(Timeout);
      }
    }

    public Task WaitForShutDownAsync() => Task.Factory.StartNew(WaitForShutDown);

    public Task WaitForShutDownAsync(TimeSpan Timeout) => Task.Factory.StartNew(() => WaitForShutDown(Timeout));
  }
}