using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChatdollKit.Avatar.LipSync
{
    [Serializable]
    public sealed class VisemeMapping
    {
        public Viseme Viseme;
        public string BlendShape = "";
        [Tooltip("Optional renderer for this binding. When empty, use Target Renderer.")]
        public SkinnedMeshRenderer Renderer;
        [Range(0, 100)] public float MaxWeight = 100;
    }
}
