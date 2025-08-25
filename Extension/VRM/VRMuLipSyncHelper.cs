using System.Collections.Generic;
using UnityEngine;
using uLipSync;
using VRM;
using ChatdollKit.Model;
using System.Linq;

namespace ChatdollKit.Extension.VRM
{
    public class VRMuLipSyncHelper : MonoBehaviour, ILipSyncHelper
    {
        private string[] BlendShapeNamesForMouth => new[] { "A", "I", "U", "E", "O" };

        public void ResetViseme()
        {
            // do nothing
        }

        public void ConfigureViseme(GameObject avatarObject)
        {
            // Get BlendShapeMap for viseme
            var blendShapeMap = GetBlendShapeMap(avatarObject);

            // Get/Add uLipSyncBlendShape
            var uLipSyncBlendShape = gameObject.GetComponent<uLipSyncBlendShape>();
            if (uLipSyncBlendShape == null)
            {
                uLipSyncBlendShape = gameObject.AddComponent<uLipSyncBlendShape>();
            }

            // Configure uLipSyncBlendShape
            uLipSyncBlendShape.skinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject, BlendShapeNamesForMouth);

            // Apply blend shapes
            uLipSyncBlendShape.blendShapes.Clear();
            foreach (var map in blendShapeMap)
            {
                uLipSyncBlendShape.blendShapes.Add(new uLipSyncBlendShape.BlendShapeInfo() { phoneme = map.Key, index = map.Value, maxWeight = 1 });
            }

#if UNITY_EDITOR
            // Get/Add uLipSync
            var uLipSyncMain = gameObject.GetComponent<uLipSync.uLipSync>();
            if (uLipSyncMain == null)
            {
                uLipSyncMain = gameObject.AddComponent<uLipSync.uLipSync>();
            }

            // Add listener
            UnityEditor.Events.UnityEventTools.AddPersistentListener(uLipSyncMain.onLipSyncUpdate, uLipSyncBlendShape.OnLipSyncUpdate);

            // Set profile
            var profiles = UnityEditor.AssetDatabase.FindAssets("-Profile-Female");
            if (profiles.Length > 0)
            {
                uLipSyncMain.profile = UnityEditor.AssetDatabase.LoadAssetAtPath<Profile>(UnityEditor.AssetDatabase.GUIDToAssetPath(profiles.First()));
            }
#endif
        }

        private Dictionary<string, int> GetBlendShapeMap(GameObject avatarObject)
        {
            var blendShapeMap = new Dictionary<string, int>();
            var proxy = avatarObject.GetComponent<VRMBlendShapeProxy>();
            foreach (var clip in proxy.BlendShapeAvatar.Clips)
            {
                if (BlendShapeNamesForMouth.Contains(clip.BlendShapeName))
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
