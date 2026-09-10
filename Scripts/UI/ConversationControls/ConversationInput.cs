using System;
using System.Linq;
using System.Threading;
using ChatdollKit.IO;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.UI.ConversationControls
{
    /// <summary>Submits a text/image draft to an explicitly selected orchestrator.
    /// Conversation captions are supplied by the orchestrator's event source, not by this input.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/UI/Conversation Input")]
    public sealed class ConversationInput : MonoBehaviour
    {
        public ChatdollOrchestrator Orchestrator;
        public InputField Input;
        public Button SendButton;
        public Button ClearImageButton;
        public Image ImagePreview;
        public RectTransform TextRect;
        public RectTransform PlaceholderRect;
        public ChatdollKit.UI.ImageButton ImagePicker;
        [Tooltip("Optional camera. Only an explicitly captured still image is attached; submitting never opens the camera.")]
        public SimpleCamera Camera;
        public Text StatusText;
        [Min(1)] public int MaxImageDimension = 640;
        public bool IsSubmitting { get; private set; }
        public bool HasImage => attachment != null;
        public string LastError { get; private set; }

        private ConversationImage attachment;
        private long draftVersion, imageVersion, bindingVersion;
        private InputField boundInput;
        private Button boundSend, boundClear;
        private ChatdollKit.UI.ImageButton boundPicker;
        private RectTransform capturedTextRect, capturedPlaceholderRect;
        private Vector2 textOffset, placeholderOffset;

        private void OnEnable() { bindingVersion++; Bind(); Refresh(); }
        private void OnDisable() { bindingVersion++; Unbind(); }
        private void OnDestroy() { Unbind(); ReleaseImage(); }
        private void Update() { Bind(); Refresh(); }

        private void Bind()
        {
            if (boundInput == Input && boundSend == SendButton && boundClear == ClearImageButton && boundPicker == ImagePicker) return;
            Unbind();
            boundInput = Input; boundSend = SendButton; boundClear = ClearImageButton; boundPicker = ImagePicker;
            if (boundInput != null)
            {
#if UNITY_6000_0_OR_NEWER
                boundInput.onSubmit.AddListener(OnInputSubmit);
#else
                boundInput.onEndEdit.AddListener(OnLegacyEndEdit);
#endif
                boundInput.onValueChanged.AddListener(OnDraftChanged);
            }
            if (boundSend != null) boundSend.onClick.AddListener(Submit);
            if (boundClear != null) boundClear.onClick.AddListener(ClearImage);
            if (boundPicker != null) boundPicker.HandleImage += OnImageSelected;
        }

        private void Unbind()
        {
            if (boundInput != null)
            {
#if UNITY_6000_0_OR_NEWER
                boundInput.onSubmit.RemoveListener(OnInputSubmit);
#else
                boundInput.onEndEdit.RemoveListener(OnLegacyEndEdit);
#endif
                boundInput.onValueChanged.RemoveListener(OnDraftChanged);
            }
            if (boundSend != null) boundSend.onClick.RemoveListener(Submit);
            if (boundClear != null) boundClear.onClick.RemoveListener(ClearImage);
            if (boundPicker != null) boundPicker.HandleImage -= OnImageSelected;
            boundInput = null; boundSend = boundClear = null; boundPicker = null;
        }

        private void OnInputSubmit(string _) { if (Input != null && !Input.wasCanceled) Submit(); }
#if !UNITY_6000_0_OR_NEWER
        private void OnLegacyEndEdit(string value)
        {
            if (Input == null || Input.wasCanceled) return;
            var keyboard = Input.touchScreenKeyboard;
            if (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Done) { Submit(); return; }
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) Submit();
#endif
        }
#endif
        private void OnDraftChanged(string _) { draftVersion++; LastError = null; SetStatus(null); }
        private void OnImageSelected(byte[] bytes)
        {
            try { SetImage(bytes); }
            catch (Exception error) { ShowError(error.Message); }
        }

        public void SetImage(byte[] bytes)
        {
            // Decode before releasing the previous draft so a bad selection cannot erase it.
            var next = ConversationImage.Create(bytes, MaxImageDimension);
            ReleaseImage();
            attachment = next;
            imageVersion++;
            LastError = null;
            SetStatus(null);
            Refresh();
        }

        public void ClearImage()
        {
            ReleaseImage();
            imageVersion++;
            Refresh();
        }

        private void ReleaseImage()
        {
            if (attachment == null) return;
            if (ImagePreview != null && ImagePreview.sprite == attachment.Sprite) ImagePreview.sprite = null;
            attachment.Dispose();
            attachment = null;
        }

        public void Submit() => SubmitFromUi().Forget();
        private async UniTask SubmitFromUi() { await SubmitAsync(); }

        /// <summary>Waits for generation, not audio playback. A failed request preserves the draft.
        /// Disabling this UI never interrupts an accepted conversation.</summary>
        public async UniTask<bool> SubmitAsync(CancellationToken cancellationToken = default)
        {
            if (IsSubmitting) return false;
            var target = Orchestrator;
            if (target == null)
            {
                ShowError("Assign ConversationInput.Orchestrator to the conversation you want to use.");
                return false;
            }
            if (!target.IsRunning)
            {
                ShowError("The assigned conversation is not running. Start its ChatdollOrchestrator before sending.");
                return false;
            }
            var input = Input;
            var originalText = input != null ? input.text : "";
            var text = originalText.Trim();
            var camera = attachment == null ? Camera : null;
            byte[] bytes;
            try { bytes = attachment?.JpegBytes ?? (camera != null ? camera.GetStillImage() : null); }
            catch (Exception error) { ShowError(error.Message); return false; }
            if (string.IsNullOrEmpty(text) && (bytes == null || bytes.Length == 0)) return false;
            var request = new SpeechPipelineRequest
            {
                Text = text,
                ImageUrls = bytes != null && bytes.Length > 0
                    ? new[] { "data:image/jpeg;base64," + Convert.ToBase64String(bytes) } : Array.Empty<string>()
            };
            var originalDraft = draftVersion;
            var originalImage = imageVersion;
            var originalBinding = bindingVersion;
            var run = target.Orchestrator;
            IsSubmitting = true;
            LastError = null;
            SetStatus("Sending…");
            Refresh();
            try
            {
                var response = await target.InvokeAsync(request, cancellationToken);
                await UniTask.SwitchToMainThread();
                if (response?.Type == SpeechPipelineResponseType.Error)
                    throw new InvalidOperationException(response.Text ?? "The message could not be sent.");
                if (response?.Type == SpeechPipelineResponseType.Canceled)
                    throw new OperationCanceledException("The request was canceled.");
                if (!CanUpdateDraft(target, run, originalBinding)) return true;
                if (input != null && input == Input && originalDraft == draftVersion && input.text == originalText)
                {
                    input.SetTextWithoutNotify("");
                    draftVersion++;
                }
                if (originalImage == imageVersion)
                {
                    if (camera == null) ClearImage();
                    else if (camera != null && camera == Camera && bytes != null)
                    {
                        var current = camera.GetStillImage();
                        if (current != null && current.SequenceEqual(bytes)) camera.ClearStillImage();
                    }
                }
                SetStatus(null);
                return true;
            }
            catch (Exception error)
            {
                await UniTask.SwitchToMainThread();
                if (CanUpdateDraft(target, run, originalBinding))
                    ShowError(error is OperationCanceledException ? "The request was canceled. Your draft has been kept." : error.Message);
                return false;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                IsSubmitting = false;
                if (this != null && isActiveAndEnabled)
                {
                    if (!CanUpdateDraft(target, run, originalBinding) && LastError == null) SetStatus(null);
                    Refresh();
                }
            }
        }

        private bool CanUpdateDraft(ChatdollOrchestrator target, ChatdollOrchestratorEngine run, long version) =>
            this != null && isActiveAndEnabled && bindingVersion == version && Orchestrator == target &&
            target != null && ReferenceEquals(target.Orchestrator, run);

        private void ShowError(string message) { LastError = message; SetStatus(message); }
        private void SetStatus(string text) { if (StatusText != null) StatusText.text = text ?? ""; }

        private void Refresh()
        {
            if (SendButton != null) SendButton.interactable = !IsSubmitting && Orchestrator != null && Orchestrator.IsRunning;
            if (ImagePreview != null)
            {
                ImagePreview.sprite = attachment?.Sprite;
                ImagePreview.preserveAspect = true;
                ImagePreview.gameObject.SetActive(HasImage);
            }
            if (ClearImageButton != null) ClearImageButton.gameObject.SetActive(HasImage);
            UpdateTextOffset(TextRect, ref capturedTextRect, ref textOffset);
            UpdateTextOffset(PlaceholderRect, ref capturedPlaceholderRect, ref placeholderOffset);
        }

        private void UpdateTextOffset(RectTransform rect, ref RectTransform captured, ref Vector2 offset)
        {
            if (rect == null) return;
            if (captured != rect) { captured = rect; offset = rect.offsetMin; }
            rect.offsetMin = new Vector2(HasImage ? Mathf.Max(offset.x, 75) : offset.x, offset.y);
        }
    }
}
