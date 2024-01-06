using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor.Animations;
using UnityEditor;
using uLipSync;
using ChatdollKit.Model;

[CustomEditor(typeof(ModelController))]
public class FaceClipEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var app = target as ModelController;

        if (EditorApplication.isPlaying)
        {
            if (GUILayout.Button("Change Avatar"))
            {
                app.SetAvatar(activation: true);
            }

            GUILayout.Space(20.0f);
        }

        base.OnInspectorGUI();
    }

    // Setup ModelController
    [MenuItem("CONTEXT/ModelController/Setup ModelController")]
    private static void Setup(MenuCommand menuCommand)
    {
        var modelController = menuCommand.context as ModelController;

        if (modelController.AvatarModel == null)
        {
            // Get target avator model to control
            var animators = FindObjectsOfType<Animator>().Where(a => a.isHuman);
            if (animators.Count() == 1)
            {
                modelController.AvatarModel = animators.First().gameObject;
            }
            else if (animators.Count() > 1)
            {
                var animator = modelController.gameObject.GetComponentInParent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    modelController.AvatarModel = animator.gameObject;
                }
                else
                {
                    Debug.LogError($"{animators.Count()} avatars found. Set 3D model to setup to AvatorModel of ModelController.");
                    return;
                }
            }
            else
            {
                Debug.LogError("Set AvatorModel to ModelController before setup.");
                return;
            }
        }

        // Set skinned mesh renderer before configure facial expression and lipsync
        var facialSkinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(modelController.AvatarModel);
        // TODO: Remove SkinnedMeshRenderer from ModelController
        modelController.SkinnedMeshRenderer = facialSkinnedMeshRenderer;
        EditorUtility.SetDirty(modelController);

        // Configure uLipSyncHelper
        var lipSyncHelper = modelController.gameObject.GetComponent<uLipSyncHelper>();
        lipSyncHelper.ConfigureViseme(modelController.AvatarModel);
        EditorUtility.SetDirty(modelController.gameObject.GetComponent<uLipSyncBlendShape>());

        // Blink and FaceExpression
        var blinker = modelController.gameObject.GetComponent<Blink>();
        var faceProxy = modelController.gameObject.GetComponent<VRCFaceExpressionProxy>();

        if (blinker == null && faceProxy == null)
        {
            // Nothing to set SkinnedMeshRenderer
            return;
        }

        if (facialSkinnedMeshRenderer != null)
        {
            // Set blink target
            if (blinker != null)
            {
                blinker.Setup(modelController.AvatarModel);
                EditorUtility.SetDirty(blinker);
            }

            // Set facial skinned mesh renderer if VRC Avator
            if (faceProxy != null)
            {
                faceProxy.SkinnedMeshRenderer = modelController.SkinnedMeshRenderer;
            }
            EditorUtility.SetDirty(faceProxy);
        }
        else
        {
            Debug.LogError("SkinnedMeshRenderer that has facial shapekeys not found.");
        }
    }

    // Setup Animator
    [MenuItem("CONTEXT/ModelController/Setup Animator")]
    private static void CreateAnimationControllerWithClips(MenuCommand menuCommand)
    {
        var modelController = menuCommand.context as ModelController;

        var animationClipFolderPath = EditorUtility.OpenFolderPanel("Select animation clip parent folder", Application.dataPath, string.Empty);
        if (!string.IsNullOrEmpty(animationClipFolderPath))
        {
            // Get animation clips from folder
            var animationClips = GetLayeredAnimationClips(animationClipFolderPath);

            // Return when no clips found
            if (animationClips.Count == 0)
            {
                Debug.LogWarning("No animation clips found");
                return;
            }

            // Make path to create new animator controller
            var animatorControllerPath = "Assets" + animationClipFolderPath.Replace(Application.dataPath, string.Empty);
            animatorControllerPath = Path.Combine(animatorControllerPath, $"{modelController.AvatarModel.gameObject.name}.controller");

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(animatorControllerPath) != null)
            {
                // Confirm overwrite when exists
                if (!EditorUtility.DisplayDialog("AnimatorController exists", $"AnimatorController already exists at {animatorControllerPath}. Are you sure to overwrite?", "OK", "Cancel"))
                {
                    return;
                }
            }

            // Create new animator controller and layer
            var animatorController = AnimatorController.CreateAnimatorControllerAtPath(animatorControllerPath);
            var baseLayer = animatorController.layers[0];
            animatorController.AddLayer("Additive Layer");
            var additiveLayer = animatorController.layers.Last();

            // TODO: Apply and persist these values. I don't know why they are not applied :(
            additiveLayer.blendingMode = AnimatorLayerBlendingMode.Additive;
            additiveLayer.defaultWeight = 1.0f;

            animatorController.AddParameter("BaseParam", AnimatorControllerParameterType.Int);
            animatorController.AddParameter("AdditiveParam", AnimatorControllerParameterType.Int);
            var paramCounter = new Dictionary<string, int>()
            {
                { "BaseParam", 0 },
                { "AdditiveParam", 0 },
            };

            // Put animation clips on layers
            foreach (var kv in animationClips)
            {
                // Select layer
                var layerName = kv.Key;
                var putOnBaseLayer = layerName == "Base layer" || EditorUtility.DisplayDialog("Select Layer", $"{kv.Value.Count} clips found in {layerName}. Put these clips on ...", "Base Layer", "Additive Layer");
                var layer = putOnBaseLayer ? baseLayer : additiveLayer;

                // Create default state
                AnimatorState defaultState = default;
                if (layer.stateMachine.states.Select(st => st.state.name).Contains("Default"))
                {
                    defaultState = layer.stateMachine.states.Where(st => st.state.name == "Default").First().state;
                }
                else
                {
                    defaultState = layer.stateMachine.AddState("Default");
                }

                // Put animation clips on layer with transition
                foreach (var clip in kv.Value)
                {
                    // Create state
                    var state = layer.stateMachine.AddState(clip.name);
                    state.motion = clip;

                    // Make transitions
                    if (putOnBaseLayer)
                    {
                        var anyToState = layer.stateMachine.AddAnyStateTransition(state);
                        anyToState.duration = modelController.AnimationFadeLength;
                        anyToState.exitTime = 0;
                        anyToState.canTransitionToSelf = false;
                        anyToState.AddCondition(AnimatorConditionMode.Equals, paramCounter["BaseParam"], "BaseParam");

                        var stateToDefault = state.AddTransition(defaultState);
                        stateToDefault.duration = modelController.AnimationFadeLength;
                        stateToDefault.exitTime = 0;
                        stateToDefault.AddCondition(AnimatorConditionMode.NotEqual, paramCounter["BaseParam"], "BaseParam");
                    }

                    paramCounter[putOnBaseLayer ? "BaseParam" : "AdditiveParam"] += 1;
                }
            }

            // Set controller to animator
            var animator = modelController.AvatarModel.gameObject.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = animatorController;
            }
        }
    }

    private static Dictionary<string, List<AnimationClip>> GetLayeredAnimationClips(string parentPath)
    {
        var animationClips = new Dictionary<string, List<AnimationClip>>();

        var directories = Directory.GetDirectories(parentPath);
        if (directories.Length > 0)
        {
            foreach (var d in directories)
            {
                var clips = GetAnimationClips(d);
                if (clips.Count > 0)
                {
                    animationClips[d.Split('/').Last()] = clips;
                }
            }
        }
        else
        {
            var clips = GetAnimationClips(parentPath);
            if (clips.Count > 0)
            {
                animationClips["Base Layer"] = clips;
            }
        }

        return animationClips;
    }

    private static List<AnimationClip> GetAnimationClips(string folderPath)
    {
        var clips = new List<AnimationClip>();
        var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
        foreach (var f in files)
        {
            var assetPath = "Assets" + f.Replace(Application.dataPath, string.Empty);
            var a = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (a != null)
            {
                clips.Add(a);
            }
        }
        return clips;
    }

    private static SkinnedMeshRenderer GetFacialSkinnedMeshRenderer(SkinnedMeshRenderer[] skinnedMeshRenderers)
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
