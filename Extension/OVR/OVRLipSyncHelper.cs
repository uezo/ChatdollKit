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

        public void ResetViseme()
        {
            LipSyncContext.ResetContext();
        }

        public void ConfigureViseme(GameObject avatarObject)
        {
            // Get/Add OVRLipSyncContextMorphTarget
            var morphTarget = GetComponent<OVRLipSyncContextMorphTarget>();
            if (morphTarget == null)
            {
                morphTarget = gameObject.AddComponent<OVRLipSyncContextMorphTarget>();
            }

            // Configure OVRLipSyncContextMorphTarget
            morphTarget.skinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject);

            // Map blend shapes
            Dictionary<string, int> blendShapeMap = null;
            if (IsVRCModel(morphTarget.skinnedMeshRenderer.sharedMesh))
            {
                blendShapeMap = GetVRCBlendShapeMap(morphTarget.skinnedMeshRenderer.sharedMesh);
            }
            else if (IsVRMModel(morphTarget.skinnedMeshRenderer.sharedMesh))
            {
                blendShapeMap = GetVRMBlendShapeMap(morphTarget.skinnedMeshRenderer.sharedMesh);
            }
            else
            {
                Debug.LogWarning("BlendShapes for VRC or VRM are not found. Set visemes manually to use LipSync.");
            }

            // Apply blend shapes
            var visemes = Enum.GetNames(typeof(OVRLipSync.Viseme));
            for (var i = 0; i < visemes.Length; i++)
            {
                if (blendShapeMap.ContainsKey(visemes[i]))
                {
                    morphTarget.visemeToBlendTargets[i] = blendShapeMap[visemes[i]];
                }
                else
                {
                    morphTarget.visemeToBlendTargets[i] = 0;
                }
            }

            // Get/Add OVRLipSyncContext
            var lipSyncContext = GetComponent<OVRLipSyncContext>();
            if (lipSyncContext == null)
            {
                lipSyncContext = gameObject.AddComponent<OVRLipSyncContext>();
            }

            // Configure OVRLipSyncContext
            lipSyncContext.audioSource = GetComponent<AudioSource>();
            lipSyncContext.audioLoopback = true;
            LipSyncContext = lipSyncContext;
        }

        private Dictionary<string, int> GetVRCBlendShapeMap(Mesh mesh)
        {
            var blendShapeMap = GetBlendShapeMapBase();

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_sil"))
                {
                    blendShapeMap["sil"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_pp"))
                {
                    blendShapeMap["PP"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_ff"))
                {
                    blendShapeMap["FF"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_th"))
                {
                    blendShapeMap["TH"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_dd"))
                {
                    blendShapeMap["DD"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_kk"))
                {
                    blendShapeMap["kk"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_ch"))
                {
                    blendShapeMap["CH"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_ss"))
                {
                    blendShapeMap["SS"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_nn"))
                {
                    blendShapeMap["nn"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_rr"))
                {
                    blendShapeMap["RR"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_aa"))
                {
                    blendShapeMap["aa"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_e"))
                {
                    blendShapeMap["E"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_ih"))
                {
                    blendShapeMap["ih"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_oh"))
                {
                    blendShapeMap["oh"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("vrc.v_ou"))
                {
                    blendShapeMap["ou"] = i;
                }
            }

            return blendShapeMap;
        }

        private Dictionary<string, int> GetVRMBlendShapeMap(Mesh mesh)
        {
            var blendShapeMap = new Dictionary<string, int>()
            {
                { "sil", -1 }, { "nn", -1 }
            };

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_A"))
                {
                    blendShapeMap["aa"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_I"))
                {
                    blendShapeMap["ih"] = i;
                    blendShapeMap["DD"] = i;
                    blendShapeMap["kk"] = i;
                    blendShapeMap["CH"] = i;
                    blendShapeMap["SS"] = i;
                    blendShapeMap["RR"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_U"))
                {
                    blendShapeMap["ou"] = i;
                    blendShapeMap["PP"] = i;
                    blendShapeMap["FF"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_E"))
                {
                    blendShapeMap["E"] = i;
                    blendShapeMap["TH"] = i;
                }
                else if (mesh.GetBlendShapeName(i).EndsWith("Fcl_MTH_O"))
                {
                    blendShapeMap["oh"] = i;
                }
            }

            return blendShapeMap;
        }

        private Dictionary<string, int> GetBlendShapeMapBase()
        {
            return Enum.GetNames(typeof(OVRLipSync.Viseme)).ToDictionary(v => v, v => 0);
        }

        private bool IsVRCModel(Mesh mesh)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains("vrc.v_"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsVRMModel(Mesh mesh)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains("Fcl_MTH_"))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
