using System;
using UnityEngine;
using ChatdollKit.IO;

namespace ChatdollKit.UI
{
    public class CameraButton : MonoBehaviour
    {
        [SerializeField]
        private SimpleCamera simpleCamera;
        public Action ToggleCamera;

        private void Start()
        {
            if (simpleCamera == null)
            {
                simpleCamera = FindFirstObjectByType<SimpleCamera>();
            }
        }

        public void OnButtonClick()
        {
            if (ToggleCamera != null)
            {
                ToggleCamera();
            }
            else if (simpleCamera != null)
            {
                simpleCamera.ToggleCamera();
            }
            else
            {
                Debug.LogWarning("Neither SimpleCamera nor ToggleCamera has been set.");
            }
        }
    }
}
