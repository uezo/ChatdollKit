using System;
using System.Collections.Generic;
using ChatdollKit.Model;
using UnityEngine;

namespace ChatdollKit.Avatar.LipSync
{
    /// <summary>Sets up named mouth shapes for MFCC lip sync when an avatar is loaded.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(MfccLipSync))]
    [AddComponentMenu("ChatdollKit/Avatar/MFCC Lip Sync Helper")]
    public class MfccLipSyncHelper : MonoBehaviour, ILipSyncHelper
    {
        public string BlendShapeNameForMouthA = "vrc.v_aa";
        public string BlendShapeNameForMouthI = "vrc.v_ih";
        public string BlendShapeNameForMouthU = "vrc.v_ou";
        public string BlendShapeNameForMouthE = "vrc.v_e";
        public string BlendShapeNameForMouthO = "vrc.v_oh";

        private MfccLipSync lipSync;

        public virtual void ConfigureViseme(GameObject avatarObject)
        {
            var engine = GetLipSync();
            if (engine == null) engine = lipSync = gameObject.AddComponent<MfccLipSync>();
            ClearConfiguration(engine);
            engine.Mappings = avatarObject != null ? CreateMappings(avatarObject) ?? Array.Empty<VisemeMapping>() : Array.Empty<VisemeMapping>();
            engine.RebuildMappings();
            MarkSetupDirty(engine);
        }

        public void ResetViseme()
        {
            var engine = GetLipSync();
            if (engine != null) engine.ResetViseme();
        }

        private MfccLipSync GetLipSync()
        {
            if (lipSync == null) lipSync = GetComponent<MfccLipSync>();
            return lipSync;
        }

        /// <summary>Produce explicit renderer bindings; subclasses can use avatar-specific expression metadata.</summary>
        protected virtual VisemeMapping[] CreateMappings(GameObject avatarObject)
        {
            if (avatarObject == null) return Array.Empty<VisemeMapping>();
            var renderers = avatarObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var names = new[] { BlendShapeNameForMouthA, BlendShapeNameForMouthI, BlendShapeNameForMouthU, BlendShapeNameForMouthE, BlendShapeNameForMouthO };
            var mappings = new List<VisemeMapping>();
            for (var vowel = 0; vowel < names.Length; vowel++)
            {
                if (string.IsNullOrEmpty(names[vowel])) continue;
                var count = FindShapes(renderers, names[vowel], true, out var renderer, out var shapeName);
                // Multiple exact matches are ambiguous too; do not select an arbitrary renderer.
                if (count == 0) count = FindShapes(renderers, names[vowel], false, out renderer, out shapeName);
                if (count != 1) continue;
                mappings.Add(new VisemeMapping { Viseme = (Viseme)(vowel + 1), Renderer = renderer, BlendShape = shapeName, MaxWeight = 100 });
            }
            return mappings.ToArray();
        }

        private static int FindShapes(SkinnedMeshRenderer[] renderers, string requestedName, bool exact,
            out SkinnedMeshRenderer matchingRenderer, out string matchingName)
        {
            matchingRenderer = null;
            matchingName = null;
            var count = 0;
            foreach (var renderer in renderers)
            {
                var mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null) continue;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var name = mesh.GetBlendShapeName(index);
                    var matches = exact ? string.Equals(name, requestedName, StringComparison.Ordinal)
                        : name.IndexOf(requestedName, StringComparison.Ordinal) >= 0;
                    if (!matches) continue;
                    count++;
                    matchingRenderer = renderer;
                    matchingName = name;
                    if (count > 1) return count;
                }
            }
            return count;
        }

        private static void ClearConfiguration(MfccLipSync lipSync)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(lipSync, "Configure MFCC lip sync");
#endif
            lipSync.ResetViseme();
            lipSync.TargetRenderer = null;
            lipSync.Mappings = Array.Empty<VisemeMapping>();
            lipSync.RebuildMappings();
            MarkSetupDirty(lipSync);
        }

        private static void MarkSetupDirty(Component component)
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return;
            UnityEditor.EditorUtility.SetDirty(component);
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (component.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
#endif
        }
    }
}
