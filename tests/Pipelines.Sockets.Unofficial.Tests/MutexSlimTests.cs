﻿using Pipelines.Sockets.Unofficial.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static Pipelines.Sockets.Unofficial.Threading.MutexSlim;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class MutexSlimTests
    {
        private readonly MutexSlim _zeroTimeoutMux = new MutexSlim(0), _timeoutMux = new MutexSlim(1000, DedicatedThreadPoolPipeScheduler.Default);

        class DummySyncContext : SynchronizationContext
        {
            public Guid Id { get; }
            public DummySyncContext(Guid guid) => Id = guid;

            public static bool Is(Guid id) => Current is DummySyncContext dsc && dsc.Id == id;

            public override void Post(SendOrPostCallback d, object state)
                => ThreadPool.QueueUserWorkItem(_ => Send(d, state), null);

            public override void Send(SendOrPostCallback d, object state)
            {
                var original = Current;
                try
                {
                    SetSynchronizationContext(this);
                    d.Invoke(state);
                }
                finally
                {
                    SetSynchronizationContext(original);
                }
            }
        }

        [Fact]
        public async Task SyncContextNotPreservedByTryWaitAsync()
        {
            var taken = _timeoutMux.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                var pending = _timeoutMux.TryWaitAsync();
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMux uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using (var token2 = await pending)
                {
                    int awaitedThread = Environment.CurrentManagedThreadId;
                    Assert.True(token2.Success, "obtained lock after dispose");

                    Assert.NotEqual(originalThread, awaitedThread);
                    Assert.False(DummySyncContext.Is(id));
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public async Task SyncContextNotPreservedByTryWaitAsync_AsValueTask()
        {
            var taken = _timeoutMux.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                ValueTask<LockToken> pending = _timeoutMux.TryWaitAsync();
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMux uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using (var token2 = await pending)
                {
                    int awaitedThread = Environment.CurrentManagedThreadId;
                    Assert.True(token2.Success, "obtained lock after dispose");

                    Assert.NotEqual(originalThread, awaitedThread);
                    Assert.False(DummySyncContext.Is(id));
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public async Task SyncContextPreservedByTryWaitAsync_WithCapture()
        {
            var taken = _timeoutMux.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                ValueTask<LockToken> pending = _timeoutMux.TryWaitAsync(options: WaitOptions.CaptureContext);
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMux uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using (var token2 = await pending)
                {
                    int awaitedThread = Environment.CurrentManagedThreadId;
                    Assert.True(token2.Success, "obtained lock after dispose");

                    Assert.NotEqual(originalThread, awaitedThread);
                    Assert.True(DummySyncContext.Is(id));
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public async Task SyncContextNotPreservedByTryWaitAsync_WithCaptureAndConfigureAwait()
        {
            var taken = _timeoutMux.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                ValueTask<LockToken> pending = _timeoutMux.TryWaitAsync(options: WaitOptions.CaptureContext);
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMux uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using (var token2 = await pending.ConfigureAwait(false))
                {
                    int awaitedThread = Environment.CurrentManagedThreadId;
                    Assert.True(token2.Success, "obtained lock after dispose");

                    Assert.NotEqual(originalThread, awaitedThread);
                    Assert.False(DummySyncContext.Is(id));
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public void CanObtain()
        {
            // can obtain when not contested (outer)
            // can not obtain when contested, even if re-entrant (inner)
            // can obtain after released (loop)

            for (int i = 0; i < 2; i++)
            {
                using (var outer = _zeroTimeoutMux.TryWait())
                {
                    Assert.True(outer.Success);
                    using (var inner = _zeroTimeoutMux.TryWait())
                    {
                        Assert.False(inner.Success);
                    }
                }
            }

            for (int i = 0; i < 2; i++)
            {
                using (var outer = _timeoutMux.TryWait())
                {
                    Assert.True(outer.Success);
                }
            }
        }

        [Fact]
        public void ChangeStatePreservesCounter()
        {
            Assert.Equal(0xAAA8, LockState.ChangeState(0xAAAA, LockState.Timeout));
            Assert.Equal(0xAAA9, LockState.ChangeState(0xAAAA, LockState.Pending));
            Assert.Equal(0xAAAA, LockState.ChangeState(0xAAAA, LockState.Success));
            Assert.Equal(0xAAAB, LockState.ChangeState(0xAAAA, LockState.Canceled));
        }
        [Fact]
        public void NextTokenIncrementsCorrectly()
        {
            // GetNextToken should always reset the 2 LSB (we'll test it with all 4 inputs), and increment the others
            int token = 0;
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Timeout));
            Assert.Equal(6, token); // 000110
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Pending));
            Assert.Equal(10, token); // 001010
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Success));
            Assert.Equal(14, token); // 001110
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Canceled));
            Assert.Equal(18, token); // 010010

            // and at wraparound, we expect zero again
            token = -1; // anecdotally: a cancelation, but that doesn't matter
            token = LockState.GetNextToken(token);
            Assert.Equal(2, token); // 000010
            token = LockState.GetNextToken(token);
            Assert.Equal(6, token); // 000110
        }

        [Fact]
        public async Task CanObtainAsyncWithoutTimeout()
        {
            // can obtain when not contested (outer)
            // can not obtain when contested, even if re-entrant (inner)
            // can obtain after released (loop)
            // with no timeout: is always completed
            // with timeout: is completed on the success option

            for (int i = 0; i < 2; i++)
            {
                var awaitable = _zeroTimeoutMux.TryWaitAsync();
                Assert.True(awaitable.IsCompleted, nameof(awaitable.IsCompleted));
                Assert.True(awaitable.CompletedSynchronously, nameof(awaitable.CompletedSynchronously));
                using (var outer = await awaitable)
                {
                    Assert.True(outer.Success, nameof(outer.Success));

                    awaitable = _zeroTimeoutMux.TryWaitAsync();
                    Assert.True(awaitable.IsCompleted, nameof(awaitable.IsCompleted) + " inner");
                    Assert.True(awaitable.CompletedSynchronously, nameof(awaitable.CompletedSynchronously) + " inner");
                    using (var inner = await awaitable)
                    {
                        Assert.False(inner.Success, nameof(inner.Success) + " inner");
                    }
                }
            }
        }

        [Fact]
        public async Task CanObtainAsyncWithTimeout()
        {
            for (int i = 0; i < 2; i++)
            {
                var awaitable = _timeoutMux.TryWaitAsync();
                Assert.True(awaitable.IsCompleted, nameof(awaitable.IsCompleted));
                Assert.True(awaitable.CompletedSynchronously, nameof(awaitable.CompletedSynchronously));
                using (var outer = await awaitable)
                {
                    Assert.True(outer.Success);
                }
            }
        }

        [Fact]
        public void Timeout()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, complete = 0, success = 0;

            Assert.NotEqual(0, _timeoutMux.TimeoutMilliseconds);
            lock (allDone)
            {
                using (var token = _timeoutMux.TryWait())
                {
                    lock (allReady)
                    {
                        for (int i = 0; i < COMPETITORS; i++)
                        {
                            Task.Run(() =>
                            {
                                lock (allReady)
                                {
                                    if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                    else Monitor.Wait(allReady);
                                }
                                using (var inner = _timeoutMux.TryWait())
                                {
                                    lock (allDone)
                                    {
                                        if (inner) success++;
                                        if (++complete == COMPETITORS) Monitor.Pulse(allDone);
                                    }
                                    Thread.Sleep(10);
                                }
                            });
                        }
                        Monitor.Wait(allReady);
                    }
                    Thread.Sleep(_timeoutMux.TimeoutMilliseconds * 2);
                }
                Monitor.Wait(allDone);
                Assert.Equal(COMPETITORS, complete);
                Assert.Equal(0, success);
            }
        }

        [Fact]
        public void CompetingCallerAllExecute()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, complete = 0, success = 0;
            lock (allDone)
            {
                using (var token = _timeoutMux.TryWait())
                {
                    lock (allReady)
                    {
                        for (int i = 0; i < COMPETITORS; i++)
                        {
                            Task.Run(() =>
                            {
                                lock (allReady)
                                {
                                    if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                    else Monitor.Wait(allReady);
                                }
                                using (var inner = _timeoutMux.TryWait())
                                {
                                    lock (allDone)
                                    {
                                        if (inner) success++;
                                        if (++complete == COMPETITORS) Monitor.Pulse(allDone);
                                    }
                                    Thread.Sleep(10);
                                }
                            });
                        }
                        Monitor.Wait(allReady);
                    }
                    Thread.Sleep(100);
                }
                Monitor.Wait(allDone);
                Assert.Equal(COMPETITORS, complete);
                Assert.Equal(COMPETITORS, success);
            }
        }

        [Fact]
        public async Task CompetingCallerAllExecuteAsync()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, success = 0, asyncOps = 0;

            var tasks = new Task[COMPETITORS];
            using (var token = await _timeoutMux.TryWaitAsync())
            {
                lock (allReady)
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        tasks[i] = Task.Run(async () =>
                        {
                            lock (allReady)
                            {
                                if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                else Monitor.Wait(allReady);
                            }
                            var awaitable = _timeoutMux.TryWaitAsync();
                            using (var inner = await awaitable)
                            {
                                lock (allDone)
                                {
                                    if (inner) success++;
                                    if (!awaitable.CompletedSynchronously) asyncOps++;
                                }
                                await Task.Delay(10).ConfigureAwait(false);
                            }
                        });
                    }
                    Monitor.Wait(allReady);
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            for (int i = 0; i < tasks.Length; i++)
            {   // deliberately not an await - we want a simple timeout here
                Assert.True(tasks[i].Wait(_timeoutMux.TimeoutMilliseconds));
            }

            lock (allDone)
            {
                Assert.Equal(COMPETITORS, success);
                Assert.Equal(COMPETITORS, asyncOps);
            }
        }

        [Fact]
        public async Task TimeoutAsync()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, success = 0, asyncOps = 0;

            var tasks = new Task[COMPETITORS];
            using (var token = await _timeoutMux.TryWaitAsync())
            {
                lock (allReady)
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        tasks[i] = Task.Run(async () =>
                        {
                            lock (allReady)
                            {
                                if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                else Monitor.Wait(allReady);
                            }
                            var awaitable = _timeoutMux.TryWaitAsync();
                            using (var inner = await awaitable)
                            {
                                lock (allDone)
                                {
                                    if (inner) success++;
                                    if (!awaitable.CompletedSynchronously) asyncOps++;
                                }
                                await Task.Delay(10).ConfigureAwait(false);
                            }
                        });
                    }
                    Monitor.Wait(allReady);
                }
                await Task.Delay(_timeoutMux.TimeoutMilliseconds * 2).ConfigureAwait(false);
            }
            for (int i = 0; i < tasks.Length; i++)
            {   // deliberately not an await - we want a simple timeout here
                Assert.True(tasks[i].Wait(_timeoutMux.TimeoutMilliseconds));
            }

            lock (allDone)
            {
                Assert.Equal(0, success);
                Assert.Equal(COMPETITORS, asyncOps);
            }
        }

        [Fact]
        public async Task PreCanceledReportsCorrectly()
        {
            var cancel = new CancellationTokenSource();
            cancel.Cancel();

            var ct = _timeoutMux.TryWaitAsync(cancel.Token);
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            Assert.Throws<TaskCanceledException>(() => ct.GetResult());

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }

        [Fact]
        public async Task DuringCanceledReportsCorrectly()
        {
            var cancel = new CancellationTokenSource();

            // cancel it *after* issuing incomplete token

            AwaitableLockToken ct;
            using (var token = _timeoutMux.TryWait())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync(cancel.Token);
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted));
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

                cancel.Cancel(); // cancel it *before* release; should be respected
            }
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            Assert.Throws<TaskCanceledException>(() => ct.GetResult());

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }

        [Fact]
        public async Task PostCanceledReportsCorrectly()
        {
            var cancel = new CancellationTokenSource();

            // cancel it *after* issuing incomplete token

            AwaitableLockToken ct;
            using (var token = _timeoutMux.TryWait())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync(cancel.Token);
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted) + ":1");
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled) + ":1");
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully) + ":1");
            }
            // cancel it *after* release - should be ignored
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted) + ":2");
            Assert.False(ct.IsCanceled, nameof(ct.IsCanceled) + ":2");
            Assert.True(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully) + ":2");

            var result = ct.GetResult();
            Assert.True(result.Success);

            result = await ct;
            Assert.True(result.Success);
        }

        [Fact]
        public async Task ManualCanceledReportsCorrectly()
        {
            AwaitableLockToken ct;
            using (var token = _timeoutMux.TryWait())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync();
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted));
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

                Assert.True(ct.TryCancel());
                Assert.True(ct.TryCancel());
            }
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            Assert.Throws<TaskCanceledException>(() => ct.GetResult());

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }

        [Fact]
        public async Task ManualCancelAfterAcquisitionDoesNothing()
        {
            AwaitableLockToken ct;
            using (var token = _timeoutMux.TryWait())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync();
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted));
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));
            }
            Assert.False(ct.TryCancel());
            Assert.False(ct.TryCancel());

            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.True(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            var result = ct.GetResult();
            Assert.True(result.Success);

            result = await ct;
            Assert.True(result.Success);
        }

        [Fact]
        public async Task ManualCancelOnPreCanceledDoesNothing()
        {
            // cancel it *before* issuing token
            var cancel = new CancellationTokenSource();
            cancel.Cancel();

            var ct = _timeoutMux.TryWaitAsync(cancel.Token);
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            Assert.True(ct.TryCancel());
            Assert.True(ct.TryCancel());

            Assert.Throws<TaskCanceledException>(() => ct.GetResult());

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }
    }
}
