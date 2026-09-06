using Microsoft.ML.OnnxRuntime;
using NUnit.Framework;

// Standalone test-host ownership only. Unity owns its shared OrtEnv itself.
[SetUpFixture]
public sealed class OnnxTestRuntime
{
    [OneTimeTearDown]
    public void DisposeHostRuntime() => OrtEnv.Instance().Dispose();
}
