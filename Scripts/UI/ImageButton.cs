using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ChatdollKit.UI
{
    public class ImageButton : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void OpenFileDialog(string gameObjectName, string methodName, string accept);
#endif

        [SerializeField]
        private bool pathMode;
        [SerializeField]
        private GameObject pathPanel;
        [SerializeField]
        private InputField pathInput;

        public Action<byte[]> HandleImage;
        public Action OnButtonClickAction;

        public void OnButtonClick()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            OpenFileDialog(gameObject.name, "OnFileSelected", "image/*");
#else
            if (pathMode)
            {
                var active = !pathPanel.activeSelf;
                if (active)
                {
                    pathInput.text = string.Empty;
                    pathInput.Select();
                }
                pathPanel.SetActive(active);
            }
            else if (OnButtonClickAction != null)
            {
                OnButtonClickAction();
            }
#endif
        }

        public void OnSubmitImagePath()
        {
            var path = pathInput.text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var imageBytes = File.ReadAllBytes(path);
            HandleImage?.Invoke(imageBytes);

            pathInput.text = string.Empty;
            pathPanel.SetActive(false);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public void OnFileSelected(string base64Data)
        {
            var imageBytes = Convert.FromBase64String(base64Data);
            HandleImage?.Invoke(imageBytes);
        }
#endif
    }
}
