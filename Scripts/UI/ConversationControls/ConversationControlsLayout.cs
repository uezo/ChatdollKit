using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.UI.ConversationControls
{
    /// <summary>Positions the device buttons and an optional bottom input drawer.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/UI/Conversation Controls Layout")]
    public sealed class ConversationControlsLayout : MonoBehaviour
    {
        public RectTransform ButtonsGroup;
        public RectTransform InputPanel;
        public CanvasGroup InputVisibility;
        public Button KeyboardButton;
        public InputField Input;
        public GameObject ImagePathPanel;
        [Min(0)] public float BottomMargin = 8;
        [Min(0)] public float InputMargin = 4;
        [Min(1)] public float InputHeight = 76;
        [Min(1)] public float ButtonRowHeight = 76;
        [Min(1)] public float ButtonRowWidth = 286;
        [Min(0)] public float InputGap = 8;
        public bool RespectSafeArea = true;
        [SerializeField] private bool inputVisible;
        public bool IsInputVisible => inputVisible;

        private Button boundKeyboard;

        private void OnEnable() { Bind(); ApplyVisibility(); RefreshLayout(); }
        private void OnDisable()
        {
            if (boundKeyboard != null) boundKeyboard.onClick.RemoveListener(ToggleInput);
            boundKeyboard = null;
            if (Input != null) Input.DeactivateInputField();
        }
        private void LateUpdate() { Bind(); RefreshLayout(); }

        private void Bind()
        {
            if (boundKeyboard == KeyboardButton) return;
            if (boundKeyboard != null) boundKeyboard.onClick.RemoveListener(ToggleInput);
            boundKeyboard = KeyboardButton;
            if (boundKeyboard != null) boundKeyboard.onClick.AddListener(ToggleInput);
        }

        public void ToggleInput() => SetInputVisible(!inputVisible);

        public void SetInputVisible(bool visible)
        {
            // InputField closes its native keyboard only while it is still interactable.
            if (!visible && Input != null) Input.DeactivateInputField();
            inputVisible = visible;
            ApplyVisibility();
            RefreshLayout();
            if (Input == null) return;
            if (visible)
            {
                Input.Select();
                Input.ActivateInputField();
            }
        }

        private void ApplyVisibility()
        {
            // Keep ConversationInput enabled so an in-flight submission can finish normally.
            if (InputVisibility != null)
            {
                InputVisibility.alpha = inputVisible ? 1 : 0;
                InputVisibility.interactable = inputVisible;
                InputVisibility.blocksRaycasts = inputVisible;
            }
            if (!inputVisible && ImagePathPanel != null) ImagePathPanel.SetActive(false);
        }

        public void RefreshLayout()
        {
            if (InputPanel != null)
                Place(InputPanel, InputMargin, InputHeight, float.PositiveInfinity, InputMargin);
            if (ButtonsGroup != null)
                Place(ButtonsGroup, inputVisible ? InputMargin + InputHeight + InputGap : BottomMargin,
                    ButtonRowHeight, ButtonRowWidth, BottomMargin);
        }

        private void Place(RectTransform target, float bottom, float height, float maxWidth, float sideMargin)
        {
            var parent = target.parent as RectTransform;
            var canvas = target.GetComponentInParent<Canvas>();
            if (parent == null || canvas == null) return;
            canvas = canvas.rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var pixels = canvas.pixelRect;
            if (pixels.width <= 0 || pixels.height <= 0) return;
            var usesTexture = camera != null && camera.targetTexture != null;
            if (RespectSafeArea && !usesTexture)
            {
                var safe = Screen.safeArea;
                pixels = Rect.MinMaxRect(Mathf.Max(pixels.xMin, safe.xMin), Mathf.Max(pixels.yMin, safe.yMin),
                    Mathf.Min(pixels.xMax, safe.xMax), Mathf.Min(pixels.yMax, safe.yMax));
            }
            if (inputVisible && !usesTexture && TouchScreenKeyboard.visible)
            {
                var keyboard = TouchScreenKeyboard.area;
                if (keyboard.width > 0 && keyboard.height > 0 && keyboard.yMin <= pixels.yMin)
                    pixels.yMin = Mathf.Min(pixels.yMax, Mathf.Max(pixels.yMin, keyboard.yMax));
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.min, camera, out var min) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.max, camera, out var max)) return;
            min = Vector2.Max(min, parent.rect.min);
            max = Vector2.Min(max, parent.rect.max);
            var width = Mathf.Max(0, Mathf.Min(maxWidth, max.x - min.x - 2 * Mathf.Max(0, sideMargin)));
            var availableHeight = Mathf.Max(0, max.y - min.y);
            height = Mathf.Clamp(height, 0, availableHeight);
            var y = Mathf.Clamp(bottom, 0, availableHeight - height);
            target.anchorMin = target.anchorMax = new Vector2(.5f, 0);
            target.pivot = new Vector2(.5f, 0);
            target.sizeDelta = new Vector2(width, height);
            target.anchoredPosition = new Vector2((min.x + max.x) * .5f - parent.rect.center.x,
                min.y - parent.rect.yMin + y);
        }
    }
}
