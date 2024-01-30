# ChatdollKit
3D virtual assistant SDK that enables you to make your 3D model into a voice-enabled chatbot. [🇯🇵日本語のREADMEはこちら](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)

- [🍎 iOS App: OshaberiAI](https://apps.apple.com/us/app/oshaberiai/id6446883638) A Virtual Agent App made with ChatdollKit: a perfect fusion of character creation by AI prompt engineering, customizable 3D VRM models, and your favorite voices by VOICEVOX.
- [🇬🇧 Live demo English](https://uezo.blob.core.windows.net/github/chatdollkit/demo_en/index.html) Say "Hello" to start conversation. This demo just returns what you say (echo).
- [🇯🇵 Live demo in Japanese](https://unagiken.com/chatdollkit/playground/index.html) OpenAI API Keyをご用意ください。「こんにちは」と話しかけると会話がスタートします。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/chatdollkit-overview.png" width="720">


# ✨ Features

- 3D Model
    - Speech and motion synchronization
    - Face expression control
    - Blink and lipsync

- Generative AI
    - Multiple LLMs: ChatGPT / Azure OpenAI Service, Anthropic Claude, Google Gemini Pro and others
    - Agents: Function Calling (ChatGPT / Gemini) or your prompt engineering
    - Multimodal: GPT-4V and Gemini-Pro-Vision are suppored
    - Emotions: Autonomous face expression and animation

- Dialog
    - Speech-to-Text and Text-to-Speech (OpenAI, Azure, Google, Watson, VOICEVOX, VOICEROID etc)
    - Dialog state management (in other word, context or memory)
    - Intent extraction and topic routing

- I/O
    - Wakeword
    - Camera and QR Code

- Platforms
    - Windows / Mac / Linux / iOS / Android and anywhere Unity supports
    - VR / AR / WebGL / Gatebox

... and more! See [ChatdollKit Documentation](Documents/manual.md) to learn details.


# 🚀 Quick start

You can learn how to setup ChatdollKit by watching this video that runs the demo scene(including chat with ChatGPT): https://www.youtube.com/watch?v=rRtm18QSJtc

[![](https://img.youtube.com/vi/rRtm18QSJtc/0.jpg)](https://www.youtube.com/watch?v=rRtm18QSJtc)

## 📦 Import packages

Download the latest version of [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) and import it into your Unity project after import dependencies;

- `Burst` from Unity Package Manager (Window > Package Manager)
- [UniTask](https://github.com/Cysharp/UniTask)(Ver.2.3.1)
- [uLipSync](https://github.com/hecomi/uLipSync)(v2.6.1)
- For VRM model: [UniVRM](https://github.com/vrm-c/UniVRM/releases/tag/v0.89.0)(v0.89.0) and [VRM Extension](https://github.com/uezo/ChatdollKit/releases)
- JSON.NET: If your project doesn't have JSON.NET, add it from Package Manager > [+] > Add package from git URL... > com.unity.nuget.newtonsoft-json
- [Azure Speech SDK](https://learn.microsoft.com/ja-jp/azure/ai-services/speech-service/quickstarts/setup-platform?pivots=programming-language-csharp&tabs=macos%2Cubuntu%2Cdotnetcli%2Cunity%2Cjre%2Cmaven%2Cnodejs%2Cmac%2Cpypi#install-the-speech-sdk-for-unity): (Optional) Required for real-time speech recognition using a stream.

<img src="Documents/Images/burst.png" width="640">


## 🐟 Resource preparation

Add 3D model to the scene and adjust as you like. Also install required resources for the 3D model like shaders etc.
In this README, I use Cygnet-chan that we can perchase at Booth. https://booth.pm/ja/items/1870320

And, import animation clips. In this README, I use [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406). I believe it is worth for you to purchase the pro edition👍


## 🎁 Put ChatdollKit prefab

Put `ChatdollKit/Prefabs/ChatdollKit` or `ChatdollKit/Prefabs/ChatdollKitVRM` to the scene. And, create EventSystem to use UI components.

<img src="Documents/Images/chatdollkit_to_scene.png" width="640">


## 🐈 ModelController

Select `Setup ModelController` in the context menu of ModelController. If *NOT* VRM, make sure that shapekey for blink to `Blink Blend Shape Name` is set after setup. If not correct or blank, set it manually.

<img src="Documents/Images/modelcontroller.png" width="640">


## 💃 Animator

Select `Setup Animator` in the context menu of ModelController and select the folder that contains animation clips or their parent folder. In this case put animation clips in `01_Idles` and `03_Others` onto `Base Layer` for override blending, `02_Layers` onto `Additive Layer` for additive blending.

<img src="Documents/Images/animator.gif" width="640">

Next, see the `Base Layer` of newly created AnimatorController in the folder you selected. Confirm the value for transition to the state you want to set it for idle animation.

<img src="Documents/Images/idleanimation01.png" width="640">

Lastly, set the value to `Idle Animation Value` on the inspector of ModelController.

<img src="Documents/Images/idleanimation02.png" width="640">


## 🦜 DialogController

On the inspector of `DialogController`, set `Wake Word` to start conversation (e.g. hello / こんにちは🇯🇵), `Cancel Word` to stop comversation (e.g. end / おしまい🇯🇵), `Prompt Voice` to require voice request from user (e.g. what's up? / どうしたの？🇯🇵).

<img src="Documents/Images/dialogcontroller.png" width="640">


## 🍣 ChatdollKit

Select the speech service (OpenAI/Azure/Google/Watson) you use and set API key and some properties like Region and BaseUrl on inspector of ChatdollKit.

<img src="Documents/Images/chatdollkit.png" width="640">


## 🍳 Skill

Attach `Examples/Echo/Skills/EchoSkill` to `ChatdollKit`. This is a skill for just echo. Or, if you want to enjoy conversation with AI, attach components and set OpenAI API Key to `ChatGPTService`:

- ChatdollKit/Scripts/LLM/ChatGPT/ChatGPTService
- ChatdollKit/Scripts/LLM/LLMRouter
- ChatdollKit/Scripts/LLM/LLMContentSkill

<img src="Documents/Images/skill.png" width="640">


## 🤗 Face Expression (*NON* VRM only)

Select `Setup VRC FaceExpression Proxy` in the context menu of VRC FaceExpression Proxy. Neutral, Joy, Angry, Sorrow and Fun face expression with all zero value and Blink face with blend shape for blink = 100.0f are automatically created.

<img src="Documents/Images/faceexpression.png" width="640">

You can edit shape keys by editing Face Clip Configuration directly or by capturing on inspector of VRCFaceExpressionProxy.

<img src="Documents/Images/faceexpressionedit.png" width="640">


## 🥳 Run

Press Play button of Unity editor. You can see the model starts with idling animation and blinking.

- Say the word you set to `Wake Word` on inspector (e.g. hello / こんにちは🇯🇵)
- Your model will reply the word you set to `Prompt Voice` on inspector (e.g. what's up? / どうしたの？🇯🇵)
- Say something you want to echo like "Hello world!"
- Your model will reply "Hello world"

<img src="Documents/Images/run.png" width="640">


# 🌊 Use Azure OpenAI Service

To use Azure OpenAI Service set following info on inspector of ChatGPTService component:

1. Endpoint url with configurations to `Chat Completion Url`
```
format: https://{your-resource-name}.openai.azure.com/openai/deployments/{deployment-id}/chat/completions?api-version={api-version}
```

2. API Key to `Api Key`

3. Set true to `Is Azure`

NOTE: `Model` on inspector is ignored. Engine in url is used.


# 👷‍♀️ Build your own app

See the `MultiSkills` example. That is more rich application including:

- Dialog Routing: `Router` is an example of how to decide the topic user want to talk
- Processing dialog: `TranslateDialog` is an example that shows how to process dialog

We are now preparing contents to create more rich virtual assistant using ChatdollKit.


# 🌐 Run on WebGL

Refer to the following tips for now. We are preparing demo for WebGL.

- It takes 5-10 minutes to build. (It depends on machine spec)
- Very hard to debug. Error message doesn't show the stacktrace: `To use dlopen, you need to use Emscripten’s linking support, see https://github.com/kripken/emscripten/wiki/Linking` 
- Built-in Async/Await doesn't work (app stops at `await`) because JavaScript doesn't support threading. Use [UniTask](https://github.com/Cysharp/UniTask) instead.
- CORS required for HTTP requests.
- Microphone is not supported. Use `ChatdollMicrophone` that is compatible with WebGL.
- Compressed audio formats like MP3 are not supported. Use WAV in TTS Loaders.
- OVRLipSync is not supported. Use [uLipSync](https://github.com/hecomi/uLipSync) and [uLipSyncWebGL](https://github.com/uezo/uLipSyncWebGL) instead.
- If you want to show multibyte characters in message window put the font that includes multibyte characters to your project and set it to message windows.


# ❤️ Thanks

- [uLipSync](https://github.com/hecomi/uLipSync) (LipSync) (c)[hecomi](https://twitter.com/hecomi)
