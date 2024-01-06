using System;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using UniGLTF;
using VRM;
using VRMShaders;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.VRM
{
    public class VRMLoader : MonoBehaviour
    {
        public string VRMFilePath;
        [SerializeField]
        private ModelController modelController;
        [SerializeField]
        private RuntimeAnimatorController animatorController;

        public bool IsCharacterReady { get; private set; } = false;
        private RuntimeGltfInstance vrmInstance;
        private GameObject characterObject;
        public Action<GameObject> OnCharacterReady;

        private void Start()
        {
            Debug.LogWarning("This is an example to load VRM and configure ChatdollKit at runtime.");

            if (!string.IsNullOrEmpty(VRMFilePath))
            {
                _ = LoadCharacterAsync(VRMFilePath);
            }
        }

        public async UniTask LoadCharacterAsync(string path)
        {
            IsCharacterReady = false;

            // Destroy current character and stop blinking
            if (vrmInstance != null)
            {
                Destroy(vrmInstance);
            }
            if (characterObject != null)
            {
                Destroy(characterObject);
            }

            // Load character
            try
            {
                // Load VRM from file
                var vrmBytes = File.ReadAllBytes(path);
                Debug.Log($"{path}: {vrmBytes.Length} bytes");

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

                // Show character
                vrmInstance.ShowMeshes();

                OnCharacterReady?.Invoke(characterObject);
                IsCharacterReady = true;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message + ex.StackTrace);
            }
        }
    }
}
