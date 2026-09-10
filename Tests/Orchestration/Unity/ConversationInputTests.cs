using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ChatdollKit.Avatar;
using ChatdollKit.IO;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.UI;
using ChatdollKit.UI.ConversationControls;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Window = ChatdollKit.UI.MessageWindow.MessageWindow;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ConversationInputTests
    {
        private GameObject root;
        private ChatdollOrchestrator orchestrator;
        private FakePipeline pipeline;
        private readonly List<ChatdollOrchestrator> orchestrators = new List<ChatdollOrchestrator>();
        private readonly List<HeldRequest> heldRequests = new List<HeldRequest>();
        private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        private readonly List<Exception> errors = new List<Exception>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Conversation input test");
            orchestrator = CreateOrchestrator(out pipeline);
            yield return Wait(orchestrator.StartAsync());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var held in heldRequests) held.Complete();
            foreach (var instance in orchestrators)
                if (instance != null) yield return Wait(instance.StopAsync());
            if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
            foreach (var asset in assets)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            yield return null;
            heldRequests.Clear(); orchestrators.Clear(); assets.Clear(); errors.Clear();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ControlPrefabSendsTrimmedTextAndUserCaptionArrivesThroughOrchestratorEvents()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ChatdollKit/Prefabs/Runtime/Orchestration/UIControlHorizontal.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(prefab, root.transform);
            var control = instance.GetComponentInChildren<ConversationInput>(true);
            Assert.That(control, Is.Not.Null);
            Assert.That(control.Input, Is.Not.Null);
            Assert.That(control.SendButton, Is.Not.Null);
            Assert.That(control.ImagePicker, Is.Not.Null);
            Assert.That(control.ImagePreview, Is.Not.Null);
            Assert.That(control.Camera, Is.Not.Null);
            Assert.That(control.StatusText, Is.Not.Null);
            control.Orchestrator = orchestrator;
            var caption = CreateUserWindow();
            yield return null;
            control.Input.text = "  Hello from the input  ";
#if UNITY_6000_0_OR_NEWER
            control.Input.onSubmit.Invoke(control.Input.text);
#else
            control.SendButton.onClick.Invoke();
