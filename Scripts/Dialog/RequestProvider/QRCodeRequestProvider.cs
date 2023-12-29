using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;

namespace ChatdollKit.Dialog
{
    public class QRCodeRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides decoded QRCode data
        public RequestType RequestType { get; } = RequestType.QRCode;

        public ChatdollCamera ChatdollCamera;

        // Create request using voice recognition
        public async UniTask<Request> GetRequestAsync(CancellationToken token)
        {
            var request = new Request(RequestType);

            if (ChatdollCamera != null)
            {
                request.Payloads.Add("qrcode", await ChatdollCamera.ReadCodeAsync(token));
            }
            else
            {
                Debug.LogWarning("ChatdollCamera is not set to QRCodeRequestProvider");
            }

            return request;
        }
    }
}
