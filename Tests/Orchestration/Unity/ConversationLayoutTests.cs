using System.Collections;
using System.Collections.Generic;
using ChatdollKit.UI.MessageWindow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ConversationLayoutTests
    {
        private GameObject root;
        private readonly List<RenderTexture> textures = new List<RenderTexture>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Conversation layout test");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            yield return null;
            foreach (var texture in textures)
            {
                if (texture == null) continue;
                texture.Release();
                Object.Destroy(texture);
            }
            textures.Clear();
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator LandscapeUsesMaximumWidthAndPortraitKeepsNearlyTheFullViewportWidth()
        {
            var landscape = CreateLayout(CreateCanvas(1600, 900));
            var portrait = CreateLayout(CreateCanvas(360, 640));
            yield return null;
            Canvas.ForceUpdateCanvases();
            landscape.RefreshLayout();
            portrait.RefreshLayout();
            Assert.That(landscape.Panel.rect.width, Is.EqualTo(960f).Within(0.1f));
            Assert.That(portrait.Panel.rect.width, Is.EqualTo(328f).Within(0.1f));
            Assert.That(portrait.Panel.rect.width, Is.GreaterThan(0.9f * 360f));
            Assert.That(landscape.Panel.rect.height, Is.EqualTo(184f).Within(0.1f));
            Assert.That(portrait.Panel.rect.height, Is.EqualTo(184f).Within(0.1f));
            Assert.That(portrait.Panel.pivot, Is.EqualTo(new Vector2(0.5f, 0f)));
        }

        [UnityTest]
        public IEnumerator BottomAnchorAcrossDifferentCanvasScalesAndCameraViewportsUsesItsScreenTop()
        {
            var canvas = CreateCanvas(1200, 800, 2f);
            var anchorCanvas = CreateCanvas(1200, 800, 0.75f, new Rect(0.1f, 0.1f, 0.8f, 0.8f));
            var anchor = CreateRect(anchorCanvas.transform, "Buttons");
            anchor.anchorMin = anchor.anchorMax = new Vector2(0.5f, 0f);
            anchor.pivot = new Vector2(0.5f, 0f);
            anchor.anchoredPosition = new Vector2(0f, 40f);
            anchor.sizeDelta = new Vector2(240f, 48f);
            var layout = CreateLayout(canvas);
            layout.BottomAnchor = anchor;
            layout.AnchorGap = 16f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            layout.RefreshLayout();

            var anchorTop = ScreenEdge(anchor, anchorCanvas.worldCamera, true);
            var panelBottom = ScreenEdge(layout.Panel, canvas.worldCamera, false);
            Assert.That(panelBottom.y, Is.EqualTo(anchorTop.y + 16f * canvas.scaleFactor).Within(0.5f));
            Assert.That(panelBottom.x, Is.EqualTo(canvas.pixelRect.center.x).Within(0.5f));
            Assert.That(layout.Panel.rect.height, Is.EqualTo(184f).Within(0.1f));
        }

        [UnityTest]
        public IEnumerator DefaultBottomMarginUsesCameraViewportAndTexturePreviewIgnoresDeviceSafeArea()
        {
            var canvas = CreateCanvas(1200, 1000, 1f, new Rect(0.25f, 0.15f, 0.5f, 0.6f));
            var layout = CreateLayout(canvas);
            layout.BottomMargin = 30f;
            layout.PreferredHeight = 100f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            layout.RefreshLayout();
            var panelBottom = ScreenEdge(layout.Panel, canvas.worldCamera, false);
            var viewport = canvas.pixelRect;
            Assert.That(viewport.width, Is.EqualTo(600f).Within(0.1f));
            Assert.That(layout.Panel.rect.width, Is.EqualTo(viewport.width - 32f).Within(0.1f));
            Assert.That(panelBottom.y, Is.EqualTo(viewport.yMin + 30f).Within(0.5f));
            Assert.That(panelBottom.x, Is.EqualTo(viewport.center.x).Within(0.5f));
            var position = layout.Panel.anchoredPosition;
            var size = layout.Panel.sizeDelta;
            layout.RespectSafeArea = false;
            layout.RefreshLayout();
            Assert.That(layout.Panel.anchoredPosition, Is.EqualTo(position));
            Assert.That(layout.Panel.sizeDelta, Is.EqualTo(size));
        }

        [UnityTest]
        public IEnumerator SmallViewportsAndInvalidSizesProduceFiniteNonnegativePanels()
        {
            var canvas = CreateCanvas(100, 80);
            var layout = CreateLayout(canvas);
            yield return null;
            Canvas.ForceUpdateCanvases();
            layout.RefreshLayout();
            Assert.That(layout.Panel.rect.width, Is.EqualTo(68f).Within(0.1f));
            Assert.That(layout.Panel.rect.height, Is.Zero.Within(0.1f));
            Assert.That(ScreenEdge(layout.Panel, canvas.worldCamera, false).y,
                Is.EqualTo(canvas.pixelRect.yMax).Within(0.5f));

            layout.SideMargin = 1000f;
            layout.BottomMargin = 30f;
            layout.PreferredHeight = 1000f;
            layout.RefreshLayout();
            Assert.That(layout.Panel.rect.width, Is.Zero.Within(0.1f));
            Assert.That(layout.Panel.rect.height, Is.EqualTo(50f).Within(0.1f));
            layout.MaxWidth = -10f;
            layout.SideMargin = -10f;
            layout.PreferredHeight = float.PositiveInfinity;
            layout.BottomMargin = float.NaN;
            layout.RefreshLayout();
            Assert.That(layout.Panel.rect.width, Is.Zero.Within(0.1f));
            Assert.That(float.IsNaN(layout.Panel.anchoredPosition.y) || float.IsInfinity(layout.Panel.anchoredPosition.y), Is.False);
            Assert.That(layout.Panel.rect.height, Is.GreaterThanOrEqualTo(0f));
            layout.Panel = null;
            Assert.DoesNotThrow(layout.RefreshLayout);
        }

        private Canvas CreateCanvas(int width, int height, float scale = 1f, Rect? viewport = null)
        {
            var texture = new RenderTexture(width, height, 0);
            textures.Add(texture);
            var cameraObject = new GameObject("Layout camera", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = texture;
            camera.rect = viewport ?? new Rect(0f, 0f, 1f, 1f);
            var owner = new GameObject("Layout canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            owner.transform.SetParent(root.transform, false);
            var canvas = owner.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var scaler = owner.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = scale;
            return canvas;
        }

        private static MessageWindowLayout CreateLayout(Canvas canvas)
        {
            var layout = canvas.gameObject.AddComponent<MessageWindowLayout>();
            layout.Panel = CreateRect(canvas.transform, "Message panel");
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
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return RectTransformUtility.WorldToScreenPoint(camera,
                top ? (corners[1] + corners[2]) * 0.5f : (corners[0] + corners[3]) * 0.5f);
        }
    }
}
