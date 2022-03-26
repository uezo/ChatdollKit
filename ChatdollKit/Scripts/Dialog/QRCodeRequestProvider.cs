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
        public async UniTask<Request> GetRequestAsync(User user, State state, CancellationToken token, Request preRequest = null)
        {
            var request = preRequest ?? new Request(RequestType);
            request.User = user;

            if (ChatdollCamera != null)
            {
                request.Payloads.Add(await ChatdollCamera.ReadCodeAsync(token));
            }
            else
            {
                Debug.LogWarning("ChatdollCamera is not set to QRCodeRequestProvider");
            }

            return request;
        }
    }
}
