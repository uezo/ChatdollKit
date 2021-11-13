using UnityEngine;
using System.Linq;

namespace ChatdollKit.Demo
{
    public class DemoLipSync : MonoBehaviour
    {
        [SerializeField]
        private AudioSource audioSource;
        [SerializeField]
        private SkinnedMeshRenderer SkinnedMeshRenderer;
        [SerializeField]
        private string lipSyncBlendShapeName;
        private int lipSyncBlendShapeIndex;
        [SerializeField]
        private float Amplification = 400f;
        private bool isAvailable;

        private void Start()
        {
            isAvailable = true;

            if (audioSource == null)
            {
                Debug.LogWarning("AudioSource is not set to DemoLipSync");
                isAvailable = false;
            }

            if (SkinnedMeshRenderer == null)
            {
                Debug.LogWarning("SkinnedMeshRenderer is not set to DemoLipSync");
                isAvailable = false;
                return;
            }

            lipSyncBlendShapeIndex = SkinnedMeshRenderer.sharedMesh.GetBlendShapeIndex(lipSyncBlendShapeName);

            if (lipSyncBlendShapeIndex == -1)
            {
                Debug.LogWarning($"BlendShape for DemoLipSync is not found: {lipSyncBlendShapeName}");
                isAvailable = false;
            }
        }

        void LateUpdate()
        {
            if (isAvailable == false)
            {
                return;
            }

            // Set the avarage volume of current frame * amplification to BlendShape
            var data = new float[256];
            audioSource.GetOutputData(data, 0);
            SkinnedMeshRenderer.SetBlendShapeWeight(lipSyncBlendShapeIndex, data.Average(v => Mathf.Abs(v)) * Amplification);
        }
    }
}
