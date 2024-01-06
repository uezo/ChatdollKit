using System.Collections.Generic;
using UnityEngine;
using uLipSync;
using VRM;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.VRM
{
    public class VRMuLipSyncHelper : uLipSyncHelper
    {
        protected override Dictionary<string, int> GetBlendShapeMap(GameObject avatarObject)
        {
            var blendShapeMap = new Dictionary<string, int>();
            var proxy = avatarObject.GetComponent<VRMBlendShapeProxy>();
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
    }
}
