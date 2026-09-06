using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    // NUnit's synchronous ThrowsAsync blocks the thread that must advance the
    // speech loop. Await the failure instead, keeping the same assertion strength.
    public static class SpeechAsyncAssert
    {
        // Verify completed operations no longer retain requests/results in private tracking collections.
        public static void NoPendingOperations(object owner, string fieldName)
        {
            var type = owner.GetType();
            System.Reflection.FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                type = type.BaseType;
            }
            Assert.That(field, Is.Not.Null, "The operation tracking collection must exist.");
            Assert.That((System.Collections.IEnumerable)field.GetValue(owner), Is.Empty,
                "Completed operations must release their tracking entries and retained results.");
        }

        public static async UniTask<TException> ThrowsAsync<TException>(Func<UniTask> operation)
            where TException : Exception
        {
            var error = await CaptureAsync(operation);
            Assert.That(error, Is.TypeOf<TException>());
            return (TException)error;
        }

        public static async UniTask<TException> ThrowsInstanceOfAsync<TException>(Func<UniTask> operation)
            where TException : Exception
        {
            var error = await CaptureAsync(operation);
            Assert.That(error, Is.InstanceOf<TException>());
            return (TException)error;
        }

        private static async UniTask<Exception> CaptureAsync(Func<UniTask> operation)
        {
            try { await operation(); }
            catch (Exception error) { return error; }
            return null;
        }
    }
}
