using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.IO;


namespace ChatdollKit.Dialog
{
    public class QRCodeRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides decoded QRCode data
        public RequestType RequestType { get; } = RequestType.QRCode;

        public ChatdollCamera ChatdollCamera;

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token, Request preRequest = null)
        {
            var request = preRequest ?? new Request(RequestType);
            request.User = user;

            var payloads = new List<string>();

            if (ChatdollCamera != null)
            {
                payloads.Add(await ChatdollCamera.ReadCodeAsync(token));
            }
            else
            {
                Debug.LogWarning("ChatdollCamera is not set to QRCodeRequestProvider");
            }

            request.Payloads = payloads;
            return request;
        }
    }
}
