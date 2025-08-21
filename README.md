# ChatdollKit
3D virtual assistant SDK that enables you to make your 3D model into a voice-enabled chatbot. [🇯🇵日本語のREADMEはこちら](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)

- [🐈 Live demo](https://unagiken.blob.core.windows.net/chatdollkit/ChatdollKitDemoWebGL/index.html) A WebGL demo. Say "Hello" to start conversation. She’s multilingual, so you can ask her something like "Let's talk in Japanese" when you want to switch languages.
- [🍎 iOS App: OshaberiAI](https://apps.apple.com/us/app/oshaberiai/id6446883638) A Virtual Agent App made with ChatdollKit: a perfect fusion of character creation by AI prompt engineering, customizable 3D VRM models, and your favorite voices by VOICEVOX.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/chatdollkit-overview-080beta.png" width="720">


## ✨ Features

- **Generative AI Native**: Supports multiple LLMs like ChatGPT, Anthropic Claude, Google Gemini Pro, Dify, and others, with function calling (ChatGPT/Gemini) and multimodal capabilities.
- **3D model expression**: Synchronizes speech and motion, controls facial expressions and animations autonomously, supports blinking and lip-sync.
- **Dialog control**: Integrates Speech-to-Text and Text-to-Speech (OpenAI, Azure, Google, VOICEVOX / AivisSpeech, Aivis Cloud API, Style-Bert-VITS2, NijiVoice etc.), manages dialog state (context), extracts intents and routes topics, supports wakeword detection.
- **Multi platforms**: Compatible with Windows, Mac, Linux, iOS, Android, and other Unity-supported platforms, including VR, AR, and WebGL.


## 💎 What's New in Version 0.8.15

- **🌏 WebGL Enhancements**: Add Silero VAD support, camera switching (front/rear) with correct aspect ratio handling, file upload for images, optimized microphone data transfer, and fixes for lip-sync when muted.  
- **✨ UI Control Improvements**: Sleeker and more streamlined UI controls that work out-of-the-box with zero configuration—just drop them onto your scene’s Canvas.  
- **🥁 Stronger Noise Resistance**: Combine multiple voice activity detection methods (e.g., Silero VAD + built-in energy-based VAD) to better capture user speech even in noisy environments like event venues.  


<details>
<summary>🕰️ Previous Updates (click to expand)</summary>

### 0.8.14

- **🎙️ Echo Cancelling Support**: Add native microphone support for Android, iOS, and macOSX that support AEC, noise cancelling and other features for voice conversation.
- **🗣️ Conversation Improvement**: Prevent conversation breakdown caused by turn-end misrecognition and improve conversation experience with features like automatic volume control when users interrupt during AI speech
- **💠 Platform Expansion**: Support for Aivis Cloud API TTS, AIAvatarKit TTS/STT, and GPT-5 `reasoning_effort` parameter

### 0.8.13

- **🥳 Silero VAD Support**: ML-based voice-activity detection vastly improves turn-end accuracy in noisy settings, enabling smooth conversations outdoors or at events.
- **🪄 TTS Pre-processing**: Optional text pre-processing lets you fine-tune pronunciation (e.g., convert “OpenAI” to katakana) before synthesis.
- **🤝 Grok & Gemini Compatibility**: Removes OpenAI-specific params from the OpenAI-style endpoint, so Grok, Gemini, and other API-compatible models work out of the box.

### 0.8.11 and 0.8.12

- **🤖 AIAvatarKit Backend**: Offloads AI agent logic to the server—boosting front-end maintainability—while letting you plug in frameworks like AutoGen (and any other agent SDK) for unlimited capability expansion.
- **🌐 WebGL Improvements**: Upgraded mic capture to modern `AudioWorkletNode` for lower latency and reliability; stabilized mute/unmute handling; improved error handling to immediately surface HTTP errors and prevent hangs; fixed API-key authorization in WebGL builds.

### 0.8.10

- **🌎 Dynamic Multi-Language**: The system can now autonomously switch languages for both speaking and listening during conversations.
- **🔖 Long-Term Memory**: Past conversation history can now be stored and searched. Components are provided for [ChatMemory](https://github.com/uezo/chatmemory), but you can also integrate with services like mem0 or Zep.


### 0.8.8 and 0.8.9

- **✨ Support NijiVoice as a Speech Synthesizer**: Now support NijiVoice, an AI-Powered Expressive Speech Generation Service.
- **🥰🥳 Support Multiple AITuber Dialogue**: AITubers can now chat with each other, bringing dynamic and engaging interactions to life like never before!
- **💪 Support Dify as a backend for AITuber**: Seamlessly integrate with any LLM while empowering AITubers with agentic capabilities, blending advanced knowledge and functionality for highly efficient and scalable operations!


### 0.8.7

- **✨ Update AITuber demo**: Support more APIs, bulk configuration, UI and mode!. (v0.8.7)


### 0.8.6

- **🎛️ Support VOICEVOX and AivisSpeech inline style**: Enables dynamic and autonomous switching of voice styles to enrich character expression and adapt to emotional nuances.
- **🥰 Improve VRM runtime loading**: Allows seamless and error-free switching of 3D models at runtime, ensuring a smoother user experience.


### 0.8.5

- **🎓 Chain of Thought Prompting**: Say hello to Chain of Thought (CoT) Prompting! 🎉 Your AI character just got a major boost in IQ and EQ!


### 0.8.4

- **🧩 Modularized for Better Reusability and Maintainability**: We’ve reorganized key components, focusing on modularity to improve customizability and reusability. Check out the demos for more details!
- **🧹 Removed Legacy Components**: Outdated components have been removed, simplifying the toolkit and ensuring compatibility with the latest features. Refer to [🔄 Migration from 0.7.x](#-migration-from-07x) if you're updating from v0.7.x.


### 0.8.3

- **🎧 Stream Speech Listener**: We’ve added `AzureStreamSpeechListener` for smoother conversations by recognizing speech as it’s spoken.
- **🗣️ Improved Conversation**: Interrupt characters to take your turn, and enjoy more expressive conversations with natural pauses—enhancing the overall experience.
- **💃 Easier Animation Registration**: We’ve simplified the process of registering animations for your character, making your code cleaner and easier to manage.


### 0.8.2

- **🌐 Control WebGL Character from JavaScript**: We’ve added the ability to control the ChatdollKit Unity application from JavaScript when running in WebGL builds. This allows for more seamless interactions between the Unity app and web-based systems.
- **🗣️ Speech Synthesizer**: A new `SpeechSynthesizer` component has been introduced to streamline text-to-speech (TTS) operations. This component is reusable across projects without `Model` package, simplifying maintenance and reusability. 


### 0.8.1

- **🏷️ User-Defined Tags Support**: You can now include custom tags in AI responses, enabling dynamic actions. For instance, embed language codes in replies to switch between multiple languages on the fly during conversations.
- **🌐 External Control via Socket**: Now supports external commands through Socket communication. Direct conversation flow, trigger specific phrases, or control expressions and gestures, unlocking new use cases like AI Vtubers and remote customer service. Check out the client-side demo here: https://gist.github.com/uezo/9e56a828bb5ea0387f90cc07f82b4c15

### 0.8 Beta

- **⚡ Optimized AI Dialog Processing**: We've boosted response speed with parallel processing and made it easier for you to customize behavior with your own code. Enjoy faster, more flexible AI conversations!
- **🥰 Emotionally Rich Speech**: Adjusts vocal tone dynamically to match the conversation, delivering more engaging and natural interactions.
- **🎤 Enhanced Microphone Control**: Microphone control is now more flexible than ever! Easily start/stop devices, mute/unmute, and adjust voice recognition thresholds independently.

</details>


## 🚀 Quick Start

You can learn how to setup ChatdollKit by watching this video that runs the demo scene(including chat with ChatGPT): https://www.youtube.com/watch?v=rRtm18QSJtc

[![](https://img.youtube.com/vi/rRtm18QSJtc/0.jpg)](https://www.youtube.com/watch?v=rRtm18QSJtc)

To run the demo for version 0.8, please follow the steps below after importing the dependencies:

- Open scene `Demo/Demo08`.
- Select `AIAvatarVRM` object in scene.
- Set OpenAI API key to following components on inspector:
  - ChatGPTService
  - OpenSpeechSynthesizer
  - OpenAISpeechListener
- Run on Unity Editor.
- Say "こんにちは" or word longer than 3 characters.


## 🔖 Table of Contents

- [📦 Setup New Project](#-setup-new-project)
  - [Import dependencies](#import-dependencies)
  - [Resource preparation](#resource-preparation)
  - [AIAvatarVRM prefab](#aiavatarvrm-prefab)
  - [ModelController](#modelcontroller)
  - [Animator](#animator)
  - [AIAvatar](#aiavatar)
  - [LLM Service](#llm-service)
  - [Speech Service](#speech-service)
  - [Microphone Controller](#microphone-controller)
  - [Run](#run)
- [🎓 LLM Service](#-llm-service)
  - [Basic Settings](#basic-settings)
  - [Facial Expressions](#facial-expressions)
  - [Animations](#animations)
  - [Pause in Speech](#pause-in-speech)
  - [User Defined Tag](#user-defined-tag)
  - [Multi Modal](#multi-modal)
  - [Chain of Thought Prompting](#chain-of-thought-prompting)
  - [Long-Term Memory](#long-term-memory)
- [🗣️ Speech Synthesizer (Text-to-Speech)](#%EF%B8%8F-speech-synthesizer-text-to-speech)
  - [Voice Prefetch Mode](#voice-prefetch-mode)
  - [Make custom SpeechSynthesizer](#make-custom-speechsynthesizer)
  - [Performance and Quality Tuning](#performance-and-quality-tuning)
  - [Preprocessing](#preprocessing)
- [🎧 Speech Listener (Speech-to-Text)](#-speech-listener-speech-to-text)
  - [Settings on AIAvatar Inspector](#settings-on-aiavatar-inspector)
  - [Downsampling](#downsampling)
  - [Using AzureStreamSpeechListener](#using-azurestreamspeechlistener)
  - [Using Silero VAD](#using-silero-vad)
- [⏰ Wake Word Detection](#-wake-word-detection)
  - [Wake Words](#wake-words)
  - [Cancel Words](#cancel-words)
  - [Interrupt Words](#interrupt-words)
  - [Ignore Words](#ignore-words)
  - [Wake Length](#wake-length)
- [⚡️ AI Agent (Tool Call)](#%EF%B8%8F-ai-agent-tool-call)
- [🎙️ Devices](#%EF%B8%8F-devices)
  - [Microphone](#microphone)
  - [Camera](#camera)
- [🥰 3D Model Control](#-3d-model-control)
  - [Idle Animations](#idle-animations)
  - [Control by Script](#control-by-script)
- [🎚️ UI Components](#%EF%B8%8F-ui-components)
- [🎮 Control from External Programs](#-control-from-external-programs)
  - [ChatdollKit Remote Client](#chatdollkit-remote-client)
- [🌐 Run on WebGL](#-run-on-webgl)
- [🔄 Migration from 0.7.x](#-migration-from-07x)
- [❤️ Thanks](#%EF%B8%8F-thanks)


## 📦 Setup New Project

The steps for setting up with a VRM model are as follows. For instructions on using models for VRChat, refer to [README v0.7.7](https://github.com/uezo/ChatdollKit/blob/v0.7.7/README.md#-modelcontroller).

**⚠️CAUTION**: Do not use the SRP (Scriptable Render Pipeline) project template in Unity. UniVRM, which ChatdollKit depends on, does not support SRP.

### Import dependencies

Download the latest version of [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) and import it into your Unity project after import dependencies;

- `Burst` from Unity Package Manager (Window > Package Manager)
- [UniTask](https://github.com/Cysharp/UniTask)(Tested on Ver.2.5.4)
- [uLipSync](https://github.com/hecomi/uLipSync)(Tested on v3.1.0)
- [UniVRM](https://github.com/vrm-c/UniVRM/releases/tag/v0.127.2)(v0.127.2)
- [ChatdollKit VRM Extension](https://github.com/uezo/ChatdollKit/releases)
- JSON.NET: If your project doesn't have JSON.NET, add it from Package Manager > [+] > Add package from git URL... > com.unity.nuget.newtonsoft-json

<img src="Documents/Images/burst.png" width="640">


### Resource preparation

Add 3D model to the scene and adjust as you like. Also install required resources for the 3D model like shaders etc.

And, import animation clips. In this README, I use [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406) that is also used in demo. I believe it is worth for you to purchase the pro edition👍


### AIAvatarVRM prefab

Add the `ChatdollKit/Prefabs/AIAvatarVRM` prefab to the scene. And, create EventSystem to use UI components.

<img src="Documents/Images/readme081/01_aiavatar_prefab.png" width="640">


### ModelController

Select `Setup ModelController` in the context menu of ModelController.

<img src="Documents/Images/readme081/02_setup_modelcontroller.png" width="640">


### Animator

Select `Setup Animator` in the context menu of ModelController and select the folder that contains animation clips or their parent folder. In this case put animation clips in `01_Idles` and `03_Others` onto `Base Layer` for override blending, `02_Layers` onto `Additive Layer` for additive blending.

<img src="Documents/Images/readme081/03_cdk_setup_animator.gif" width="640">

Next, see the `Base Layer` of newly created AnimatorController in the folder you selected. Confirm the value for transition to the state you want to set it for idle animation.

<img src="Documents/Images/readme081/04_check_idle_animation.png" width="640">

Lastly, set the value to `Idle Animation Value` on the inspector of ModelController.

<img src="Documents/Images/readme081/05_set_idle_animation_param.png" width="640">


### AIAvatar

On the inspector of `AIAvatar`, set `Wake Word` to start conversation (e.g. hello / こんにちは🇯🇵), `Cancel Word` to stop comversation (e.g. stop / おしまい🇯🇵), `Error Voice` and `Error Face` that will be shown when error occured (e.g. Something wrong / 調子が悪いみたい🇯🇵).

`Prefix / Suffix Allowance` is the allowable length for additional characters before or after the wake word. For example, if the wake word is "Hello" and the allowance is 4 characters, the phrase "Ah, Hello!" will still be detected as the wake word.

<img src="Documents/Images/readme081/06_setup_ai_avatar.png" width="640">


### LLM Service

Attach the component corresponding to the LLM service from `ChatdollKit/Scripts/LLM` and set the required fields like API keys and system prompts. In this example, we use ChatGPT, but the framework also supports Claude, Gemini, and Dify.

<img src="Documents/Images/readme081/07_setup_chatgpt.png" width="640">


### Speech Service

Attach the `SpeechListener` component from `ChatdollKit/Scripts/SpeechListener` for speech recognition and the `SpeechSynthesizer` component from `ChatdollKit/Scripts/SpeechSynthesizer` for speech synthesis. Configure the necessary fields like API keys and language codes. Enabling `PrintResult` in the SpeechListener settings will output recognized speech to the log, useful for debugging.

<img src="Documents/Images/readme081/08_setup_speech.png" width="640">


### Microphone Controller

Add `ChatdollKit/Prefabs/Runtime/MicrophoneController` to your scene. This provides a UI to adjust the minimum volume for speech recognition. If the environment is noisy, you can slide it to the left to filter out background noise.

<img src="Documents/Images/readme081/09_add_microphone_controller.png" width="640">


### Run

Press Play button of Unity editor. You can see the model starts with idling animation and blinking.

- Adjust the microphone volume slider if necessary.
- Say the word you set to `Wake Word` on inspector. (e.g. hello / こんにちは🇯🇵)
- Your model will reply "Hi there!" or something.

<img src="Documents/Images/readme081/10_run.png" width="640">

Enjoy👍


## 🎓 LLM Service

### Basic Settings

We support ChatGPT, Claude, Gemini, and Dify as text generation AI services. Experimental support for Command R is also available, but it is unstable. To use LLM services, attach the LLMService component you want to use from `ChatdollKit/Scripts/LLM` to the AIAvatar object and check the `IsEnabled` box. If other LLMServices are already attached, make sure to uncheck the `IsEnabled` box for those you don't plan to use.

You can configure parameters like API keys and system prompts directly on the attached LLMService in the inspector. For more details of these parameters, please refer to the API references for the LLM services.

NOTE: To use OpenAI-compatible APIs, check `IsOpenAPICompatibleAPI` and set `ChatCompletionURL` in addition to the above.

- Gemini: https://generativelanguage.googleapis.com/v1beta/chat/completions
- Grok: https://api.x.ai/v1/chat/completions


### Facial Expressions

You can autonomously control facial expressions according to the conversation content.

To control expressions, include tags like `[face:ExpressionName]` in the AI responses, which can be set through system prompts. Here's an example of a system prompt:

```
You have four expressions: 'Joy', 'Angry', 'Sorrow', 'Fun' and 'Surprised'.
If you want to express a particular emotion, please insert it at the beginning of the sentence like [face:Joy].

Example
[face:Joy]Hey, you can see the ocean! [face:Fun]Let's go swimming.
```

The expression names must be understandable by the AI. Make sure they match exactly, including case sensitivity, with the expressions defined in the VRM model.


### Animations

You can also control gestures (referred to as animations) autonomously based on the conversation content.

To control animations, include tags like `[anim:AnimationName]` in the AI responses, and set the instructions in the system prompt. Here's an example:

```
You can express your emotions through the following animations:

- angry_hands_on_waist
- brave_hand_on_chest
- calm_hands_on_back
- concern_right_hand_front
- energetic_right_fist_up
- energetic_right_hand_piece
- pitiable_right_hand_on_back_head
- surprise_hands_open_front
- walking
- waving_arm
- look_away
- nodding_once
- swinging_body

If you want to express emotions with gestures, insert the animation into the response message like [anim:waving_arm].

Example
[anim:waving_arm]Hey, over here!
```

The animation names must be clear to the AI for it to understand the intended gesture.

To link the specified animation name to the animation defined in the `Animator Controller`, register them in `ModelController` through code as shown below:

```csharp
// Base
modelController.RegisterAnimation("angry_hands_on_waist", new Model.Animation("BaseParam", 0, 3.0f));
modelController.RegisterAnimation("brave_hand_on_chest", new Model.Animation("BaseParam", 1, 3.0f));
modelController.RegisterAnimation("calm_hands_on_back", new Model.Animation("BaseParam", 2, 3.0f));
modelController.RegisterAnimation("concern_right_hand_front", new Model.Animation("BaseParam", 3, 3.0f));
modelController.RegisterAnimation("energetic_right_fist_up", new Model.Animation("BaseParam", 4, 3.0f));
modelController.RegisterAnimation("energetic_right_hand_piece", new Model.Animation("BaseParam", 5, 3.0f));
modelController.RegisterAnimation("pitiable_right_hand_on_back_head", new Model.Animation("BaseParam", 7, 3.0f));
modelController.RegisterAnimation("surprise_hands_open_front", new Model.Animation("BaseParam", 8, 3.0f));
modelController.RegisterAnimation("walking", new Model.Animation("BaseParam", 9, 3.0f));
modelController.RegisterAnimation("waving_arm", new Model.Animation("BaseParam", 10, 3.0f));
// Additive
modelController.RegisterAnimation("look_away", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_look_away_01", "Additive Layer"));
modelController.RegisterAnimation("nodding_once", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
modelController.RegisterAnimation("swinging_body", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_swinging_body_01", "Additive Layer"));
```

If you use Animation Girl Idle Animations or its free edition, you can register animations easily:

```csharp
modelController.RegisterAnimations(AGIARegistry.GetAnimations(animationCollectionKey));
```


### Pause in Speech

You can insert pauses in the character's speech to make conversations feel more natural and human-like.

To control the length of pauses, include tags like `[pause:seconds]` in the AI responses, which can be set through system prompts. The specified number of seconds can be a float value, allowing precise control of the pause duration at that point in the dialogue. Here's an example of a system prompt:

```
You can insert pauses in the character's speech to make conversations feel more natural and human-like.

Example:
Hey, it's a beautiful day outside! [pause:1.5] What do you think we should do?
```


### User Defined Tag

Besides expressions and animations, you can execute actions based on developer-defined tags. Include the instructions to insert tags in the system prompt and implement `HandleExtractedTags`.

Here's an example of switching room lighting on/off during the conversation:


```
If you want switch room light on or off, insert language tag like [light:on].

Example:
[light:off]OK, I will turn off the light. Good night.
```

```csharp
dialogProcessor.LLMServiceExtensions.HandleExtractedTags = (tags, session) =>
{
    if (tags.ContainsKey("light"))
    {
        var lightCommand = tags["light"];
        if (lightCommand.lower() == "on")
        {
            // Turn on the light
            Debug.Log($"Turn on the light");
        }
        else if (lightCommand.lower() == "off")
        {
            // Turn off the light
            Debug.Log($"Turn off the light");
        }
        else
        {
            Debug.LogWarning($"Unprocessable command: {lightCommand}");
        }
    }
};
```


### Multi Modal

You can include images from cameras or files in requests to the LLM. Include the image binary data under the key `imageBytes` in the `payloads` argument of `DialogProcessor.StartDialogAsync`.

Additionally, you can enable the system to autonomously capture images when required based on the user's speech. To achieve this, add the tag `[vision:camera]` in the AI response by configuring it in the system prompt, and implement the image capture process for when this tag is received in the LLM service.

```
You can use camera to get what you see.
When the user wants to you to see something, insert [vision:camera] into your response message.

Example
user: Look! I bought this today.
assistant: [vision:camera]Let me see.
```

```csharp
gameObject.GetComponent<ChatGPTService>().CaptureImage = async (source) =>
{
    if (simpleCamera != null)
    {
        try
        {
            return await simpleCamera.CaptureImageAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error at CaptureImageAsync: {ex.Message}\n{ex.StackTrace}");
        }
    }

    return null;
};
```


### Chain of Thought Prompting

Chain of Thought (CoT) prompting is a technique to enhance AI performance. For more information about CoT and examples of prompts, see https://docs.anthropic.com/en/docs/build-with-claude/prompt-engineering/chain-of-thought .

ChatdollKit supports Chain of Thought by excluding sentences wrapped in `<thinking> ~ </thinking>` tags from speech synthesis.

You can customize the tag by setting a preferred word (e.g., "reason") as the `ThinkTag` in the inspector of `LLMContentProcessor`.


### Long-Term Memory

ChatdollKit itself does not have a built-in mechanism for managing long-term memory. However, by implementing `OnStreamingEnd`, it is possible to accumulate memory. Additionally, by using a tool that retrieves stored memories, the system can recall and reflect them in conversations.

The following is an example using [ChatMemory](https://github.com/uezo/chatmemory).

First, to store memories, attach the `Extension/ChatMemory/ChatMemoryIntegrator` component to the main GameObject and set the ChatMemory service URL and a user ID. The user ID can be any value, but if you are building a service for multiple users, make sure to assign an ID that can uniquely identify each user within your service from code-behind.

Next, add the following code to an appropriate location (such as `Main`) so that the request and response messages are stored in ChatMemory as history when the LLM stream finishes.

```csharp
using ChatdollKit.Extension.ChatMemory;

var chatMemory = gameObject.GetComponent<ChatMemoryIntegrator>();
dialogProcessor.LLMServiceExtensions.OnStreamingEnd += async (text, payloads, llmSession, token) =>
{
    chatMemory.AddHistory(llmSession.ContextId, text, llmSession.CurrentStreamBuffer, token).Forget();
};
```


To retrieve memories and include them in the conversation, simply add the `Extension/ChatMemory/ChatMemoryTool` component to the main GameObject.

**NOTE:** ChatMemory manages what is known as episodic memory. There is also an entity called `Knowledge`, which corresponds to factual information, but it is not automatically extracted or stored. Handle it manually as needed. (By default, it is included in search targets.)


## 🗣️ Speech Synthesizer (Text-to-Speech)

We support cloud-based speech synthesis services such as Google, Azure, OpenAI, and Watson, in addition to VOICEVOX / AivisSpeech, Aivis Cloud API, VOICEROID, Style-Bert-VITS2, and NijiVoice for more characterful and engaging voices. To use a speech synthesis service, attach `SpeechSynthesizer` from `ChatdollKit/Scripts/SpeechListener` to the AIAvatar object and check the `IsEnabled` box. If other `SpeechSynthesizer` components are attached, make sure to uncheck the `IsEnabled` box for those not in use.

You can configure parameters like API keys and endpoints on the attached `SpeechSynthesizer` in the inspector. For more details of these parameters, refer to the API references of TTS services.

### Voice Prefetch Mode

The `Voice Prefetch Mode` determines how speech synthesis requests are managed and processed. By default, the system operates in Parallel mode. The following modes are supported:

1. **Parallel (default)**: In this mode, multiple speech synthesis requests are sent and processed simultaneously. This ensures the fastest response times when generating multiple speech outputs in quick succession. Use this mode when latency is critical and sufficient resources are available for parallel processing.
1. **Sequential**: Requests are processed one at a time in the order they are enqueued. This mode is ideal for managing limited resources or ensuring strict order of speech outputs. It avoids potential concurrency issues but may result in longer wait times for subsequent requests.
1. **Disabled**: No prefetching is performed in this mode. Speech synthesis occurs only when explicitly triggered, making it suitable for minimal-resource scenarios or when prefetching is unnecessary.

You can change the `Voice Prefetch Mode` in the inspector on the SpeechSynthesizer component. Ensure the selected mode aligns with your performance and resource management requirements.


### Make custom SpeechSynthesizer

You can easily create and use a custom `SpeechSynthesizer` for your preferred text-to-speech service. Create a class that inherits from `ChatdollKit.SpeechSynthesizer.SpeechSynthesizerBase`, and implement the asynchronous method `DownloadAudioClipAsync` that takes a `string text` and `Dictionary<string, object> parameters`, and returns an `AudioClip` object playable in Unity.

```csharp
UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken cancellationToken)
```

Note that WebGL does not support compressed audio playback, so make sure to handle this by adjusting your code depending on the platform.


### Performance and Quality Tuning

To achieve fast response times, rather than synthesizing the entire response message into speech, we split the text into smaller parts based on punctuation and progressively synthesize and play each segment. While this greatly improves performance, excessively splitting the text can reduce the quality of the speech, especially when using AI-based speech synthesis like Style-Bert-VITS2, affecting the tone and fluency.

You can balance performance and speech quality by adjusting how the text is split for synthesis in the `LLMContentProcessor` component's inspector.

|Item|Description|
|----|----|
|**Split Chars**|Characters to split the text at for synthesis. Speech synthesis is always performed at these points.|
|**Optional Split Chars**|Optional split characters. Normally, the text isn't split at these, but it will be if the text length exceeds the value set in Max Length Before Optional Split.|
|**Max Length Before Optional Split**|Threshold for text length at which optional split characters are used as split points.|


### Preprocessing

Implement `SpeechSynthesizer.PreprocessText` method to preprocess the text to synthesize.

Interface:

```csharp
Func<string, Dictionary<string, object>, CancellationToken, UniTask<string>> PreprocessText;
```


## 🎧 Speech Listener (Speech-to-Text)

We support cloud-based speech recognition services such as Google, Azure, and OpenAI. To use these services, attach the `SpeechListener` component from `ChatdollKit/Scripts/SpeechListener` to the AIAvatar object. Be aware that if multiple SpeechListeners are attached, they will run in parallel, so ensure that only the one you want is active.

You can configure parameters such as API keys and endpoints on the attached SpeechListener in the inspector. For details of these parameters, please refer to the API references of the respective STT services and products.

Most of the `Voice Recorder Settings` are controlled by the `AIAvatar` component, described later, so any settings in the inspector, except for those listed below, will be ignored.

|Item|Description|
|----|-----------|
|**Auto Start**|When enabled, starts speech recognition automatically when the application launches.|
|**Print Result**|When enabled, outputs the transcribed recognized speech to the console.|


### Settings on AIAvatar Inspector

Most of the settings related to the SpeechListener are configured in the inspector of the `AIAvatar` component.

|Item|Description|
|---|---|
|**Conversation Timeout**|The waiting time (seconds) before the conversation is considered finished. After this period, it transitions to Idle mode, and the message window will be hidden. To resume the conversation, the wake word must be recognized again.|
|**Idle Timeout**|The waiting time (seconds) before transitioning from Idle mode to Sleep mode. By default, there is no difference between Idle and Sleep modes, but it can be used to switch between different speech recognition methods or idle animations through user implementation.|
|**Voice Recognition Threshold DB**|The volume threshold (decibels) for speech recognition. Sounds below this threshold will not be recognized.|
|**Voice Recognition Raised Threshold DB**|An elevated threshold (decibels) for voice recognition, used to detect louder speech. This is utilized when the `Microphone Mute By` setting is set to `Threshold`.|
|**Conversation Silence Duration Threshold**|If silence is detected for longer than this time, recording ends, and speech recognition is performed.|
|**Conversation Min Recording Duration**|Speech recognition is performed only if the recorded sound exceeds this duration. This helps to ignore short noises and prevent misrecognition.|
|**Conversation Max Recording Duration**|If the recorded sound exceeds this time, speech recognition is not performed, and the recording is ignored. This prevents overly long recordings from overburdening speech recognition.|
|**Idle Silence Duration Threshold**|The amount of silence (seconds) required to stop recording during Idle mode. A smaller value is set to smoothly detect short periods of silence when waiting for the wake word.|
|**Idle Min Recording Duration**|The minimum recording duration during Idle mode. A smaller value is set compared to conversation mode to smoothly detect short phrases.|
|**Idle Max Recording Duration**|The maximum recording duration during Idle mode. Since wake words are usually short, a shorter value is set compared to conversation mode.|
|**Microphone Mute By**|The method used to prevent the avatar's speech from being recognized during speech. <br><br>- None: Does nothing.<br>- Threshold: Raises the voice recognition threshold to `Voice Recognition Raised Threshold DB`.<br>- Mute: Ignores input sound from the microphone.<br>- Stop Device: Stops the microphone device.<br>- Stop Listener: Stops the listener. **Select this when you use AzureStreamSpeechListener**|


**NOTE: **`AzureStreamSpeechListener` doesn't have some properties above because that control microphone by SDK DLL internally.


### Downsampling

The `SpeechListener` class supports downsampling of raw microphone input to a lower sample rate before sending input data to the STT service. This feature helps reduce audio payload size, leading to smoother transcription over limited-bandwidth networks.

You’ll find the **Target Sample Rate** (int) field exposed in the Inspector of SpeechListener component:

- Set to `0` (default) to use the original sample rate (no downsampling).  
- Set to a positive integer (e.g., `16000`) to downsample input to that rate (in Hz).


### Using AzureStreamSpeechListener

To use `AzureStreamSpeechListener`, some settings differ from other SpeechListeners. This is because `AzureStreamSpeechListener` controls the microphone internally through the SDK and performs transcription incrementally.

**Microphone Mute By**: Select `Stop Listener`. If this is not set, the character will listen to its own speech, disrupting the conversation.
**User Message Window**: Uncheck `Is Text Animated`, and set `Pre Gap` to `0` and `Post Gap` to around `0.2`.
**Update()**: To display the recognized text incrementally, add the following code inside the Update() method:


```csharp
if (aiAvatar.Mode == AIAvatar.AvatarMode.Conversation)
{
    if (!string.IsNullOrEmpty(azureStreamSpeechListener.RecognizedTextBuffer))
    {
        aiAvatar.UserMessageWindow.Show(azureStreamSpeechListener.RecognizedTextBuffer);
    }
}
```


### Using Silero VAD

Silero VAD is a machine learning-based voice activity detection model. By using this, you can determine human voice even in noisy environments, which significantly improves the accuracy of turn-end detection in noisy conditions compared to microphone volume-based voice activity detection.

The usage procedure is as follows:

- Import [onnxruntime-unity](https://github.com/asus4/onnxruntime-unity). Follow the procedure on GitHub to edit manifest.json.
- Download the [Silero VAD ONNX model](https://github.com/snakers4/silero-vad/tree/master/src/silero_vad/data) and place it in the StreamingAssets folder. The filename should be `silero_vad.onnx`.
- Download and import ChatdollKit's SileroVADExtension.
- Attach `SileroVADProcessor` to the object where SpeechListener is attached.
- In the `Awake` method of any MonoBehaviour component, set it as the voice detection function for SpeechListener.
    ```csharp
    var sileroVad = gameObject.GetComponent<SileroVADProcessor>();
    sileroVad.Initialize();
    var speechListener = gameObject.GetComponent<SpeechListenerBase>();
    speechListener.DetectVoiceFunc = sileroVad.IsVoiced;
    ```
- Place SileroVADMicrophoneButton in the scene if necessary

When executed, Silero VAD will be used for voice activity detection.


### Using Multiple VADs Combination

ChatdollKit supports combining multiple types of VADs. For example, by combining Silero VAD, which can recognize only human voices even in noisy environments, with the built-in energy-based VAD, which only captures loud voices, the system can accurately pick up the user’s speech at event venues while partially filtering out surrounding voices and venue announcements.

To use multiple VADs, add multiple voice detection functions to `DetectVoiceFunctions` instead of `DetectVoiceFunc`.

```csharp
speechListener.DetectVoiceFunctions = new List<Func<float[], float, bool>>()
{
    sileroVad.IsVoiced, speechListener.IsVoiceDetectedByVolume
};
```


### Echo Cancelling

Unity's built-in Microphone API doesn't support echo cancelling. To enable this feature, use platform-specific native microphone plugins.

```csharp
private void Awake()
{
    var microphoneManager = gameObject.GetComponent<MicrophoneManager>();
    
    // First, import the ChatdollKit_NativeMicrophone package
    // Then, set the appropriate provider for your platform:
    
    // iOS
    microphoneManager.MicrophoneProvider = new IOSMicrophoneProvider();
    // Android
    microphoneManager.MicrophoneProvider = new AndroidMicrophoneProvider();
    // macOS
    microphoneManager.MicrophoneProvider = new MacMicrophoneProvider();
}
```

With echo cancelling enabled, you can allow users to interrupt the AI while it's speaking. To enable this feature:

1. In the Inspector, select the `AIAvatar` component
2. Set `MicrophoneMuteBy` to `None`

This configuration allows the microphone to remain active during AI speech, enabling natural conversation interruptions while the echo cancelling prevents the AI's voice from being picked up by the microphone.


## ⏰ Wake Word Detection

You can detect wake words as triggers to start a conversation. You can also configure settings in the AIAvatar component’s inspector for cancel words that end a conversation, or to use the length of recognized speech as a trigger instead of specific phrases.

### Wake Words

The conversation starts when this phrase is recognized. You can register multiple wake words. Except for the following items, settings will be ignored in versions 0.8 and later.

|Item|Description|
|---|---|
|**Text**|Phrase to start conversation.|
|**Prefix / Suffix Allowance**|The allowable length for additional characters before or after the wake word. For example, if the wake word is "Hello" and the allowance is 4 characters, the phrase "Ah, Hello!" will still be detected as the wake word.|

### Cancel Words

The conversation ends when this phrase is recognized. You can register multiple cancel words.

### Interrupt Words

The character stop speaking and start listening user's request. You can register multiple interrupt words. (e.g. "Wait")

**NOTE:** In the AIAvatar's inspector, select `Threshold` under `Microphone Mute By` to allow ChatdollKit to listen your voice while the character is speaking.

### Ignore Words

You can register strings to be ignored when determining whether the recognized speech matches a wake word or cancel word. This is useful if you don’t want to consider the presence or absence of punctuation.

### Wake Length

You can start a conversation based on the length of the recognized text, rather than specific phrases. This feature is disabled when the value is `0`. For example, in Idle mode, you can resume the conversation using text length instead of a wake word, and in Sleep mode, the conversation can resume with the wake word.


## ⚡️ AI Agent (Tool Call)

Using the Tool Call (Function Calling) feature provided by the LLM, you can develop AI characters that function as AI agents, rather than simply engaging in conversation.

By creating a component that implements `ITool` or extends `ToolBase` and attaching it to the AIAvatar object, it will automatically be recognized as a tool and executed when needed. To create a custom tool, define `FunctionName` and `FunctionDescription`, and implement the `GetToolSpec` method, which returns the function definition, and the `ExecuteFunction` method, which handles the function’s process. For details, refer to `ChatdollKit/Examples/WeatherTool`.

**NOTE**: See [Migration from FunctionSkill to Tool](#migration-from-functionskill-to-tool) if your project has custom LLMFunctionSkills.


### Integration with Remote AI Agents

While ChatdollKit natively supports simple tool calls, it also provides integration with server-side AI agents to enable more agentic behaviors.

Specifically, ChatdollKit allows you to call AI agents through RESTful APIs by registering them as an `LLMService`. This lets you send requests and receive responses without needing to be aware of the agentic processes happening behind the scenes.  
Currently, [Dify](https://dify.ai) and [AIAvatarKit](https://github.com/uezo/aiavatarkit) are supported. You can use them by attaching either `DifyService` or `AIAvatarKitService`, configuring their settings, and enabling the `IsEnabled` flag.


## 🎙️ Devices

We provide a device control mechanism. Currently, microphones and cameras are supported.

### Microphone

The `MicrophoneManager` component captures audio from the microphone and makes the audio waveform data available to other components. It is primarily intended for use with the SpeechListener, but you can also register and use recording sessions through the `StartRecordingSession` method in custom user-implemented components.

The following are the settings that can be configured in the inspector.

|Item|Description|
|----|----|
|**Sample Rate**|Specifies the sampling rate. Set it to 44100 when using WebGL.|
|**Noise Gate Threshold DB**|Specifies the noise gate level in decibels. When used with the AIAvatar component, this value is controlled by the AIAvatar component.|
|**Auto Start**|Starts capturing audio from the microphone when the application launches.|
|**Is Debug**|Logs microphone start/stop and mute/unmute actions.|


### Camera

We provide the `SimpleCamera` prefab, which packages features such as image capture, preview display, and camera switching. Since the way cameras are handled varies by device, this is provided experimentally. For details, refer to the prefab and the scripts attached to it.


## 🥰 3D Model Control

The `ModelController` component controls the gestures, facial expressions, and speech of 3D models.

### Idle Animations

Idle animations are looped while the model is waiting. To run the desired motion, register it in the state machine of the Animator Controller and configure the transition conditions by setting the parameter name as the `Idle Animation Key` and the value as the `Idle Animation Value` in the `ModelController` inspector.

To register multiple motions and randomly switch between them at regular intervals, use the `AddIdleAnimation` method in the code as shown below. The first argument is the `Animation` object to be executed, `weight` is the multiplier for the appearance probability, and `mode` is only specified if you want to display the animation in a particular model state. The constructor of the `Animation` class takes the parameter name as the first argument, the value as the second, and the duration (in seconds) as the third.

```csharp
modelController.AddIdleAnimation(new Animation("BaseParam", 2, 5f));
modelController.AddIdleAnimation(new Animation("BaseParam", 6, 5f), weight: 2);
modelController.AddIdleAnimation(new Animation("BaseParam", 99, 5f), mode: "sleep");
```

### Control by Script

This section is under construction. Essentially, you create an `AnimatedVoiceRequest` object and call `ModelController.AnimatedSay`. The `AIAvatar` internally makes requests that combine animations, expressions, and speech, so refer to that for guidance.


## 🎚️ UI Components

We provide UI component prefabs commonly used in voice-interactive AI character applications. You can use them by simply adding them to the scene. For configuration details, refer to the demo.

- **FPSManager**: Displays the current frame rate. You can also set the target frame rate using this component.
- **MicrophoneController**: A slider to adjust the microphone's noise gate.
- **RequestInput**: A text box for inputting requests. It also provides buttons for retrieving images from the file system and for launching the camera.
- **SimpleCamera**: A component that handles image capture and preview display from the camera. You can also capture images without showing the preview.


## 🎮 Control from External Programs

You can send requests to the ChatdollKit application from external programs using socket communication or from JavaScript. This feature enables new use cases such as AI Vtuber streaming, remote avatar customer service, and hybrid character operations combining AI and human interaction.
Attach `ChatdollKit/Scripts/Network/SocketServer` to the AIAvatar object and set the port number (e.g., 8080) to control using socket communication, or, attach `ChatdollKit/Scripts/IO/JavaScriptMessageHandler` to control from JavaScript.

Additionally, to handle dialog requests over the network, attach the `ChatdollKit/Scripts/Dialog/DialogPriorityManager` to the AIAvatar object. To process requests that make the character perform gestures, facial expressions, or speech created by humans instead of AI responses, attach the `ChatdollKit/Scripts/Model/ModelRequestBroker` to the AIAvatar object.

Below is a code example for using both of the above components.

```csharp
// Configure message handler for remote control
#pragma warning disable CS1998
#if UNITY_WEBGL && !UNITY_EDITOR
gameObject.GetComponent<JavaScriptMessageHandler>().OnDataReceived = async (message) =>
{
    HandleExternalMessage(message, "JavaScript");
};
#else
gameObject.GetComponent<SocketServer>().OnDataReceived = async (message) =>
{
    HandleExternalMessage(message, "SocketServer");
};
#endif
#pragma warning restore CS1998
```

```csharp
private void HandleExternalMessage(ExternalInboundMessage message, string source)
{
    // Assign actions based on the request's Endpoint and Operation
    if (message.Endpoint == "dialog")
    {
        if (message.Operation == "start")
        {
            if (source == "JavaScript")
            {
                dialogPriorityManager.SetRequest(message.Text, message.Payloads, 0);
            }
            else
            {
                dialogPriorityManager.SetRequest(message.Text, message.Payloads, message.Priority);
            }
        }
        else if (message.Operation == "clear")
        {
            dialogPriorityManager.ClearDialogRequestQueue(message.Priority);
        }
    }
    else if (message.Endpoint == "model")
    {
        modelRequestBroker.SetRequest(message.Text);
    }            
}
```

### ChatdollKit Remote Client

The `SocketServer` is designed to receive arbitrary information via socket communication, so no official client program is provided. However, a Python sample code is available. Please refer to the following and adapt it to other languages or platforms as needed.

https://gist.github.com/uezo/9e56a828bb5ea0387f90cc07f82b4c15

Or, if you want to build AITuber (AI VTuber), try AITuber demo with [ChatdollKit AITuber Controller](https://github.com/uezo/chatdollkit-aituber) that is using `SocketServer` internally.


## 🌐 Run on WebGL

Refer to the following tips for now. We are preparing demo for WebGL.

- It takes 5-10 minutes to build. (It depends on machine spec)
- Very hard to debug. Error message doesn't show the stacktrace: `To use dlopen, you need to use Emscripten’s linking support, see https://github.com/kripken/emscripten/wiki/Linking` 
- Built-in Async/Await doesn't work (app stops at `await`) because JavaScript doesn't support threading. Use [UniTask](https://github.com/Cysharp/UniTask) instead.
- CORS required for HTTP requests.
- Microphone is not supported. Use `ChatdollMicrophone` that is compatible with WebGL.
- Compressed audio formats like MP3 are not supported. Use WAV in SpeechSynthesizer.
- OVRLipSync is not supported. Use [uLipSync](https://github.com/hecomi/uLipSync) instead.
- You also add the code below to your main script to enable uLipSync:
    ```
    var ul = gameObject.GetComponent<uLipSync.uLipSync>();
    modelController.HandlePlayingSamples = (samples) =>
    {
        ul.OnDataReceived(samples, 1);
    };
    ```
- If you want to show multibyte characters in message window put the font that includes multibyte characters to your project and set it to message windows.


## 🔄 Migration from 0.7.x

The easiest way is deleting `Assets/ChatdollKit` and import ChatdollKit unitypackage again. But if you can't do so for some reasons, you can solve errors by following steps:

1. Import the latest version of ChatdollKit unitypackage. Some errors will be shown in the console.

1. Import ChatdollKit_0.7to084Migration.unitypackage.

1. Add `partial` keyword to `ModelController`, `AnimatedVoiceRequest` and `Voice`.

1. Replace `OnSayStart` with `OnSayStartMigration` in `DialogController`.

**⚠️Note**: This simply suppresses error outputs and does not enable continued use of legacy code. If any parts of your project still use `DialogController`, `LLMFunctionSkill`, `LLMContentSkill`, or `ChatdollKit`, replace each with the updated component as follows:

- `DialogController`: `DialogProcessor`
- `LLMFunctionSkill`: `Tool`
- `LLMContentSkill`: `LLMContentProcessor`
- `ChatdollKit`: `AIAvatar`


### Migration from FunctionSkill to Tool

If your component inherits from `LLMFunctionSkillBase`, you can easily migrate it to inherit from `ToolBase` by following these steps:

1. Change the inherited class

    Replace `LLMFunctionSkillBase` with `ToolBase` as the base class.

    ```md
    // Before
    public class MyFunctionSkill : LLMFunctionSkillBase

    // After
    public class MyFunctionSkill : ToolBase
    ```

1. Update the `ExecuteFunction` method signature

    Modify the `ExecuteFunction` method’s parameters and return type as follows:

    ```md
    // Before
    public UniTask<FunctionResponse> ExecuteFunction(string argumentsJsonString, Request request, State state, User user, CancellationToken token)

    // After
    public UniTask<ToolResponse> ExecuteFunction(string argumentsJsonString, CancellationToken token)
    ```

1. Update the return type of `ExecuteFunction`

    Change `FunctionResponse` to `ToolResponse`.



## ❤️ Thanks

- [uLipSync](https://github.com/hecomi/uLipSync) (LipSync) (c)[hecomi](https://twitter.com/hecomi)
- [UniTask](https://github.com/Cysharp/UniTask) (async/await integration) (c)[neuecc](https://x.com/neuecc)
- [UniVRM](https://github.com/vrm-c/UniVRM/releases/tag/v0.89.0) (VRM) (c)[VRM Consortium](https://x.com/vrm_pr) / (c)[Masataka SUMI](https://x.com/santarh) for MToon
