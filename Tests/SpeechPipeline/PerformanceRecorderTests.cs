using ChatdollKit.SpeechPipeline.Performance;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class PerformanceRecorderTests
    {
        private string directory;
        private string path;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "chatdoll-performance-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "nested", "performance.jsonl");
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test]
        public void CopyIsIndependentAndPreservesNullableMeasurements()
        {
            var original = Record("first");
            original.SttTime = 0.12;
            original.SpeechEndAt = original.StartedAt.AddMilliseconds(-500);
            var copy = original.Copy();
            original.TransactionId = "changed";
            original.SttTime = null;
            Assert.That(copy, Is.Not.SameAs(original));
            Assert.That(copy.TransactionId, Is.EqualTo("first"));
            Assert.That(copy.SttTime, Is.EqualTo(0.12));
            Assert.That(copy.SpeechEndAt, Is.EqualTo(copy.StartedAt.AddMilliseconds(-500)));
            Assert.That(copy.SttAfterThresholdTime, Is.Null);
        }

        [Test]
        public async NUnitTask WritesCorrelationAndDurationsWithoutChangingVadTimingOrigin()
        {
            var record = Record("request");
            record.SessionId = "session"; record.ContextId = "context"; record.UserId = "user"; record.Channel = "channel";
            record.SttName = "stt"; record.LlmName = "llm"; record.TtsName = "tts";
            record.SttTime = 0.1; record.StopResponseTime = 0.2; record.BeforeLlmTime = 0.3;
            record.LlmFirstChunkTime = 0.4; record.LlmFirstVoiceChunkTime = 0.5; record.LlmTime = 0.6;
            record.TtsFirstChunkTime = 0.7; record.TtsTime = 0.8; record.TotalTime = 0.9; record.VoiceLength = 1.2;
            record.SpeechEndAt = record.StartedAt.AddSeconds(-1); record.SilenceThresholdTime = 0.45;
            record.SttAfterThresholdTime = 0.05; record.TurnEndGateTime = 0.2; record.TurnEndGateHeld = true;
            var recorder = new JsonlPerformanceRecorder(path);
            try
            {
                await recorder.RecordAsync(record);
                var lines = await ReadLines();
                Assert.That(lines.Length, Is.EqualTo(1));
                var actual = JObject.Parse(lines[0]);
                Assert.That((string)actual["TransactionId"], Is.EqualTo("request"));
                Assert.That((string)actual["SessionId"], Is.EqualTo("session"));
                Assert.That((string)actual["ContextId"], Is.EqualTo("context"));
                Assert.That((string)actual["UserId"], Is.EqualTo("user"));
                Assert.That((string)actual["Channel"], Is.EqualTo("channel"));
                Assert.That((string)actual["SttName"], Is.EqualTo("stt"));
                Assert.That((string)actual["LlmName"], Is.EqualTo("llm"));
                Assert.That((string)actual["TtsName"], Is.EqualTo("tts"));
                Assert.That((double)actual["LlmFirstChunkTime"], Is.EqualTo(0.4));
                Assert.That((double)actual["LlmFirstVoiceChunkTime"], Is.EqualTo(0.5));
                Assert.That((double)actual["TotalTime"], Is.EqualTo(0.9));
                Assert.That((double)actual["VoiceLength"], Is.EqualTo(1.2));
                Assert.That(actual["SpeechEndAt"].ToObject<DateTimeOffset>(), Is.EqualTo(record.SpeechEndAt));
                Assert.That((double)actual["SilenceThresholdTime"], Is.EqualTo(0.45));
                Assert.That((double)actual["SttAfterThresholdTime"], Is.EqualTo(0.05));
                Assert.That((double)actual["TurnEndGateTime"], Is.EqualTo(0.2));
                Assert.That((bool)actual["TurnEndGateHeld"], Is.True);
                Assert.That(actual.Properties().Select(property => property.Name), Does.Not.Contain("Text").And.Not.Contain("Audio").And.Not.Contain("ApiKey"));
            }
            finally { await recorder.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask SnapshotsRecordBeforeAwaitingFileIo()
        {
            var recorder = new JsonlPerformanceRecorder(path);
            try
            {
                var record = Record("original"); record.SttTime = 0.25;
                var operation = recorder.RecordAsync(record);
                record.TransactionId = "mutated"; record.SttTime = 100;
                await operation;
                var actual = JObject.Parse((await ReadLines()).Single());
                Assert.That((string)actual["TransactionId"], Is.EqualTo("original"));
                Assert.That((double)actual["SttTime"], Is.EqualTo(0.25));
            }
            finally { await recorder.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ConcurrentCallsProduceSeparateValidUtf8JsonLines()
        {
            var recorder = new JsonlPerformanceRecorder(path);
            try
            {
                var records = Enumerable.Range(0, 100).Select(index => Record("取引\"\n" + index)).ToArray();
                await Bounded(UniTask.WhenAll(records.Select(record => recorder.RecordAsync(record))));
                SpeechAsyncAssert.NoPendingOperations(recorder, "pending");
                var lines = await ReadLines();
                Assert.That(lines.Length, Is.EqualTo(records.Length));
                Assert.That(lines.Select(line => (string)JObject.Parse(line)["TransactionId"]), Is.EquivalentTo(records.Select(record => record.TransactionId)));
                var bytes = await File.ReadAllBytesAsync(path);
                Assert.That(bytes[0], Is.EqualTo((byte)'{'), "Do not emit UTF-8 BOMs between append operations.");
                Assert.That(bytes[bytes.Length - 1], Is.EqualTo((byte)'\n'));
            }
            finally { await recorder.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask NewRecorderAppendsToExistingLog()
        {
            var first = new JsonlPerformanceRecorder(path);
            try { await first.RecordAsync(Record("first")); }
            finally { await first.DisposeAsync(); }
            var second = new JsonlPerformanceRecorder(path);
            try { await second.RecordAsync(Record("second")); }
            finally { await second.DisposeAsync(); }
            Assert.That((await ReadLines()).Select(line => (string)JObject.Parse(line)["TransactionId"]), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public async NUnitTask AlreadyCancelledRecordDoesNotCreateFile()
        {
            var recorder = new JsonlPerformanceRecorder(path);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try
                {
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await recorder.RecordAsync(Record("cancelled"), cancelled.Token));
                    Assert.That(File.Exists(path), Is.False);
                }
                finally { await recorder.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask DisposeIsIdempotentRejectsNewRecordsAndDoesNotCreateUnusedFile()
        {
            var recorder = new JsonlPerformanceRecorder(path);
            var first = recorder.DisposeAsync();
            var second = recorder.DisposeAsync();
            Assert.That(second, Is.EqualTo(first));
            await first;
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<ObjectDisposedException>(async () => await recorder.RecordAsync(Record("late")));
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public async NUnitTask DisposeDrainsWrittenLinesAndCancelsQueuedRecordsWithoutPartialJson()
        {
            var recorder = new JsonlPerformanceRecorder(path);
            var operations = Enumerable.Range(0, 200).Select(index => recorder.RecordAsync(Record(index.ToString()))).ToArray();
            try
            {
                await Bounded(recorder.DisposeAsync());
                Assert.That(operations.All(operation => operation.Status.IsCompleted()), Is.True);
                Assert.That(operations.All(operation => operation.Status.IsCanceled() || operation.Status == UniTaskStatus.Succeeded), Is.True);
                var lines = File.Exists(path) ? await ReadLines() : Array.Empty<string>();
                Assert.That(lines.Length, Is.EqualTo(operations.Count(operation => operation.Status == UniTaskStatus.Succeeded)));
                foreach (var line in lines) Assert.That(JObject.Parse(line)["TransactionId"], Is.Not.Null);
                if (File.Exists(path)) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            finally { await recorder.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask FileOpenFailurePropagatesAndLaterRecordCanRetryAfterPathIsFixed()
        {
            Directory.CreateDirectory(directory);
            var blocker = Path.Combine(directory, "nested");
            await File.WriteAllTextAsync(blocker, "blocking file");
            var recorder = new JsonlPerformanceRecorder(path);
            try
            {
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<IOException>(async () => await recorder.RecordAsync(Record("failed")));
                File.Delete(blocker);
                await recorder.RecordAsync(Record("succeeded"));
                Assert.That((string)JObject.Parse((await ReadLines()).Single())["TransactionId"], Is.EqualTo("succeeded"));
            }
            finally { await recorder.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask NullRecordIsRejectedWithoutFileIo()
        {
            var recorder = new JsonlPerformanceRecorder(path);
            try
            {
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<ArgumentNullException>(async () => await recorder.RecordAsync(null));
                Assert.That(File.Exists(path), Is.False);
            }
            finally { await recorder.DisposeAsync(); }
        }

        private static PipelinePerformanceRecord Record(string id) => new PipelinePerformanceRecord
        {
            TransactionId = id, Status = "completed", StartedAt = new DateTimeOffset(2026, 9, 5, 12, 34, 56, TimeSpan.FromHours(9))
        };

        private async UniTask<string[]> ReadLines()
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true))
            using (var reader = new StreamReader(input, Encoding.UTF8))
                return (await reader.ReadToEndAsync()).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
        private static async UniTask Bounded(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromMilliseconds(5000))), Is.EqualTo(0), "Operation did not settle within five seconds.");
            await task;
        }
    }
}
