using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TqkLibrary.Queues.WaitQueues
{
    /// <summary>
    /// 
    /// </summary>
    public interface IWaitHandle : IDisposable
    {

    }

    /// <summary>
    /// 
    /// </summary>
    public class WaitQueue
    {
        readonly AsyncLock asyncLock = new AsyncLock();
        readonly AsyncLock asyncLockAccess = new AsyncLock();

        int _currentAccess = 0;

        int _MaxAccess = 2;
        /// <summary>
        /// 
        /// </summary>
        public int MaxAccess
        {
            get { return _MaxAccess; }
            set { _MaxAccess = value; AccessChanged?.Invoke(); }
        }
        event Action AccessChanged;
        /// <summary>
        /// 
        /// </summary>
        public WaitQueue()
        {
        }
        private void _Increment()
        {
            Interlocked.Increment(ref _currentAccess);
        }
        private void _Decrement()
        {
            Interlocked.Decrement(ref _currentAccess);
            AccessChanged?.Invoke();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IWaitHandle> WaitLockAsync(CancellationToken cancellationToken = default)
        {
            using (var l = await asyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_currentAccess >= _MaxAccess)
                {
                    TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var register = cancellationToken.Register(() => tcs.TrySetCanceled());
                    Action action = () => tcs.TrySetResult(true);
                    try
                    {
                        AccessChanged += action;
                        if (_currentAccess >= _MaxAccess)
                        {
                            await tcs.Task.ConfigureAwait(false);
                        }
                        else
                        {
                            break;
                        }
                    }
                    finally
                    {
                        AccessChanged -= action;
                    }

                }
                return new WaitHandle(this);
            }
        }

        class WaitHandle : IWaitHandle
        {
            readonly WaitQueue waitQueue;
            public WaitHandle(WaitQueue waitQueue)
            {
                this.waitQueue = waitQueue ?? throw new ArgumentNullException(nameof(waitQueue));
                this.waitQueue._Increment();
            }
            public void Dispose()
            {
                waitQueue._Decrement();
            }
        }
    }
}