#endif
            yield return Until(() => pipeline.Requests.Count == 1 && !control.IsSubmitting);
            yield return Wait(orchestrator.DrainAsync());
            yield return Until(() => caption.MessageText.text == "Hello from the input");
            Assert.That(pipeline.Requests.Single().Text, Is.EqualTo("Hello from the input"));
            Assert.That(control.Input.text, Is.Empty);
            Assert.That(caption.SpeakerText.text, Is.EqualTo("User"));
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance.DisplayText, Is.EqualTo("Hello from the input"));
            control.Input.text = "  Sent with the button  ";
            control.SendButton.onClick.Invoke();
            yield return Until(() => pipeline.Requests.Count == 2 && !control.IsSubmitting);
            Assert.That(pipeline.Requests.Last().Text, Is.EqualTo("Sent with the button"));
            Assert.That(control.Camera.IsAlreadyStarted, Is.False);
            Assert.That(errors, Is.Empty);
        }

        [UnityTest]
        public IEnumerator MissingOrchestratorReportsConnectionGuidanceWithoutUsingANearbyRunningConversation()
        {
            var control = CreateInput();
            control.Orchestrator = null;
            control.Input.text = "  Keep this unconnected draft  ";
            control.SetImage(Png(Color.red));
            var image = control.ImagePreview.sprite;
            var submitted = control.SubmitAsync();
            yield return Wait(submitted);
            Assert.That(submitted.GetAwaiter().GetResult(), Is.False);
            Assert.That(control.LastError, Is.EqualTo("Assign ConversationInput.Orchestrator to the conversation you want to use."));
            Assert.That(control.StatusText.text, Is.EqualTo(control.LastError));
            Assert.That(orchestrator.IsRunning, Is.True, "A separate conversation is already running in this scene.");
            Assert.That(control.Orchestrator, Is.Null, "Input must not silently choose a scene conversation.");
            Assert.That(pipeline.Requests, Is.Empty);
            Assert.That(control.IsSubmitting, Is.False);
            Assert.That(control.Input.text, Is.EqualTo("  Keep this unconnected draft  "));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(image));
        }

        [UnityTest]
        public IEnumerator StoppedAssignedOrchestratorReportsStartupGuidanceAndKeepsDraftUntilExplicitStart()
        {
            var stopped = CreateOrchestrator(out var stoppedPipeline);
            var control = CreateInput(stopped);
            control.Input.text = "  Start this selected conversation  ";
            control.SetImage(Png(Color.blue));
            var image = control.ImagePreview.sprite;
            var rejected = control.SubmitAsync();
            yield return Wait(rejected);
            Assert.That(rejected.GetAwaiter().GetResult(), Is.False);
            Assert.That(control.LastError, Is.EqualTo("The assigned conversation is not running. Start its ChatdollOrchestrator before sending."));
            Assert.That(control.StatusText.text, Is.EqualTo(control.LastError));
            Assert.That(stopped.IsRunning, Is.False, "Sending must not start the assigned conversation implicitly.");
            Assert.That(control.Orchestrator, Is.SameAs(stopped));
            Assert.That(stoppedPipeline.Requests, Is.Empty);
            Assert.That(pipeline.Requests, Is.Empty, "The nearby running conversation is not a fallback target.");
            Assert.That(control.Input.text, Is.EqualTo("  Start this selected conversation  "));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(image));
            yield return Wait(stopped.StartAsync());
            var submitted = control.SubmitAsync();
            yield return Wait(submitted);
            Assert.That(submitted.GetAwaiter().GetResult(), Is.True);
            Assert.That(stoppedPipeline.Requests.Single().Text, Is.EqualTo("Start this selected conversation"));
            Assert.That(control.Input.text, Is.Empty);
            Assert.That(control.HasImage, Is.False);
            Assert.That(control.LastError, Is.Null);
        }

        [UnityTest]
        public IEnumerator FocusLossBlankDraftAndInflightSubmitDoNotCreateExtraRequests()
        {
            var control = CreateInput();
            control.Input.text = "Do not submit on focus loss";
            control.Input.onEndEdit.Invoke(control.Input.text);
            yield return null;
            Assert.That(pipeline.Requests, Is.Empty);
            control.Input.text = "   ";
            var blank = control.SubmitAsync();
            yield return Wait(blank);
            Assert.That(blank.GetAwaiter().GetResult(), Is.False);
            Assert.That(pipeline.Requests, Is.Empty);
            var held = HoldNext(pipeline);
            control.Input.text = "One request";
            var first = control.SubmitAsync();
            yield return Until(() => pipeline.Requests.Count == 1);
            Assert.That(control.IsSubmitting, Is.True);
            Assert.That(control.SendButton.interactable, Is.False);
            Assert.That(control.StatusText.text, Does.Contain("Sending"));
            control.SendButton.onClick.Invoke();
            var duplicate = control.SubmitAsync();
            yield return Wait(duplicate);
            Assert.That(duplicate.GetAwaiter().GetResult(), Is.False);
            Assert.That(pipeline.Requests.Count, Is.EqualTo(1));
            held.Complete();
            yield return Wait(first);
            Assert.That(first.GetAwaiter().GetResult(), Is.True);
            Assert.That(control.SendButton.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator ImagePickerCreatesResizedPreviewAndImageOnlyJpegRequest()
        {
            var control = CreateInput(); control.MaxImageDimension = 16;
            control.ImagePicker.HandleImage?.Invoke(Png(Color.red, 80, 40));
            Assert.That(control.HasImage, Is.True);
            Assert.That(control.ImagePreview.gameObject.activeSelf, Is.True);
            Assert.That(control.ImagePreview.preserveAspect, Is.True);
            Assert.That(control.ImagePreview.sprite.texture.width, Is.EqualTo(16));
            Assert.That(control.ImagePreview.sprite.texture.height, Is.EqualTo(8));
            Assert.That(control.TextRect.offsetMin.x, Is.EqualTo(75));
            Assert.That(control.PlaceholderRect.offsetMin.x, Is.EqualTo(75));
            var held = HoldNext(pipeline);
            var submitted = control.SubmitAsync();
            yield return Until(() => pipeline.Requests.Count == 1);
            var request = pipeline.Requests.Single();
            Assert.That(request.Text, Is.Empty);
            var outgoing = DecodeJpeg(request.ImageUrls.Single());
            Assert.That(outgoing.width, Is.EqualTo(16));
            Assert.That(outgoing.height, Is.EqualTo(8));
            Assert.That(outgoing.GetPixel(4, 4).r, Is.GreaterThan(0.8f));
            held.Complete();
            yield return Wait(submitted);
            Assert.That(submitted.GetAwaiter().GetResult(), Is.True);
            Assert.That(control.HasImage, Is.False);
            Assert.That(control.ImagePreview.gameObject.activeSelf, Is.False);
            Assert.That(control.TextRect.offsetMin, Is.EqualTo(new Vector2(12, 4)));
            Assert.That(control.PlaceholderRect.offsetMin, Is.EqualTo(new Vector2(14, 5)));
        }

        [UnityTest]
        public IEnumerator AttachmentTakesPriorityOverCapturedCameraStillAndSendingNeverStartsCamera()
        {
            var control = CreateInput();
            var camera = CreateCamera(); control.Camera = camera;
            SetCameraStill(camera, Png(Color.red));
            control.SetImage(Png(Color.blue));
            var first = control.SubmitAsync();
            yield return Wait(first);
            Assert.That(first.GetAwaiter().GetResult(), Is.True);
            Assert.That(DecodeJpeg(pipeline.Requests.First().ImageUrls.Single()).GetPixel(1, 1).b, Is.GreaterThan(0.8f));
            Assert.That(camera.GetStillImage(), Is.Not.Null, "Sending an attachment must keep the unsubmitted camera still.");
            Assert.That(camera.IsAlreadyStarted, Is.False);
            var second = control.SubmitAsync();
            yield return Wait(second);
            Assert.That(second.GetAwaiter().GetResult(), Is.True);
            Assert.That(DecodeJpeg(pipeline.Requests.Last().ImageUrls.Single()).GetPixel(1, 1).r, Is.GreaterThan(0.8f));
            Assert.That(camera.GetStillImage(), Is.Null);
            control.Input.text = "No image is selected";
            var third = control.SubmitAsync();
            yield return Wait(third);
            Assert.That(pipeline.Requests.Last().ImageUrls, Is.Empty);
            Assert.That(camera.IsAlreadyStarted, Is.False);
        }

        [UnityTest]
        public IEnumerator FailedAndCanceledRequestsKeepDraftAttachmentAndReadableStatus()
        {
            var control = CreateInput();
            control.Input.text = "  Keep my draft  "; control.SetImage(Png(Color.red));
            var sprite = control.ImagePreview.sprite;
            var failure = new InvalidOperationException("Fake pipeline rejected this request");
            pipeline.Handler = (request, token) => throw failure;
            var failed = control.SubmitAsync();
            yield return Wait(failed);
            Assert.That(failed.GetAwaiter().GetResult(), Is.False);
            Assert.That(control.Input.text, Is.EqualTo("  Keep my draft  "));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(sprite));
            Assert.That(control.LastError, Is.EqualTo(failure.Message));
            Assert.That(control.StatusText.text, Is.EqualTo(failure.Message));
            var held = HoldNext(pipeline);
            using (var cancellation = new CancellationTokenSource())
            {
                var canceled = control.SubmitAsync(cancellation.Token);
                yield return Until(() => pipeline.Requests.Count == 2);
                cancellation.Cancel();
                yield return Wait(canceled);
                Assert.That(canceled.GetAwaiter().GetResult(), Is.False);
            }
            Assert.That(control.LastError, Does.Contain("canceled"));
            Assert.That(control.Input.text, Is.EqualTo("  Keep my draft  "));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(sprite));
            Assert.That(control.IsSubmitting, Is.False);
            Assert.That(control.SendButton.interactable, Is.True);
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(errors, Does.Contain(failure));
        }

        [UnityTest]
        public IEnumerator EditingTextAndReplacingImageDuringFlightPreservesTheNextDraft()
        {
            var control = CreateInput();
            control.Input.text = "first draft"; control.SetImage(Png(Color.red));
            var held = HoldNext(pipeline);
            var submitted = control.SubmitAsync();
            yield return Until(() => pipeline.Requests.Count == 1);
            control.Input.text = "next draft"; control.SetImage(Png(Color.blue));
            var nextSprite = control.ImagePreview.sprite;
            held.Complete();
            yield return Wait(submitted);
            Assert.That(submitted.GetAwaiter().GetResult(), Is.True);
            Assert.That(pipeline.Requests.Single().Text, Is.EqualTo("first draft"));
            Assert.That(DecodeJpeg(pipeline.Requests.Single().ImageUrls.Single()).GetPixel(1, 1).r, Is.GreaterThan(0.8f));
            Assert.That(control.Input.text, Is.EqualTo("next draft"));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(nextSprite));
            Assert.That(control.HasImage, Is.True);
        }

        [UnityTest]
        public IEnumerator DisableAndReenablePreventOldCompletionFromClearingTheDraft()
        {
            var control = CreateInput();
            control.Input.text = "draft on reopened UI"; control.SetImage(Png(Color.red));
            var sprite = control.ImagePreview.sprite;
            var held = HoldNext(pipeline);
            var submitted = control.SubmitAsync();
            yield return Until(() => pipeline.Requests.Count == 1);
            control.enabled = false;
            control.enabled = true;
            held.Complete();
            yield return Wait(submitted);
            Assert.That(submitted.GetAwaiter().GetResult(), Is.True);
            Assert.That(control.Input.text, Is.EqualTo("draft on reopened UI"));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(sprite));
            Assert.That(control.StatusText.text, Is.Empty);
            Assert.That(control.IsSubmitting, Is.False);
        }

        [UnityTest]
        public IEnumerator RebindAndDestroyMakeDelayedCompletionsHarmlessToTheCurrentUi()
        {
            var control = CreateInput(); control.Input.text = "keep after rebind";
            control.SetImage(Png(Color.red)); var originalSprite = control.ImagePreview.sprite;
            var firstHeld = HoldNext(pipeline);
            var first = control.SubmitAsync();
            yield return Until(() => pipeline.Requests.Count == 1);
            var nextOrchestrator = CreateOrchestrator(out var nextPipeline);
            yield return Wait(nextOrchestrator.StartAsync());
            control.Orchestrator = nextOrchestrator;
            firstHeld.Complete();
            yield return Wait(first);
            Assert.That(control.Input.text, Is.EqualTo("keep after rebind"));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(originalSprite));
            var secondHeld = HoldNext(nextPipeline);
            var second = control.SubmitAsync();
            yield return Until(() => nextPipeline.Requests.Count == 1);
            UnityEngine.Object.Destroy(control.gameObject);
            yield return null;
            var replacement = CreateInput(nextOrchestrator);
            replacement.Input.text = "new UI draft"; replacement.SetImage(Png(Color.blue));
            var replacementSprite = replacement.ImagePreview.sprite;
            secondHeld.Complete();
            yield return Wait(second);
            Assert.That(replacement.Input.text, Is.EqualTo("new UI draft"));
            Assert.That(replacement.ImagePreview.sprite, Is.SameAs(replacementSprite));
            Assert.That(replacement.IsSubmitting, Is.False);
            Assert.That(replacement.StatusText.text, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RepeatedEnableAndPickerReplacementDoNotDuplicateOrRetainListeners()
        {
            var control = CreateInput(); var originalPicker = control.ImagePicker;
            var externalCalls = 0; originalPicker.HandleImage += _ => externalCalls++;
            var red = Png(Color.red);
            for (var i = 0; i < 3; i++)
            {
                control.enabled = false;
                Assert.That(originalPicker.HandleImage.GetInvocationList().Length, Is.EqualTo(1));
                originalPicker.HandleImage.Invoke(red);
                Assert.That(control.HasImage, Is.False);
                control.enabled = true;
                Assert.That(originalPicker.HandleImage.GetInvocationList().Length, Is.EqualTo(2));
            }
            Assert.That(externalCalls, Is.EqualTo(3));
            originalPicker.HandleImage.Invoke(red);
            var redSprite = control.ImagePreview.sprite;
            var replacementPicker = Widget<ImageButton>(control.gameObject, "Replacement picker");
            control.ImagePicker = replacementPicker;
            yield return null;
            Assert.That(originalPicker.HandleImage.GetInvocationList().Length, Is.EqualTo(1));
            Assert.That(replacementPicker.HandleImage.GetInvocationList().Length, Is.EqualTo(1));
            originalPicker.HandleImage.Invoke(Png(Color.blue));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(redSprite));
            replacementPicker.HandleImage.Invoke(Png(Color.green));
            Assert.That(control.ImagePreview.sprite, Is.Not.SameAs(redSprite));
            UnityEngine.Object.Destroy(control);
            yield return null;
            Assert.That(replacementPicker.HandleImage, Is.Null);
            Assert.That(originalPicker.HandleImage.GetInvocationList().Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ImageOwnershipReleasesReplacedClearedAndDestroyedTexturesAndKeepsValidDraftOnEmptyInput()
        {
            var control = CreateInput(); control.SetImage(Png(Color.red));
            var firstSprite = control.ImagePreview.sprite; var firstTexture = firstSprite.texture;
            Assert.Throws<ArgumentException>(() => control.SetImage(Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => control.SetImage(null));
            Assert.That(control.ImagePreview.sprite, Is.SameAs(firstSprite));
            Assert.That(control.HasImage, Is.True);
            control.SetImage(Png(Color.blue));
            var secondSprite = control.ImagePreview.sprite; var secondTexture = secondSprite.texture;
            yield return null;
            Assert.That(firstSprite == null, Is.True);
            Assert.That(firstTexture == null, Is.True);
            control.ClearImageButton.onClick.Invoke();
            Assert.That(control.ImagePreview.sprite, Is.Null);
            Assert.That(control.HasImage, Is.False);
            Assert.That(control.ClearImageButton.gameObject.activeSelf, Is.False);
            yield return null;
            Assert.That(secondSprite == null, Is.True);
            Assert.That(secondTexture == null, Is.True);
            control.SetImage(Png(Color.green));
            var finalSprite = control.ImagePreview.sprite; var finalTexture = finalSprite.texture;
            var preview = control.ImagePreview;
            UnityEngine.Object.Destroy(control);
            yield return null;
            yield return null;
            Assert.That(finalSprite == null, Is.True);
            Assert.That(finalTexture == null, Is.True);
            Assert.That(preview.sprite, Is.Null);
        }

        private ChatdollOrchestrator CreateOrchestrator(out FakePipeline fake)
        {
            var instance = Child(root, "Orchestrator").AddComponent<ChatdollOrchestrator>();
            fake = new FakePipeline();
            instance.ConfigurePipeline(fake);
            instance.ConfigureAvatar(new FakeAvatar());
            instance.Error += errors.Add;
            orchestrators.Add(instance);
            return instance;
        }

        private ConversationInput CreateInput(ChatdollOrchestrator target = null)
        {
            var host = Child(root, "Input UI"); host.SetActive(false);
            var control = host.AddComponent<ConversationInput>();
            control.Orchestrator = target != null ? target : orchestrator;
            control.Input = Widget<InputField>(host, "Text input");
            control.Input.textComponent = Widget<Text>(control.Input.gameObject, "Text");
            control.Input.placeholder = Widget<Text>(control.Input.gameObject, "Placeholder");
            control.TextRect = control.Input.textComponent.rectTransform;
            control.PlaceholderRect = ((Text)control.Input.placeholder).rectTransform;
            control.TextRect.offsetMin = new Vector2(12, 4);
            control.PlaceholderRect.offsetMin = new Vector2(14, 5);
            control.SendButton = Widget<Button>(host, "Send");
            control.ClearImageButton = Widget<Button>(host, "Clear image");
            control.ImagePreview = Widget<Image>(host, "Image preview");
            control.ImagePicker = Widget<ImageButton>(host, "Image picker");
            control.StatusText = Widget<Text>(host, "Status");
            host.SetActive(true);
            return control;
        }

        private Window CreateUserWindow()
        {
            var host = Child(root, "User caption"); host.SetActive(false);
            var window = host.AddComponent<Window>();
            window.MessageText = Widget<Text>(host, "Message");
            window.SpeakerText = Widget<Text>(host, "Speaker");
            window.Options.ShowAssistantMessages = false;
            window.Options.AutoHideUser = false;
            window.IsTextAnimated = false;
            window.Source = orchestrator;
            host.SetActive(true);
            return window;
        }

        private SimpleCamera CreateCamera()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ChatdollKit/Prefabs/Runtime/SimpleCamera.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(prefab, root.transform);
            var camera = instance.GetComponentInChildren<SimpleCamera>(true);
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.IsAlreadyStarted, Is.False);
            return camera;
        }

        private void SetCameraStill(SimpleCamera camera, byte[] bytes)
        {
            camera.SetStillImage(bytes);
            using (var serialized = new SerializedObject(camera))
            {
                var image = (Image)serialized.FindProperty("stillImage").objectReferenceValue;
                // The legacy camera does not own/dispose these objects when ClearStillImage is called.
                assets.Add(image.sprite); assets.Add(image.sprite.texture);
            }
        }

        private byte[] Png(Color color, int width = 8, int height = 8)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            assets.Add(texture);
            texture.SetPixels(Enumerable.Repeat(color, width * height).ToArray());
            texture.Apply();
            return texture.EncodeToPNG();
        }

        private Texture2D DecodeJpeg(string url)
        {
            const string prefix = "data:image/jpeg;base64,";
            Assert.That(url, Does.StartWith(prefix));
            var bytes = Convert.FromBase64String(url.Substring(prefix.Length));
            Assert.That(bytes.Take(2), Is.EqualTo(new byte[] { 0xff, 0xd8 }));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            assets.Add(texture);
            Assert.That(texture.LoadImage(bytes), Is.True);
            return texture;
        }

        private HeldRequest HoldNext(FakePipeline source)
        {
            var held = new HeldRequest();
            heldRequests.Add(held);
            source.Handler = held.WaitAsync;
            return held;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent.transform, false);
            return host;
        }
        private static T Widget<T>(GameObject parent, string name) where T : Component => Child(parent, name).AddComponent<T>();
        private static IEnumerator Wait(UniTask task)
        {
            var until = Time.realtimeSinceStartupAsDouble + 5;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartupAsDouble < until) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "Conversation input operation timed out.");
            // A UniTask<T> can be single-consumer; successful generic results are inspected by the test.
            if (task.Status.IsFaulted()) task.GetAwaiter().GetResult();
            Assert.That(task.Status.IsCanceled(), Is.False, "The UI operation should convert cancellation to a false result.");
        }
        private static IEnumerator Until(Func<bool> condition)
        {
            var until = Time.realtimeSinceStartupAsDouble + 5;
            while (!condition() && Time.realtimeSinceStartupAsDouble < until) yield return null;
            Assert.That(condition(), Is.True, "Expected conversation input state did not arrive.");
        }

        private sealed class HeldRequest
        {
            private readonly SpeechCompletionSource<SpeechPipelineResponse> completion = new SpeechCompletionSource<SpeechPipelineResponse>();
            internal async UniTask<SpeechPipelineResponse> WaitAsync(SpeechPipelineRequest request, CancellationToken token)
            {
                using (token.Register(() => completion.TrySetCanceled(token))) return await completion.Task;
            }
            internal void Complete() => completion.TrySetResult(new SpeechPipelineResponse { Type = SpeechPipelineResponseType.Final, Text = "done" });
        }

        private sealed class FakeAvatar : IAvatarController
        {
            public UniTask PresentAsync(AvatarRequest request, CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        private sealed class FakePipeline : ISpeechPipeline
        {
            public string SessionId => "conversation-input-test";
            public readonly ConcurrentQueue<SpeechPipelineRequest> Requests = new ConcurrentQueue<SpeechPipelineRequest>();
            internal Func<SpeechPipelineRequest, CancellationToken, UniTask<SpeechPipelineResponse>> Handler;
            private int sequence;
            public event Func<SpeechPipelineResponse, UniTask> ResponseReceived;
            public event Action<Exception> Error { add { } remove { } }

            public async UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default)
            {
                var snapshot = request.Copy();
                var id = snapshot.TransactionId ?? "request-" + Interlocked.Increment(ref sequence);
                snapshot.TransactionId = id;
                Requests.Enqueue(snapshot.Copy());
                await EmitAsync(Response(SpeechPipelineResponseType.Accepted, id));
                var start = Response(SpeechPipelineResponseType.Start, id);
                start.Metadata = new JObject { ["recognized_text"] = snapshot.Text };
                await EmitAsync(start);
                SpeechPipelineResponse result;
                try
                {
                    result = Handler == null ? Response(SpeechPipelineResponseType.Final, id) : await Handler(snapshot, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception error)
                {
                    await EmitAsync(Response(error is OperationCanceledException ? SpeechPipelineResponseType.Canceled : SpeechPipelineResponseType.Error, id));
                    throw;
                }
                result.SessionId = SessionId; result.TransactionId = id;
                await EmitAsync(result);
                return result;
            }
            private SpeechPipelineResponse Response(SpeechPipelineResponseType type, string id) =>
                new SpeechPipelineResponse { Type = type, SessionId = SessionId, TransactionId = id };
            private async UniTask EmitAsync(SpeechPipelineResponse response)
            {
                var handlers = ResponseReceived;
                if (handlers != null)
                    foreach (Func<SpeechPipelineResponse, UniTask> handler in handlers.GetInvocationList()) await handler(response);
            }
            public UniTask ProcessAudioSamplesAsync(byte[] samples, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask InterruptAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask DrainAsync() => UniTask.CompletedTask;
            public UniTask DisposeAsync() => UniTask.CompletedTask;
        }
    }
}
