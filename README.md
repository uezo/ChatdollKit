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
    - Text-to-Speech (Azure, Google, Watson, Voiceroid etc)
    - Dialog state management
    - Intent extraction and topic routing

- I/O
    - Wakeword
    - Camera and QR Code

... and more!

<img src="https://uezo.blob.core.windows.net/github/chatdoll/chatdollkit_architecture.png" width="640">

# 🚀 Quick start

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
    - Add `Extension/OVR/OVRLipSyncHelper` to your 3D model


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

## Setup LipSync

Add `OVRLipSyncHelper` from `Extension/OVR` to the 3D model. If the model is built as VRC FBX format or VRM format the configuration will complete automatically.

## Run

Press Play button of Unity editor. You can see the model starts with idling animation and blinking.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/run_echo.png" width="640">

Okay, let's start chatting with your chatdoll now.

- Say "hello" or the word you set to `Wake Word` on inspector
- Your model will be reply "what's up?" or the word you set to `Prompt Voice` on inspector
- Say something you want to echo like "Hello world!"
- Your model will be reply "Hello world"


# Build your own app

See the `MultiDialog` example. That is more rich application including:

- Dialog Routing: `Router` is an example of how to decide the topic user want to talk
- Processing dialog: `TranslateDialog` is an example that shows how to process dialog

We are now preparing contents to create more rich virtual assistant using ChatdollKit.


# Appendix 1. Setup ModelController manually

Create a new empty GameObject attach `OVR Lip Sync Context` and `OVR Lip Sync Context Morph Target`.

- Then turn on `Audio Loopback` in `OVR Lip Sync Context`
- Set the object that has the shapekeys for face expressions to `Skinned Mesh Renderer` in `OVR Lip Sync Context Morph Target`
- Configure viseme to blend targets in `OVR Lip Sync Context Morph Target`

<img src="https://uezo.blob.core.windows.net/github/chatdoll/02_2.png" width="640">

After that, select root GameObject to which ModelController is attached.

- Set LipSync object to `Audio Source`
- Set the object that has the shape keys for face expression to `Skinned Mesh Renderer`
- Set the shape key that close the eyes for blink to `Blink Blend Shape Name`.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/06_2.png" width="640">

# Appendix 2. Setup Animator manually

Create Animator Controller and create `Default` state on the Base Layer, then put animations. Lastly set a motion you like to the `Default` state. You can create other layers and put animations at this time. Note that every layers should have the `Default` state and `None` should be set to their motion except for the Base Layer.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/04.png" width="640">

After configuration set the Animator Controller as a `Controller` of Animator component of the 3D model.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/05_2.png" width="640">


# Appendix 3. Using uLipSync

If you want to use uLipSync instead of OVRLipSync please follow the official readme. (Apple Store doesn't accept the app using OVRLipSync🙃)

https://github.com/hecomi/uLipSync

We don't provide LipSyncHelper for it because it doesn't have a function to reset viseme. But don't worry, it works without any helpers. 
