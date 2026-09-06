using ChatdollKit.Avatar.LipSync;
using System;
using System.Collections.Generic;
using UnityEngine;
using VRM;

namespace ChatdollKit.Extension.VRM
{
    /// <summary>Configure MFCC mouth mappings from a VRM 0.x avatar's vowel clips.</summary>
    [AddComponentMenu("ChatdollKit/VRM/VRM MFCC Lip Sync Helper")]
    public sealed class VRMMfccLipSyncHelper : MfccLipSyncHelper
    {
        protected override VisemeMapping[] CreateMappings(GameObject avatarObject)
        {
            var proxy = avatarObject != null ? avatarObject.GetComponent<VRMBlendShapeProxy>() : null;
            var clips = proxy != null && proxy.BlendShapeAvatar != null ? proxy.BlendShapeAvatar.Clips : null;
            if (clips == null) return Array.Empty<VisemeMapping>();

            // Presets are authoritative. Legacy clips named A/I/U/E/O are a fallback only.
            var hasPreset = new bool[6];
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                var viseme = FromPreset(clip.Preset);
                if (viseme != Viseme.None) hasPreset[(int)viseme] = true;
            }

            var mappings = new List<VisemeMapping>();
            foreach (var clip in clips)
            {
                if (clip == null || clip.Values == null) continue;
                var viseme = FromPreset(clip.Preset);
                if (viseme == Viseme.None && clip.Preset == BlendShapePreset.Unknown)
                {
                    viseme = FromName(clip.BlendShapeName);
                    if (viseme != Viseme.None && hasPreset[(int)viseme]) continue;
                }
                if (viseme == Viseme.None) continue;

                foreach (var binding in clip.Values)
                {
                    var transform = string.IsNullOrEmpty(binding.RelativePath)
                        ? avatarObject.transform : avatarObject.transform.Find(binding.RelativePath);
                    var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                    var mesh = renderer != null ? renderer.sharedMesh : null;
                    if (mesh == null || binding.Index < 0 || binding.Index >= mesh.blendShapeCount
                        || float.IsNaN(binding.Weight) || float.IsInfinity(binding.Weight)) continue;

                    mappings.Add(new VisemeMapping
                    {
                        Viseme = viseme,
                        Renderer = renderer,
                        BlendShape = mesh.GetBlendShapeName(binding.Index),
                        MaxWeight = Mathf.Clamp(binding.Weight, 0, 100)
                    });
                }
            }
            return mappings.ToArray();
        }

        private static Viseme FromPreset(BlendShapePreset preset)
        {
            switch (preset)
            {
                case BlendShapePreset.A: return Viseme.A;
                case BlendShapePreset.I: return Viseme.I;
                case BlendShapePreset.U: return Viseme.U;
                case BlendShapePreset.E: return Viseme.E;
                case BlendShapePreset.O: return Viseme.O;
                default: return Viseme.None;
            }
        }

        private static Viseme FromName(string name)
        {
            switch (name?.ToUpperInvariant())
            {
                case "A": return Viseme.A;
                case "I": return Viseme.I;
                case "U": return Viseme.U;
                case "E": return Viseme.E;
                case "O": return Viseme.O;
                default: return Viseme.None;
            }
        }
    }
}
