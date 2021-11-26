# ChatdollKit
ChatdollKit enables you to make your 3D model into a voice-enabled chatbot.

[🇯🇵日本語のREADMEはこちら](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)

# ✨ Features

- Model
    - Speech and motion synchronization
    - Face expression control
    - Blink and lipsync

- Dialog
    - Speech-to-Text (Azure, Google, Watson etc)
    - Text-to-Speech (Azure, Google, Watson, VOICEROID, VOICEVOX etc)
    - Dialog state management
    - Intent extraction and topic routing

- I/O
    - Wakeword
    - Camera and QR Code

... and more! See [ChatdollKit Documentation](https://github.com/uezo/ChatdollKit/blob/master/manual.md) to learn details.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/chatdollkit-overview.png" width="720">

# 🥳 Run Demo

We provide the demo that runs out-of-the-box even if you are too busy to walk through Quick Start below and don't have any API keys for speech services.👍

1. Import dependencies [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347) and [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)
1. Import [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406) for idle motions
1. Import [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) and [ChatdollKit_Demo.unitypackage](https://github.com/uezo/ChatdollKit/releases)
1. Open scene `Asset/Demo/DemoOOTB` and start application
1. Press `Start chat` button on the inspector of `ChatdollApplication` attached to the 3D model, and input and send request message (e.g. 今日はいい天気ですね)

If you have API keys for Azure / Google / Watson speech service, open `Asset/Demo/Azure`, `Google` or `Watson` and set API key to inspector of Main application that is attached to 3D model. You can talk to 3D model instead of text request.


# 🚀 Quick start index

You can learn how to setup ChatdollKit by watching this 2 minutes video: https://www.youtube.com/watch?v=aJ0iDZ0o4Es

1. 📦Import packages
    - Import [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347) and [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)
    - Import [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases)

1. 🐟Resource preparation
    - Import 3D model and put it on the scene
    - Put animation clips to animations directory 👉 For tutorial [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406)
    - Get API Key for [Azure Speech Services](https://azure.microsoft.com/ja-jp/services/cognitive-services/speech-services/), [Google Cloud Speech API](https://cloud.google.com/speech-to-text/) or [Watson](https://cloud.ibm.com/)

1. 🍣Setup
    - Add Echo example to your 3D model and set API key on inspector
    - Run `Setup ModelController` and `Setup Animator` in the context menu on inspector

# 📦 Import packages

Download the latest version of [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) and import it into your Unity project after import dependencies;

- [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)
- [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)

If you want to create [Gatebox](https://www.gatebox.ai/en/) application also import [ChatdollKit Gatebox Extension](https://github.com/uezo/ChatdollKit/releases).

# 🐟 Resource preparation

## 3D model

Add 3D model to the scene and adjust as you like. Also install required resources for the 3D model like shaders, Dynamic Bone etc.
In this README, I use Cygnet-chan that we can perchase at Booth. https://booth.pm/ja/items/1870320

<img src="https://uezo.blob.core.windows.net/github/chatdoll/camera_light.png" width="640">

## Animations

Create `/Animations` folder and put animation clips.
In this README, I use [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406). I believe it is worth for you to purchase the pro edition.


# 🍣 Setup

## Add ChatdollKit

Add `EchoAppAzure`, `EchoAppGoogle` or `EchoAppWatson` from `ChatdollKit/Excamples/Echo` to the 3D model. The required components will be added automatically, including `ModelController`, that controls animations, voices and face expressions of 3D model.

## Configure Application

At least API Key and some properties like Region and BaseUrl should be set on inspector of `EchoAppAzure`, `EchoAppGoogle` or `EchoAppWatson`.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/add_echoapp_mark.png" width="640">


## Setup ModelController

Select `Setup ModelController` in the context menu of ModelController and set the name of shapekey for blink to `Blink Blend Shape Name` if it is not set after setup.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/setup_mc.png" width="640">

If you want to setup manually, go to [Appendix1. Setup ModelController manually](#Appendix%201.%20Setup%20ModelController%20manually)

## Setup Animator

Select `Setup Animator` in the context menu of ModelController and select the folder that contains animation clips. If subfolders are included, layers with the same name as the subfolders are created in the AnimatorController, and clips in each subfolders are put on each layers.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/choose_animation_dir.png" width="640">

In this case you can select to put clips on `Base Layer` or create layers named `01_Idles`, `02_Layers` and `03_Others` and put on them.

After creating Animator Controller you can select default idle animation by editing `Default` status if you want to change.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/change_idle.png" width="640">

If you want to setup manually, go to [Appendix2. Setup Animator manually](#Appendix%202.%20Setup%20Animator%20manually)


## Run

Press Play button of Unity editor. You can see the model starts with idling animation and blinking.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/run_echo.png" width="640">

Okay, let's start chatting with your chatdoll now.

- Say "hello" or the word you set to `Wake Word` on inspector
- Your model will be reply "what's up?" or the word you set to `Prompt Voice` on inspector
- Say something you want to echo like "Hello world!"
- Your model will be reply "Hello world"


# 👷‍♀️ Build your own app

See the `MultiDialog` example. That is more rich application including:

- Dialog Routing: `Router` is an example of how to decide the topic user want to talk
- Processing dialog: `TranslateDialog` is an example that shows how to process dialog

We are now preparing contents to create more rich virtual assistant using ChatdollKit.


# ❤️ Thanks

- [Tsukuyomi-chan 3D model](https://tyc.rei-yumesaki.net/) (3D model for demo) (c)[Rei Yumesaki](https://twitter.com/TYC_Project)
- [VOICEVOX](https://voicevox.hiroshiba.jp) (Text-to-Speech service for demo) (c)[Hiroshiba](https://twitter.com/hiho_karuta)
- [Shikoku Metan and Zundamon](https://zunko.jp/con_voice.html) (Voice for demo, used in VOICEVOX TTS loader)

Strictly follow the [Term of Use of Shikoku Metan and Zundamon](https://zunko.jp/con_ongen_kiyaku.html). And, if you distribute the voice generated with VOICEVOX let the users follow that rules.
