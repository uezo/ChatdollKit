using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChatdollKit.Model
{
    public class AvatarUtility
    {
        public static readonly string[] BasicFaceBlendShapeKeywords = { "blink", "eye_close" };

        public static SkinnedMeshRenderer GetFacialSkinnedMeshRenderer(GameObject avatarGameObject)
        {
            return GetFacialSkinnedMeshRenderer(avatarGameObject, BasicFaceBlendShapeKeywords);
        }

        public static SkinnedMeshRenderer GetFacialSkinnedMeshRenderer(GameObject avatarGameObject, IEnumerable<string> blendShapeKeywords)
        {
            foreach (var smr in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;

                for (var i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    var blendShapeNameLower = smr.sharedMesh.GetBlendShapeName(i).ToLower();

                    if (blendShapeKeywords.Any(keyword => !string.IsNullOrEmpty(keyword) && blendShapeNameLower.Contains(keyword)))
                    {
                        return smr;
                    }
                }
            }

            Debug.LogWarning("Facial SkinnedMeshRenderer not found");
            return null;
        }
    }
}
