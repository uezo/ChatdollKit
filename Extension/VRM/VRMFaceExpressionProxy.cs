using System.Collections.Generic;
using UnityEngine;
using VRM;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.VRM
{
    public class VRMFaceExpressionProxy : MonoBehaviour, IFaceExpressionProxy
    {
        private VRMBlendShapeProxy blendShapeProxy;
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
            blendShapeProxy = avatarObject.GetComponent<VRMBlendShapeProxy>();
            blinker = gameObject.GetComponent<IBlink>();
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

                foreach (var kv in blendShapeProxy.GetValues())
                {
                    if (kv.Key.ToString() == currentFaceName)
                    {
                        blendShapeProxy.ImmediatelySetValue(kv.Key, velocity * valueToApply);
                    }
                    else if (kv.Value > 0)
                    {
                        blendShapeProxy.ImmediatelySetValue(kv.Key, kv.Value * (1 - velocity));
                    }
                }
            }
        }

        public void SetExpression(string name = "Neutral", float value = 1.0f)
        {
            blendShapeProxy.ImmediatelySetValue(GetKeyValue(name).Key, value);
        }

        public void SetExpressionSmoothly(string name = "Neutral", float value = 1.0f)
        {
            changeStartAt = Time.realtimeSinceStartup;
            currentFaceName = name;
            valueToApply = value;
            velocityAtStart = GetKeyValue(name).Value;
        }

        private KeyValuePair<BlendShapeKey, float> GetKeyValue(string name)
        {
            KeyValuePair<BlendShapeKey, float> neutral = default;
            foreach (var kv in blendShapeProxy.GetValues())
            {
                if (kv.Key.ToString() == name)
                {
                    return kv;
                }
                else if (kv.Key.ToString() == "Neutral")
                {
                    neutral = kv;
                }
            }
            return neutral;
        }
    }
}
