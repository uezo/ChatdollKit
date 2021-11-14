using UnityEditor;
using ChatdollKit;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;

[CustomEditor(typeof(ChatdollApplication))]
public class ChatdollEditor : Editor
{
    // Remove ChatdollKit components and objects
    [MenuItem("CONTEXT/ChatdollApplication/Remove ChatdollKit components")]
    private static void RemoveComponents(MenuCommand menuCommand)
    {
        if (!EditorUtility.DisplayDialog("Confirmation", "Are you sure to remove all ChatdollKit components?", "OK", "Cancel"))
        {
            return;
        }

        var chatdoll = menuCommand.context as ChatdollApplication;
        var gameObject = chatdoll.gameObject;

        // Main Application
        DestroyComponents(gameObject.GetComponents<ChatdollApplication>());

        // RequestProviders and WakeWordListener
        DestroyComponents(gameObject.GetComponents<IRequestProvider>());
        DestroyComponents(gameObject.GetComponents<WakeWordListenerBase>());

        // Microphone (Voice recorder depends on this)
        DestroyComponents(gameObject.GetComponents<ChatdollMicrophone>());

        // Voice loaders
        DestroyComponents(gameObject.GetComponents<IVoiceLoader>());

        // Prompter
        DestroyComponents(gameObject.GetComponents<HttpPrompter>());

        // Router
        DestroyComponents(gameObject.GetComponents<ISkillRouter>());

        // LipSyncHelper
        DestroyComponents(gameObject.GetComponents<ILipSyncHelper>());

        // Skills
        DestroyComponents(gameObject.GetComponents<ISkill>());

        // ModelController
        foreach (var c in gameObject.GetComponents<ModelController>())
        {
            // VoiceAudio Control Object
            DestroyImmediate(c.AudioSource.gameObject);
            // ModelController itself
            DestroyImmediate(c);
        }
    }

    public static void DestroyComponents(object[] components)
    {
        foreach (var c in components)
        {
            DestroyImmediate(c as UnityEngine.Object);
        }
    }
}
