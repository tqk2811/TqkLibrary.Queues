using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TqkLibrary.Queues.TaskQueues;

namespace ConsoleTest
{
  static class TaskQueueTest
  {
    static readonly TaskQueue<JobQueue> taskQueue = new TaskQueue<JobQueue>();
    public static void Test()
    {
      taskQueue.OnQueueComplete += TaskQueue_OnQueueComplete;
      taskQueue.OnQueueNextParty += TaskQueue_OnQueueNextParty;
      taskQueue.OnRunComplete += TaskQueue_OnRunComplete;
      for (int i = 0; i < 500; i++) taskQueue.Add(new JobQueue($"Job_{i:000}"));
      taskQueue.MaxRun = 10;
      Console.ReadLine();
    }

    private static void TaskQueue_OnRunComplete(bool isRequeue)
    {
      Console.WriteLine($"{DateTime.Now:HH:mm:ss} All Run Completed, Is ReRun: {isRequeue}");
    }

    private static void TaskQueue_OnQueueNextParty()
    {
      Console.WriteLine($"{DateTime.Now:HH:mm:ss} Party (Max {taskQueue.MaxRun} threads) Completed, Start Run Next Party (Max {taskQueue.MaxRun} threads)");
    }

    private static void TaskQueue_OnQueueComplete(Task task, JobQueue queue)
    {
      Console.WriteLine($"{DateTime.Now:HH:mm:ss} {nameof(JobQueue)} Completed");
    }
  }





  class JobQueue : IQueue
  {
    static readonly Random random = new Random();
    readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public readonly string JobData;
    public JobQueue(string jobData)
    {
      this.JobData = jobData;
    }
    public bool IsPrioritize { get; private set; } = false;

    public bool ReQueue { get; private set; } = false;

    public bool ReQueueAfterRunComplete { get; private set; } = false;

    public void Cancel()
    {
      cancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
      cancellationTokenSource.Dispose();
    }

    public async Task DoWork()
    {
      try
      {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {JobData} Start");
        await Task.Delay(random.Next(1000, 8000), cancellationTokenSource.Token);
      }
      finally
      {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {JobData} Stop");
      }
    }
  }
}
