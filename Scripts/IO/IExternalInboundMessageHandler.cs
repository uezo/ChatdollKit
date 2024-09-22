using System;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.IO
{
    public interface IExternalInboundMessageHandler
    {
        Func<ExternalInboundMessage, UniTask> OnDataReceived { get; set; }
    }
}
