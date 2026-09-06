using ChatdollKit.Orchestration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class OrchestratorInspectorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OptionsUseThreeVisibleRowsRegardlessOfSavedFoldoutState(bool expanded)
        {
            var owner = new GameObject("Orchestrator Inspector test")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                var orchestrator = owner.AddComponent<ChatdollOrchestrator>();
                using (var serializedOrchestrator = new SerializedObject(orchestrator))
                {
                    var options = serializedOrchestrator.FindProperty(nameof(ChatdollOrchestrator.Options));
                    options.isExpanded = expanded;

                    // Ask Unity to resolve the actual registered drawer. Calling the drawer directly
                    // would miss an import failure and pass while users still see the old foldout.
                    var height = EditorGUI.GetPropertyHeight(options, true);
                    var threeRows = EditorGUIUtility.singleLineHeight * 3
                        + EditorGUIUtility.standardVerticalSpacing * 2;

                    Assert.That(height, Is.EqualTo(threeRows).Within(0.01f),
                        "The Options Inspector should display Allow Barge In and both queue limits "
                        + "without a foldout. Verify that Unity imported the Orchestration Editor drawer.");
                }
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
