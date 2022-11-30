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
            taskQueue.RunRandom = false;//true: ngẫu nhiên trong hàng chờ, không theo thứ tự trước sau.
            taskQueue.UseAsyncContext = true;//true: use Nito AsyncContext
            taskQueue.TaskScheduler = TaskScheduler.Default;
            for (int i = 0; i < 20; i++) taskQueue.Add(new JobQueue($"Job_{i:000}"));
            taskQueue.MaxRun = 5;
            taskQueue.RunAsParty = false;
            Console.ReadLine();
            taskQueue.ShutDown();
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
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {nameof(JobQueue)} Completed, Requeue: {queue.ReQueue}, Exception:{task.Exception}");
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

        //Ưu tiên chạy ngay lập tức sau khi add vào TaskQueue<T>
        //Start when add to TaskQueue<T>
        public bool IsPrioritize { get; private set; } = false;

        //If true, This job will re-add to taskqueue after this job completed.
        //Nếu true, Job này sẽ tự thêm lại vào TaskQueue<T> và xếp hàng chờ chạy lại.
        public bool ReQueue { get; private set; } = false;

        //if true, when all queues completed. This job will re-add to TaskQueue<T>
        //nếu true, khi tất cả các queue chạy xong. Sẽ thêm vào TaskQueue<T>
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
                ReQueue = random.NextDouble() <= 0.10;//random requeue 10%;. 
                ReQueueAfterRunComplete = false;

                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId:0000} {DateTime.Now:HH:mm:ss} {JobData} Start");
                await Task.Delay(random.Next(1000, 8000), cancellationTokenSource.Token);

            }
            finally
            {
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId:0000} {DateTime.Now:HH:mm:ss} {JobData} Stop");
            }
        }
    }
}
