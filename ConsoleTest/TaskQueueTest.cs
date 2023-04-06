using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TqkLibrary.Queues.TaskQueues;
using TqkLibrary.Queues.WaitQueues;

namespace ConsoleTest
{
    static class TaskQueueTest
    {
        static readonly TaskQueue<JobQueue> taskQueue = new TaskQueue<JobQueue>();
        public static void Test()
        {
            taskQueue.OnQueueComplete += TaskQueue_OnQueueComplete;
            taskQueue.OnRunComplete += TaskQueue_OnRunComplete;
            taskQueue.RunRandom = false;//true: ngẫu nhiên trong hàng chờ, không theo thứ tự trước sau.
            taskQueue.UseAsyncContext = true;//true: use Nito AsyncContext
            taskQueue.TaskScheduler = TaskScheduler.Default;
            for (int i = 0; i < 100; i++) taskQueue.Add(new JobQueue($"Job_{i:000}"));
            taskQueue.MaxRun = 10;//số lượng luồng tối đa
            //taskQueue.SetRunLockObject(x => x.IsPrioritize);
            Console.ReadLine();
            taskQueue.ShutDown();
            Console.ReadLine();
        }

        private static void TaskQueue_OnRunComplete()
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} All Run Completed");
        }

        private static void TaskQueue_OnQueueComplete(Task task, QueueEventArgs<JobQueue> queue)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {nameof(JobQueue)} {queue.Queue.JobData} Completed, Exception:{task.Exception}");
        }
    }





    class JobQueue : IQueue
    {
        static readonly WaitQueue waitQueue = new WaitQueue() { MaxAccess = 2 };
        static readonly Random random = new Random();
        readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        public readonly string JobData;
        public JobQueue(string jobData)
        {
            this.JobData = jobData;
        }

        //Ưu tiên chạy ngay lập tức sau khi add vào TaskQueue<T>
        //Start when add to TaskQueue<T>
        public bool IsPrioritize { get; private set; } = false;

        public void Cancel()
        {
            cancellationTokenSource.Cancel();
        }

        public void Dispose()
        {
            cancellationTokenSource.Dispose();
        }

        static int count = 0;
        public async Task DoWork()
        {
            try
            {
                //if (random.NextDouble() > 0.9)
                //    throw new Exception();

                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId:0000} {DateTime.Now:HH:mm:ss} {JobData} Start WaitLockAsync");
                using (var w = await waitQueue.WaitLockAsync())
                {
                    count++;
                    Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId:0000} {DateTime.Now:HH:mm:ss} {JobData} Entered WaitLockAsync ({count})");
                    await Task.Delay(random.Next(1000, 2000), cancellationTokenSource.Token);
                    count--;
                }
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId:0000} {DateTime.Now:HH:mm:ss} {JobData} Exited WaitLockAsync");

            }
            catch (Exception)
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId:0000} {DateTime.Now:HH:mm:ss} {JobData} Exception");
            }
            finally
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId:0000} {DateTime.Now:HH:mm:ss} {JobData} Stop");
            }
        }
    }
}
