using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using UniGLTF;
using VRM;
using VRMShaders;
using Newtonsoft.Json;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Extension.VRM
{
    public class VRMLoader : MonoBehaviour
    {
        public string VRMFilePath;
        [SerializeField]
        private ModelController modelController;
        [SerializeField]
        private RuntimeAnimatorController animatorController;
        [SerializeField]
        private int WaitBeforeShowCharacter = 1000;

        public bool IsCharacterReady { get; private set; } = false;
        private RuntimeGltfInstance vrmInstance;
        private GameObject characterObject;
        public Action<GameObject> OnCharacterReady;

        private void Start()
        {
            Debug.LogWarning("This is an example to load VRM and configure ChatdollKit at runtime.");

            if (VRMFilePath.StartsWith("http://") || VRMFilePath.StartsWith("https://"))
            {
                Debug.Log($"Get VRM from url: {VRMFilePath}");
                _ = LoadCharacterAsync(VRMFilePath, "GET");
            }
            else
            {
                Debug.Log($"Get VRM from file: {VRMFilePath}");
                _ = LoadCharacterAsync(VRMFilePath);
            }
        }

        public async UniTask LoadCharacterAsync(string path)
        {
            var vrmBytes = File.ReadAllBytes(path);
            await LoadCharacterAsync(vrmBytes);
        }

        public async UniTask LoadCharacterAsync(string url, string method = "GET", Dictionary<string, string> parameters = null, Dictionary<string, string> headers = null, Dictionary<string, object> jsonData = null)
        {
            var client = new ChatdollHttp();
            var response = await client.SendRequestAsync(
                url, method,
                parameters: parameters,
                headers: headers,
                content: JsonConvert.SerializeObject(jsonData)
            );

            await LoadCharacterAsync(response.Data);
        }

        public async UniTask LoadCharacterAsync(byte[] vrmBytes)
        {
            IsCharacterReady = false;

            try
            {
                // Destroy current character and stop blinking
                if (vrmInstance != null)
                {
                    Destroy(vrmInstance);
                }
                if (characterObject != null)
                {
                    Destroy(characterObject);
                }

                Debug.Log($"VRM size: {vrmBytes.Length} bytes");

                // Parse data
                var gltfData = new GlbBinaryParser(vrmBytes, "UserVrm").Parse();
                var vrmData = new VRMData(gltfData);
                var context = new VRMImporterContext(vrmData);
                vrmInstance = await context.LoadAsync(new RuntimeOnlyAwaitCaller());
                // Dispose to prevent memory leak
                context.Dispose();

                // Setup ChatdollKit
                characterObject = vrmInstance.gameObject;
                characterObject.name = "CharacterVRM";
                modelController.AvatarModel = characterObject;
                var animator = characterObject.GetComponent<Animator>();
                animator.runtimeAnimatorController = animatorController;
                var lipSyncHelper = modelController.gameObject.GetComponent<VRMuLipSyncHelper>();
                lipSyncHelper.ConfigureViseme(characterObject);

                // Initialize ChatdollKit
                modelController.gameObject.SetActive(true);

                // Wait before show to finish transition to initial animation
                await UniTask.Delay(WaitBeforeShowCharacter);

                // Show character
                vrmInstance.ShowMeshes();

                OnCharacterReady?.Invoke(characterObject);
                IsCharacterReady = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at LoadCharacterAsync: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
