using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TqkLibrary.Queues.WaitQueues
{
  public delegate void SetStatusFreeDelegate<T>(T sender);

  public interface IWait
  {
    event SetStatusFreeDelegate<IWait> Free;
  }

  //max 64 wait
  public class WaitQueue<T> where T : IWait
  {
    private readonly List<Tuple<T, AutoResetEvent>> waits = new List<Tuple<T, AutoResetEvent>>();

    /// <summary>
    /// warning: dont add when something is waiting
    /// </summary>
    /// <param name="wait"></param>
    public void Add(T wait)
    {
      if (null == wait) throw new ArgumentNullException(nameof(wait));
      if (waits.Count >= 64) throw new Exception("Limit at 64 wait");
      wait.Free += Wait_Free;
      waits.Add(new Tuple<T, AutoResetEvent>(wait, new AutoResetEvent(false)));
    }

    private void Wait_Free(IWait sender)
    {
      waits.Where(x => x.Item1.Equals(sender)).FirstOrDefault()?.Item2.Set();
    }

    /// <summary>
    /// warning: dont remove when something is waiting
    /// </summary>
    /// <param name="wait"></param>
    public void Remove(T wait)
    {
      if (null == wait) throw new ArgumentNullException(nameof(wait));
      wait.Free -= Wait_Free;
      waits.Remove(waits.Where(x => x.Item1.Equals(wait)).FirstOrDefault());
    }

    public T WaitAny()
    {
      return WaitAny(TimeSpan.MaxValue);
    }

    public T WaitAny(TimeSpan timeSpan)
    {
      //autoResetEvent.WaitOne(TimeSpan.Zero) -> if set return true (free)
      //search free
      var con = waits.Where(x => x.Item2.WaitOne(TimeSpan.Zero)).OrderBy(x => Guid.NewGuid()).FirstOrDefault();
      if (null != con)
      {
        con.Item2.Reset();//busy mode
        return con.Item1;
      }
      else
      {
        //wait
        DateTime dateTime = DateTime.Now;
        if (Monitor.TryEnter(waits, timeSpan))//lock, only one wait
        {
          try
          {
            TimeSpan timespanleft = DateTime.Now - dateTime;//time left
            AutoResetEvent[] waitHandles = waits.Select(x => x.Item2).ToArray();
            int freeIndex = WaitHandle.WaitAny(waitHandles, timeSpan.Subtract(timespanleft));
            if (freeIndex == WaitHandle.WaitTimeout) return default;//timeout
            else
            {
              Tuple<T, AutoResetEvent> tuple = waits.Where(x => x.Item2.Equals(waitHandles[freeIndex])).FirstOrDefault();
              tuple.Item2.Reset();//busy mode
              return tuple.Item1;
            }
          }
          finally
          {
            Monitor.Exit(waits);
          }
        }
        else return default;//timeout
      }
    }
  }
}