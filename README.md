# ChatdollKit
ChatdollKit enables you to make your 3D model into a voice-enabled chatbot.

<!-- 
# Quick start guide

Watch this 2 minutes video to learn how ChatdollKit works and the way to use quickly. -->

[🇯🇵日本語のREADMEはこちら](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)

<img src="https://uezo.blob.core.windows.net/github/chatdoll/chatdollkit_architecture.png" width="640">


# 🚀 Quick start

1. 📦Import packages
    - Import [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347) and [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)
    - Import [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases)

1. 🐟Resource preparation
    - Import 3D model and put it on the scene
    - Put voice files to resource directory and animation clips to animations directory

1. 🍣Setup
    - Run `Setup ModelController` and `Setup Animator` in the context menu on inspector
    - Set the name of ShapeKey for blink


# 📦 Import packages

Download the latest version of [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) and import it into your Unity project after import dependencies;

- [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)
- [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)


# 🐟 Resource preparation

## 3D model

Add 3D model to the scene and adjust as you like. Also install required resources for the 3D model like shaders, Dynamic Bone etc.
In this README, I use Cygnet-chan that we can perchase at Booth. https://booth.pm/ja/items/1870320


## Voices

Create `/Resources/Voices` directory and put voices into there. If your don't have voice audio files to run the example, download from [here](https://soundeffect-lab.info/sound/voice/line-girl1.html). I use these 2 files in the Hello world example.

- こんにちは: `line-girl1-konnichiha1.mp3`
- 呼びました？: `line-girl1-yobimashita1.mp3`

<img src="https://uezo.blob.core.windows.net/github/chatdoll/03_2.png" width="640">


## Animations

Create `/Animations` folder and put animation clips.
In this README, I use [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406). I believe it is worth for you to purchase the pro edition.

# 🍣 Setup

## Add ChatdollKit

Add `ChatdollKit/ChatdollKit/Scripts/chatdoll.cs` to the 3D model. `ModelController` will be also added automatically.

- `ModelController` controls animations, voices and face expressions of 3D model.

## Setup ModelController

Select `Setup ModelController` in the context menu of ModelController and set the name of shapekey for blink to `Blink Blend Shape Name`.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/mceditor.png" width="640">

If you want to setup manually, go to [Appendix1. Setup ModelController manually](#Appendix%201.%20Setup%20ModelController%20manually)

## Setup Animator

Select `Setup Animator` in the context menu of ModelController and select the folder that contains animation clips.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/setupanimator01.png" width="640">

If subfolders are included, layers with the same name as the subfolders are created in the AnimatorController, and clips in each subfolders are put on each layers. (Case1. in the picture below)

<img src="https://uezo.blob.core.windows.net/github/chatdoll/setupanimator02.png" width="640">

If you want to setup manually, go to [Appendix2. Setup Animator manually](#Appendix%202.%20Setup%20Animator%20manually)

## Run

Press Play button of Unity editor. You can see the model starts with idling animation and blinking.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/07_2.png" width="640">

Then ChatdollKit is correctly configured except for voice settings!


# Hello world example

Here is how to configure and run "Hello world" example.

1. Open `Examples/HelloWorld/Scripts` and add `HelloWorld.cs` to the 3D model game object.

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/08_2.png" width="640">

1. Add `SimpleMessageWindow` prefab to the scene from `ChatdollKit/Prefabs/SimpleMessageWindow` directory.

1. Set the `SimpleMessageWindow` to the `Message Window` of the `Hello World`.

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/09_2.png" width="640">

1. Put something to say to the `Dummy Text` in the `Request Provider`. This text is sent to the Chatdoll as a request message instead of using speech recognition.

Play and click the `Start Chat` button in inspector. Confirm that she asks `呼びました？`, the value put in the dummy text is shown in the message box, and she says `こんにちは` as the result of hello dialog.


# Customize Hello world

## DialogRouter

`DialogRouter` is automatically added with `HelloWorld`. You can implement `ExtractIntentAsync` method to extract the intent and entities from what the user is saying. You see can the static rule by default.

```Csharp
request.Intent = "hello";
```

Replace this code like below or call some NLU service.

```Csharp
if (request.Text.ToLower().Contains("weather"))
{
    request.Intent = "weather";
    request.Entities["LocationName"] = ParseLocation(request.Text);
}
else if (...)
{

}
```

Besides this, you can customize what the 3D model says and animates for each intent by editing here.

```Csharp
var animatedVoiceRequest = new AnimatedVoiceRequest();
animatedVoiceRequest.AddVoice("line-girl1-haihaai1", preGap: 1.0f, postGap: 2.0f);
animatedVoiceRequest.AddAnimation("Default");
```

## DialogProcessor

HelloWorld example has a DialogProcessor named `hello` and it is implemented in `HelloDialog`. In this module, nothing is processed and just respond to say hello.

You can add your own skill to chatdoll by creating and adding the DialogProcessors implements `IDialogProcessor`. When the value of `TopicName` property is set to the `request.Intent` in the `DialogRouter`, the DialogProcessor is called and the `TopicName` is set to `Context.Topic.Name` to continue the successive conversation.

## RequestProvider

`RequestProvider` is just a mock to walk through the HelloWorld example. To create a pratical chatdoll, replace it with `AzureVoiceRequestProvider`, `GoogleVoiceRequestProvider` or your own RequestProvider that extends `VoiceRequestProviderBase` like below.

```csharp
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace YourApp
{
    [RequireComponent(typeof(VoiceRecorder))]
    public class MyVoiceRequestProvider : VoiceRequestProviderBase
    {
        protected override async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            // Call Speech-to-Text service
            var response = await client.PostBytesAsync<MyRecognitionResponse>(
                $"https://my_stt_service", AudioConverter.AudioClipToPCM(recordedVoice));

            // Return the recognized text
            return response.recognizedText;
        }
    }
}
```

# Deep Dive

We are now preparing contents to create more complex virtual assistant using ChatdollKit.

Basically, you can make the character more lively to improve the animations and set it to the idle animations and each situational actions. This activity requires the skill of Unity, so chatdoll provides the easy way for Unity beginner　(like me) to control the 3D model easily with just coding.

You can make the more useful virtual assistant to improve the conversation logic and backend functions. This acticity requires the skill to build chatbot, so chatdoll provides the basic framework to build chatbot that allows you to concentrate in coding the rules of intent extraction and each logic of dialogs.


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
