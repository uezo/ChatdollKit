using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChatdollKit.UI;
using ChatdollKit.UI.ConversationControls;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ConversationControlsLayoutTests
    {
        private GameObject root;
        private readonly List<RenderTexture> renderTextures = new List<RenderTexture>();
        private readonly List<Object> assets = new List<Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Conversation controls layout test");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            yield return null;
            foreach (var texture in renderTextures)
            {
                if (texture == null) continue;
                texture.Release(); Object.Destroy(texture);
            }
            foreach (var asset in assets) if (asset != null) Object.Destroy(asset);
            renderTextures.Clear(); assets.Clear();
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator PrefabStartsWithHiddenActiveInputAndFourOrderedDeviceButtons()
        {
            var layout = CreatePrefab();
            var input = layout.GetComponentInChildren<ConversationInput>(true);
            var microphone = layout.GetComponentInChildren<MicrophoneControl>(true);
            var speaker = layout.GetComponentInChildren<SpeakerControl>(true);
            var camera = layout.GetComponentInChildren<CameraButton>(true);
            Assert.That(input, Is.Not.Null);
            Assert.That(microphone, Is.Not.Null);
            Assert.That(speaker, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);
            Assert.That(layout.KeyboardButton, Is.Not.Null);
            Assert.That(layout.Input, Is.SameAs(input.Input));
            Assert.That(layout.IsInputVisible, Is.False);
            AssertHiddenButActive(layout, input);
            var buttons = new[] { microphone.MuteButton.transform, speaker.MuteButton.transform,
                camera.transform, layout.KeyboardButton.transform };
            var directChildren = buttons.Select(button => DirectChild(layout.ButtonsGroup, button)).ToArray();
            Assert.That(directChildren.Distinct().Count(), Is.EqualTo(4));
            Assert.That(layout.ButtonsGroup.childCount, Is.EqualTo(4));
            Assert.That(directChildren.Select(child => child.GetSiblingIndex()), Is.EqualTo(new[] { 0, 1, 2, 3 }),
                "The row must read microphone, speaker, camera, keyboard from left to right.");
            Assert.That(input.ImagePicker.transform.IsChildOf(layout.InputPanel), Is.True);
            Assert.That(input.ImagePicker.transform.IsChildOf(layout.ButtonsGroup), Is.False);
            yield return null;
            AssertHiddenButActive(layout, input);
        }

        [UnityTest]
        public IEnumerator KeyboardTogglePreservesTextAndAttachmentAndClosesTheImagePathPanel()
        {
            var layout = CreatePrefab();
            var input = layout.GetComponentInChildren<ConversationInput>(true);
            layout.KeyboardButton.onClick.Invoke();
            Assert.That(layout.IsInputVisible, Is.True);
            Assert.That(layout.InputVisibility.alpha, Is.EqualTo(1f));
            Assert.That(layout.InputVisibility.interactable, Is.True);
            Assert.That(layout.InputVisibility.blocksRaycasts, Is.True);
            input.Input.text = "Keep the draft when the drawer closes";
            input.SetImage(ImageBytes());
            var image = input.ImagePreview.sprite;
            Assert.That(layout.ImagePathPanel, Is.Not.Null);
            layout.ImagePathPanel.SetActive(true);
            layout.KeyboardButton.onClick.Invoke();
            AssertHiddenButActive(layout, input);
            Assert.That(layout.ImagePathPanel.activeSelf, Is.False);
            Assert.That(input.Input.text, Is.EqualTo("Keep the draft when the drawer closes"));
            Assert.That(input.ImagePreview.sprite, Is.SameAs(image));
            Assert.That(input.HasImage, Is.True);
            yield return null;
            layout.KeyboardButton.onClick.Invoke();
            Assert.That(layout.IsInputVisible, Is.True);
            Assert.That(input.Input.text, Is.EqualTo("Keep the draft when the drawer closes"));
            Assert.That(input.ImagePreview.sprite, Is.SameAs(image));
            Assert.That(input.isActiveAndEnabled, Is.True);
        }

        [UnityTest]
        public IEnumerator PortraitAndLandscapeUseViewportWidthAndCenterTheButtonRowAtTheBottom()
        {
            var landscapeCanvas = CreateCanvas(1600, 900);
            var portraitCanvas = CreateCanvas(360, 640);
            var landscape = CreateLayout(landscapeCanvas);
            var portrait = CreateLayout(portraitCanvas);
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var layout in new[] { landscape, portrait }) layout.RefreshLayout();
            Assert.That(landscape.InputPanel.rect.width, Is.EqualTo(1592f).Within(0.1f));
            Assert.That(portrait.InputPanel.rect.width, Is.EqualTo(352f).Within(0.1f));
            Assert.That(portrait.InputPanel.rect.width, Is.GreaterThan(360f * 0.95f));
            foreach (var layout in new[] { landscape, portrait })
            {
                var canvas = layout.GetComponent<Canvas>();
                Assert.That(layout.ButtonsGroup.rect.width, Is.EqualTo(286f).Within(0.1f));
                Assert.That(layout.ButtonsGroup.rect.height, Is.EqualTo(76f).Within(0.1f));
                Assert.That(layout.InputPanel.rect.height, Is.EqualTo(76f).Within(0.1f));
                var rowBottom = ScreenEdge(layout.ButtonsGroup, canvas.worldCamera, false);
                var inputBottom = ScreenEdge(layout.InputPanel, canvas.worldCamera, false);
                Assert.That(rowBottom.x, Is.EqualTo(canvas.pixelRect.center.x).Within(0.5f));
                Assert.That(rowBottom.y, Is.EqualTo(canvas.pixelRect.yMin + layout.BottomMargin).Within(0.5f));
                Assert.That(inputBottom.y, Is.EqualTo(canvas.pixelRect.yMin + layout.InputMargin).Within(0.5f));
            }
        }

        [UnityTest]
        public IEnumerator OpeningInputMovesButtonsAboveItAndClosingRestoresTheirViewportMargin()
        {
            var canvas = CreateCanvas(1200, 800, 2f, new Rect(0.1f, 0.1f, 0.8f, 0.8f));
            var layout = CreateLayout(canvas);
            yield return null;
            Canvas.ForceUpdateCanvases();
            layout.RefreshLayout();
            var closedBottom = ScreenEdge(layout.ButtonsGroup, canvas.worldCamera, false);
            Assert.That(closedBottom.y, Is.EqualTo(canvas.pixelRect.yMin + layout.BottomMargin * canvas.scaleFactor).Within(0.5f));
            layout.SetInputVisible(true);
            var inputTop = ScreenEdge(layout.InputPanel, canvas.worldCamera, true);
            var openBottom = ScreenEdge(layout.ButtonsGroup, canvas.worldCamera, false);
            Assert.That(openBottom.y, Is.EqualTo(inputTop.y + layout.InputGap * canvas.scaleFactor).Within(0.5f));
            Assert.That(openBottom.x, Is.EqualTo(canvas.pixelRect.center.x).Within(0.5f));
            Assert.That(layout.InputPanel.rect.width * canvas.scaleFactor,
                Is.EqualTo(canvas.pixelRect.width - 2f * layout.InputMargin * canvas.scaleFactor).Within(0.5f));
            // Texture previews must use their own camera viewport instead of the desktop screen's safe area.
            var position = layout.ButtonsGroup.anchoredPosition;
            layout.RespectSafeArea = false;
            layout.RefreshLayout();
            Assert.That(layout.ButtonsGroup.anchoredPosition, Is.EqualTo(position));
            layout.SetInputVisible(false);
            var restored = ScreenEdge(layout.ButtonsGroup, canvas.worldCamera, false);
            Assert.That(restored.x, Is.EqualTo(closedBottom.x).Within(0.5f));
            Assert.That(restored.y, Is.EqualTo(closedBottom.y).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator KeyboardListenersRemainSingleAcrossEnableAndButtonReplacement()
        {
            var layout = CreatePrefab();
            var original = layout.KeyboardButton;
            for (var i = 0; i < 3; i++)
            {
                layout.enabled = false;
                original.onClick.Invoke();
                Assert.That(layout.IsInputVisible, Is.False);
                layout.enabled = true;
            }
            original.onClick.Invoke();
            Assert.That(layout.IsInputVisible, Is.True, "Duplicate toggle listeners would close the drawer again.");
            var replacement = CreateRect(layout.transform, "Replacement keyboard").gameObject.AddComponent<Button>();
            layout.KeyboardButton = replacement;
            yield return null;
            original.onClick.Invoke();
            Assert.That(layout.IsInputVisible, Is.True);
            replacement.onClick.Invoke();
            Assert.That(layout.IsInputVisible, Is.False);
        }

        private ConversationControlsLayout CreatePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ChatdollKit/Prefabs/Runtime/Orchestration/UIControlHorizontal.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab, root.transform);
            var layout = instance.GetComponentInChildren<ConversationControlsLayout>(true);
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.ButtonsGroup, Is.Not.Null);
            Assert.That(layout.InputPanel, Is.Not.Null);
            Assert.That(layout.InputVisibility, Is.Not.Null);
            return layout;
        }

        private static void AssertHiddenButActive(ConversationControlsLayout layout, ConversationInput input)
        {
            Assert.That(layout.IsInputVisible, Is.False);
            Assert.That(layout.InputVisibility.alpha, Is.Zero);
            Assert.That(layout.InputVisibility.interactable, Is.False);
            Assert.That(layout.InputVisibility.blocksRaycasts, Is.False);
            Assert.That(layout.InputPanel.gameObject.activeInHierarchy, Is.True);
            Assert.That(input.isActiveAndEnabled, Is.True, "Hiding must not disable request completion or input subscriptions.");
        }

        private static Transform DirectChild(Transform parent, Transform descendant)
        {
            Assert.That(descendant.IsChildOf(parent), Is.True);
            while (descendant.parent != parent) descendant = descendant.parent;
            return descendant;
        }

        private Canvas CreateCanvas(int width, int height, float scale = 1f, Rect? viewport = null)
        {
            var texture = new RenderTexture(width, height, 0); renderTextures.Add(texture);
            var cameraObject = new GameObject("Controls layout camera", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false; camera.targetTexture = texture;
            camera.rect = viewport ?? new Rect(0, 0, 1, 1);
            var owner = new GameObject("Controls layout canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            owner.transform.SetParent(root.transform, false);
            var canvas = owner.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            var scaler = owner.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = scale;
            return canvas;
        }

        private static ConversationControlsLayout CreateLayout(Canvas canvas)
        {
            var layout = canvas.gameObject.AddComponent<ConversationControlsLayout>();
            layout.ButtonsGroup = CreateRect(canvas.transform, "Buttons");
            layout.InputPanel = CreateRect(canvas.transform, "Input bar");
            layout.InputVisibility = layout.InputPanel.gameObject.AddComponent<CanvasGroup>();
            layout.SetInputVisible(false);
            return layout;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var owner = new GameObject(name, typeof(RectTransform));
            owner.transform.SetParent(parent, false);
            return (RectTransform)owner.transform;
        }

        private static Vector2 ScreenEdge(RectTransform rect, Camera camera, bool top)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return RectTransformUtility.WorldToScreenPoint(camera,
                top ? (corners[1] + corners[2]) * 0.5f : (corners[0] + corners[3]) * 0.5f);
        }

        private byte[] ImageBytes()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false); assets.Add(texture);
            texture.SetPixels(Enumerable.Repeat(Color.cyan, 16).ToArray()); texture.Apply();
            return texture.EncodeToPNG();
        }
    }
}
