using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.UI
{
    public class ImageButton : MonoBehaviour
    {
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
    }
}
