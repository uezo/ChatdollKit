using System.Collections.Generic;
using UnityEngine;
using uLipSync;
using VRM;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.VRM
{
    public class VRMuLipSyncHelper : uLipSyncHelper
    {
#if UNITY_EDITOR
        public override void ConfigureViseme()
        {
            // ConfigureViseme depends on SkinnedMeshRenderer
            // We will remove the dependency in the future.
            // See https://github.com/hecomi/uLipSync#vrm-support-1

            var modelController = gameObject.GetComponent<ModelController>();
            modelController.SkinnedMeshRenderer = GetFacialSkinnedMeshRenderer(
                modelController.AvatarModel.GetComponentsInChildren<SkinnedMeshRenderer>()
            );
            base.ConfigureViseme();
        }
#endif

        protected override Dictionary<string, int> GetBlendShapeMap(ModelController modelController)
        {
            var blendShapeMap = new Dictionary<string, int>();
            var proxy = modelController.AvatarModel.GetComponent<VRMBlendShapeProxy>();
            foreach (var clip in proxy.BlendShapeAvatar.Clips)
            {
                if (clip.BlendShapeName == "A" || clip.BlendShapeName == "I" || clip.BlendShapeName == "U" || clip.BlendShapeName == "E" || clip.BlendShapeName == "O")
                {
                    blendShapeMap.Add(clip.BlendShapeName, clip.Values[0].Index);
                }
            }
            blendShapeMap.Add("N", -1);
            blendShapeMap.Add("-", -1);
            return blendShapeMap;
        }

        public void ConfigureVisemeRuntime(ModelController modelController)
        {
            // Set SkinnedMeshRenderer
            var uLipSyncBlendShape = gameObject.GetComponent<uLipSyncBlendShape>();
            uLipSyncBlendShape.skinnedMeshRenderer = GetFacialSkinnedMeshRenderer(
                modelController.AvatarModel.GetComponentsInChildren<SkinnedMeshRenderer>()
            );

            // Apply blend shapes
            var blendShapeMap = GetBlendShapeMap(modelController);
            uLipSyncBlendShape.blendShapes.Clear();
            foreach (var map in blendShapeMap)
            {
                uLipSyncBlendShape.blendShapes.Add(new uLipSyncBlendShape.BlendShapeInfo() { phoneme = map.Key, index = map.Value, maxWeight = 1 });
            }
        }

        private SkinnedMeshRenderer GetFacialSkinnedMeshRenderer(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            var maxCount = 1;
            var maxIndex = -1;

            for (var i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                if (skinnedMeshRenderers[i].sharedMesh == null)
                {
                    continue;
                }

                var tempCount = skinnedMeshRenderers[i].sharedMesh.blendShapeCount;
                if (tempCount >= maxCount)
                {
                    maxCount = tempCount;
                    maxIndex = i;
                }
            }

            if (maxIndex < 0)
            {
                // Return null when no blend shapes found
                return null;
            }
            else
            {
                return skinnedMeshRenderers[maxIndex];
            }
        }
    }
}
