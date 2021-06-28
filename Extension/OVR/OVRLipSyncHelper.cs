using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.OVR
{
    public class OVRLipSyncHelper : MonoBehaviour, ILipSyncHelper
    {
        public OVRLipSyncContext LipSyncContext;

        public void Reset()
        {
            // Configure viseme when attached or reset
            ConfigureViseme();
        }

        public void ResetViseme()
        {
            LipSyncContext.ResetContext();
        }

        public void ConfigureViseme()
        {
            // Get GameObjects
            var modelController = gameObject.GetComponent<ModelController>();
            var voiceAudioObject = modelController.AudioSource.gameObject;

            // Get/Add OVRLipSyncContextMorphTarget
            var morphTarget = voiceAudioObject.GetComponent<OVRLipSyncContextMorphTarget>();
            if (morphTarget == null)
            {
                morphTarget = voiceAudioObject.AddComponent<OVRLipSyncContextMorphTarget>();
            }

            // Configure OVRLipSyncContextMorphTarget
            morphTarget.skinnedMeshRenderer = modelController.SkinnedMeshRenderer;
            var shapeKeyIndexes = GetVisemeTargetShapeKeyIndexes(morphTarget.skinnedMeshRenderer.sharedMesh);
            if (shapeKeyIndexes.Count(v => v == -1) > 0)
            {
                Debug.LogWarning("Can't get all shapekeys for VRC FBX format. Retry searching viseme target for VRM format");
                shapeKeyIndexes = GetVisemeTargetShapeKeyIndexes(morphTarget.skinnedMeshRenderer.sharedMesh, true);
            }

            if (shapeKeyIndexes.Count(v => v == -1) > 0)
            {
                Debug.LogWarning("Can't get all shapekeys for VRM. Please configure viseme to blendshapes manually.");
                shapeKeyIndexes = GetVisemeTargetShapeKeyIndexes(morphTarget.skinnedMeshRenderer.sharedMesh, true);
            }
            else
            {
                for (var i = 0; i < morphTarget.visemeToBlendTargets.Length; i++)
                {
                    if (shapeKeyIndexes[i] >= 0)
                    {
                        morphTarget.visemeToBlendTargets[i] = shapeKeyIndexes[i];
                    }
                }
            }

            // Get/Add OVRLipSyncContext
            var lipSyncContext = voiceAudioObject.GetComponent<OVRLipSyncContext>();
            if (lipSyncContext == null)
            {
                lipSyncContext = voiceAudioObject.AddComponent<OVRLipSyncContext>();
            }

            // Configure OVRLipSyncContext
            lipSyncContext.audioSource = modelController.AudioSource;
            lipSyncContext.audioLoopback = true;
            LipSyncContext = lipSyncContext;
        }

        private List<int> GetVisemeTargetShapeKeyIndexes(Mesh mesh, bool IsVRM = false)
        {
            var visemeTargetShapeKeyIndexes = new List<int>();

            // Get shapekeys of this model
            var shapeKeys = new Dictionary<string, int>();
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                shapeKeys[mesh.GetBlendShapeName(i)] = i;
            }

            // Set index of shapekeys
            var visemeNames = Enum.GetNames(typeof(OVRLipSync.Viseme)); // Viseme names of OVRLipSync
            for (var i = 0; i < visemeNames.Length; i++)
            {
                var visemeKey = visemeNames[i];
                if (IsVRM)
                {
                    if (visemeKey.ToLower() == "aa") visemeKey = "a";
                    else if (visemeKey.ToLower() == "ih") visemeKey = "i";
                    else if (visemeKey.ToLower() == "ou") visemeKey = "u";
                    else if (visemeKey.ToLower() == "e") visemeKey = "e";
                    else if (visemeKey.ToLower() == "oh") visemeKey = "o";
                    else visemeKey = "n";
                }

                var shapeKey = shapeKeys.Keys.Where(sk => sk.ToLower().Trim().EndsWith($"_{visemeKey}".ToLower())).FirstOrDefault();
                if (!string.IsNullOrEmpty(shapeKey))
                {
                    visemeTargetShapeKeyIndexes.Add(shapeKeys[shapeKey]);
                }
                else
                {
                    visemeTargetShapeKeyIndexes.Add(-1);
                }
            }

            return visemeTargetShapeKeyIndexes;
        }
    }
}
