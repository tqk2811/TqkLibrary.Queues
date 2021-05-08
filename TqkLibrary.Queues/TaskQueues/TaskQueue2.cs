//using System;
//using System.Threading;
//using System.Threading.Tasks;

//namespace TqkLibrary.Queues.TaskQueues
//{
//  public class TaskQueue2
//  {
//    private SemaphoreSlim semaphore;

//    public TaskQueue2(int numQueue = 1)
//    {
//      semaphore = new SemaphoreSlim(numQueue);
//    }

//    public async Task<T> Enqueue<T>(Func<Task<T>> taskGenerator)
//    {
//      await semaphore.WaitAsync();
//      try
//      {
//        return await taskGenerator();
//      }
//      finally
//      {
//        semaphore.Release();
//      }
//    }

//    public async Task Enqueue(Func<Task> taskGenerator)
//    {
//      await semaphore.WaitAsync();
//      try
//      {
//        await taskGenerator();
//      }
//      finally
//      {
//        semaphore.Release();
//      }
//    }
//  }
//}