using System;
using System.Collections.Concurrent;
using System.Threading;
using ChatdollKit.SpeechPipeline.Async;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

// NUnit runs the pure cores outside Unity. Only this test harness uses a desktop
// event-loop thread and timer; the production scheduler uses Unity's PlayerLoop.
[SetUpFixture]
public sealed class SpeechAsyncTestRuntime
{
    private TestScheduler scheduler;
    [OneTimeSetUp]
    public void Start() { scheduler = new TestScheduler(); SpeechAsync.Scheduler = scheduler; }
    [OneTimeTearDown]
    public void Stop() => scheduler.Dispose();

    private sealed class TestScheduler : ISpeechAsyncScheduler, IDisposable
    {
        private readonly BlockingCollection<Action> pending = new BlockingCollection<Action>();
        private readonly Thread loop;
        public TestScheduler()
        {
            loop = new Thread(Run) { IsBackground = true, Name = "Speech pipeline test loop" };
            loop.Start();
        }
        public void Post(Action continuation) => pending.Add(continuation);
        public UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            SpeechAsync.FromTask(System.Threading.Tasks.Task.Delay(delay, cancellationToken));
        private void Run()
        {
            foreach (var action in pending.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception error) { TestContext.Progress.WriteLine(error); }
            }
        }
        public void Dispose() { pending.CompleteAdding(); loop.Join(TimeSpan.FromSeconds(5)); pending.Dispose(); }
    }
}
