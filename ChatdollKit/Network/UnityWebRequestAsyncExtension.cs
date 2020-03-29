using UnityEngine.Networking;


namespace ChatdollKit.Network
{
    public static class UnityWebRequestAsyncExtension
    {
        public static UnityWebRequestAwaiter GetAwaiter(this UnityWebRequestAsyncOperation asyncOperation)
        {
            return new UnityWebRequestAwaiter(asyncOperation);
        }
    }
}
