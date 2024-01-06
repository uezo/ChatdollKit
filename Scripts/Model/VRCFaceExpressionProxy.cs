using System.Collections.Generic;
using UnityEngine;

namespace ChatdollKit.Model
{
    public class VRCFaceExpressionProxy : MonoBehaviour, IFaceExpressionProxy
    {
        public FaceClipConfiguration FaceClipConfiguration;
        public SkinnedMeshRenderer SkinnedMeshRenderer;
        private Dictionary<string, FaceClip> faceClips = new Dictionary<string, FaceClip>();
        private Dictionary<string, float> faceValues = new Dictionary<string, float>();
        private IBlink blinker;

        [SerializeField]
        private float smoothTime = 0.2f;
        private float changeStartAt;
        private string currentFaceName;
        private float valueToApply;
        private float velocityAtStart;

        private void Awake()
        {
            Setup(gameObject.GetComponent<ModelController>().AvatarModel);
        }

        public void Setup(GameObject avatarObject)
        {
            SkinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject);
            blinker = gameObject.GetComponent<IBlink>();
            LoadFaces();
            if (faceClips.Count == 0)
            {
                Debug.Log("Add Neutral face expression with values zero");
                faceClips.Add("Neutral", new FaceClip("Neutral", SkinnedMeshRenderer));
            }
        }

        private void Update()
        {
            if (changeStartAt > 0)
            {
                if (currentFaceName == "Neutral")
                {
                    _ = blinker.StartBlinkAsync();
                }
                else
                {
                    blinker.StopBlink();
                }

                var elapsed = Time.realtimeSinceStartup - changeStartAt;
                var velocity = elapsed / smoothTime + velocityAtStart;

                if (velocity > 1)
                {
                    velocity = 1;
                    changeStartAt = 0;
                }

                // Reset before apply
                foreach (var fc in faceClips)
                {
                    if (fc.Key != currentFaceName)
                    {
                        SetExpression(fc.Key, faceValues[fc.Key] * (1 - velocity));
                    }
                }
                // Apply new
                SetExpression(currentFaceName, velocity * valueToApply);
            }
        }

        public void SetExpression(string name = "Neutral", float value = 1.0f)
        {
            if (!faceClips.ContainsKey(name))
            {
                Debug.LogWarning($"Face '{name}' is not registered");
                return;
            }

            foreach (var blendShapeValue in faceClips[name].Values)
            {
                if (blendShapeValue.Weight > 0)
                {
                    SkinnedMeshRenderer.SetBlendShapeWeight(blendShapeValue.Index, blendShapeValue.Weight * value);
                }
            }
            faceValues[name] = value;
        }

        public void SetExpressionSmoothly(string name = "Neutral", float value = 1.0f)
        {
            if (!faceClips.ContainsKey(name))
            {
                Debug.LogWarning($"Face '{name}' is not registered");
                return;
            }

            changeStartAt = Time.realtimeSinceStartup;
            currentFaceName = name;
            valueToApply = value;
            velocityAtStart = faceValues[name];
        }

        // Load faces from config
        private void LoadFaces()
        {
            if (FaceClipConfiguration == null)
            {
                Debug.LogWarning("Face configuration is not set");
                return;
            }

            faceClips.Clear();
            faceValues.Clear();

            foreach (var faceClip in FaceClipConfiguration.FaceClips)
            {
                faceClips[faceClip.Name] = faceClip;
                faceValues[faceClip.Name] = 0.0f;
            }
        }
    }
}
