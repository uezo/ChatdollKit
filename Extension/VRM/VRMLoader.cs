using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using UniGLTF;
using VRM;
// using VRMShaders;    // Uncomment if you use UniVRM 0.89
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
            Debug.LogWarning("**CAUTION** YOU MUST INCLUDE SOME SHADERS TO BUILD RUNTIME LOAD APP.");
            Debug.LogWarning("See https://vrm.dev/en/api/project/build/ for more details.");

            if (VRMFilePath.StartsWith("http://") || VRMFilePath.StartsWith("https://"))
            {
                Debug.Log($"Get VRM from url: {VRMFilePath}");
                _ = LoadCharacterAsync(VRMFilePath, "GET");
            }
            else if (!string.IsNullOrEmpty(VRMFilePath.Trim()))
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
                // Deactivate and unload
                modelController.DeactivateAvatar(() => {
                    if (vrmInstance != null)
                    {
                        Destroy(vrmInstance);
                    }
                    if (characterObject != null)
                    {
                        Destroy(characterObject);
                    }
                });

                Debug.Log($"VRM size: {vrmBytes.Length} bytes");

                // Parse data
                var gltfData = new GlbBinaryParser(vrmBytes, "UserVrm").Parse();
                var vrmData = new VRMData(gltfData);
                var context = new VRMImporterContext(vrmData);
                vrmInstance = await context.LoadAsync(new RuntimeOnlyAwaitCaller());
                foreach (var renderer in vrmInstance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    renderer.receiveShadows = false;
                }
                // Dispose to prevent memory leak
                context.Dispose();

                characterObject = vrmInstance.gameObject;
                characterObject.name = "CharacterVRM";
                var animator = characterObject.GetComponent<Animator>();
                animator.runtimeAnimatorController = animatorController;

                // Apply avatar to ModelController
                modelController.ActivateAvatar(characterObject, true);

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
