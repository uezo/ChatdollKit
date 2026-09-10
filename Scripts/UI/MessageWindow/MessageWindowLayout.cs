using UnityEngine;

namespace ChatdollKit.UI.MessageWindow
{
    /// <summary>Fits a message panel to the viewport and optionally places it above another UI element.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/UI/Message Window Layout")]
    public sealed class MessageWindowLayout : MonoBehaviour
    {
        public RectTransform Panel;
        public RectTransform BottomAnchor;
        [Min(0f)] public float MaxWidth = 960f;
        [Min(0f)] public float SideMargin = 16f;
        [Min(0f)] public float PreferredHeight = 184f;
        [Min(0f)] public float BottomMargin = 104f;
        [Min(0f)] public float AnchorGap = 16f;
        public bool RespectSafeArea = true;

        private readonly Vector3[] anchorCorners = new Vector3[4];

        private void OnEnable() => RefreshLayout();
        private void LateUpdate() => RefreshLayout();

        public void RefreshLayout()
        {
            if (Panel == null || !(Panel.parent is RectTransform parent)) return;
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            var camera = CanvasCamera(canvas);
            var viewport = canvas.pixelRect;
            var rendersToTexture = camera != null && camera.targetTexture != null;
            if (RespectSafeArea && !rendersToTexture) viewport = Intersect(viewport, Screen.safeArea);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, viewport.min, camera, out var minimum) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, viewport.max, camera, out var maximum) ||
                !Finite(minimum) || !Finite(maximum)) return;

            var left = Mathf.Min(minimum.x, maximum.x);
            var right = Mathf.Max(minimum.x, maximum.x);
            var lower = Mathf.Min(minimum.y, maximum.y);
            var upper = Mathf.Max(minimum.y, maximum.y);
            var width = Mathf.Min(NonNegative(MaxWidth, 960f),
                Mathf.Max(0f, right - left - 2f * NonNegative(SideMargin, 16f)));
            var bottom = lower + NonNegative(BottomMargin, 104f);

            if (BottomAnchor != null)
            {
                var anchorCanvas = BottomAnchor.GetComponentInParent<Canvas>();
                if (anchorCanvas != null)
                {
                    BottomAnchor.GetWorldCorners(anchorCorners);
                    var screen = RectTransformUtility.WorldToScreenPoint(CanvasCamera(anchorCanvas.rootCanvas),
                        (anchorCorners[1] + anchorCorners[2]) * 0.5f);
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out var anchorTop) && Finite(anchorTop))
                        bottom = anchorTop.y + NonNegative(AnchorGap, 16f);
                }
            }

            bottom = Mathf.Clamp(bottom, lower, upper);
            var height = Mathf.Min(NonNegative(PreferredHeight, 184f), Mathf.Max(0f, upper - bottom));
            Panel.anchorMin = Panel.anchorMax = new Vector2(0.5f, 0f);
            Panel.pivot = new Vector2(0.5f, 0f);
            Panel.sizeDelta = new Vector2(width, height);
            Panel.anchoredPosition = new Vector2((left + right) * 0.5f - parent.rect.center.x,
                bottom - parent.rect.yMin);
        }

        private static Camera CanvasCamera(Canvas canvas) =>
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        private static float NonNegative(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);

        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);

        private static Rect Intersect(Rect left, Rect right)
        {
            var min = Vector2.Min(Vector2.Max(left.min, right.min), left.max);
            var max = Vector2.Min(left.max, right.max);
            return Rect.MinMaxRect(min.x, min.y, Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }
    }
}
