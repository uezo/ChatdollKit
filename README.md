# ChatdollKit
ChatdollKit enables you to make your 3D model into a voice-enabled chatbot.

<!-- 
# Quick start guide

Watch this 2 minutes video to learn how ChatdollKit works and the way to use quickly. -->


# Install

Clone or download this repository and put `ChatdollKit` directory into your Unity project after install dependencies below;

- [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)
- [Oculus Lipsync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)
- To use Speech-to-Text service to recognize what you are saying, install [Azure Speech SDK for Unity](https://docs.microsoft.com/ja-jp/azure/cognitive-services/speech-service/speech-sdk?tabs=windows) or [Google Cloud Speech Recognition](https://assetstore.unity.com/packages/add-ons/machinelearning/google-cloud-speech-recognition-vr-ar-mobile-desktop-pro-72625?locale=ja-JP) then add `AzureVoiceRequestProvider` or `GoogleCloudSpeechRequestProvider` in `ChatdollKit.Extension` directory. Note that Azure SDK doesn't support Mac OSX for now (March 2020). Just trying the `HelloWorldExample`, you can skip to install speech recognition libraries.
- To create Gatebox Application, install [GateboxSDK](https://developer.gatebox.biz/document). You have to sign up for Gatebox Developer Program to get the SDK.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/01.png" width="640">


# Resource preparation

## 3D model

Add 3D model to the scene and adjust as you like. Install required resources for the 3D model like shaders and Dynamic Bone at this time.
In this README, I use Cygnet-chan that we can perchase at Booth. https://booth.pm/ja/items/1870320

After adjustment, add following LipSync components using inspector.

- OVR Lip Sync Context Morph Target
- OVR Lip Sync Context

Then set the object that has the shapekeys for face expressions to `Skinned Mesh Renderer` in `OVR Lip Sync Context Morph Target` and turn on `Audio Loopback` in `OVR Lip Sync Context`.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/02.png" width="640">


## Voices

Create `/Resources/Voices` directory and put voices into there. If your don't have voice audio files to run the example, download from [here](https://soundeffect-lab.info/sound/voice/line-girl1.html). I use these 3 files in the Hello world example.

- こんにちは: `line-girl1-konnichiha1.mp3`
- 呼びました？: `line-girl1-yobimashita1.mp3`
- はいは〜い: `line-girl1-haihaai1.mp3`

<img src="https://uezo.blob.core.windows.net/github/chatdoll/03.png" width="640">


## Animations

Create Animator Controller and create `Default` state on the Base Layer, then put animations. Lastly set a motion you like to the `Default` state. You can create other layers and put animations at this time. Note that every layers should have the `Default` state and `None` should be set to their motion except for the Base Layer.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/04.png" width="640">

After configuration set the Animator Controller as a `Controller` of Animator component of the 3D model.
In this README, I use [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406). I believe it is worth for you to purchase the pro edition.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/05.png" width="640">


# Basic configuration

## Add ChatdollKit

Add `Chatdoll/chatdoll.cs` to the 3D model. These 2 components are added automatically at this time.

- `ModelController` controls animations, voices and face expressions of 3D model.
- `MicEnabler` gets the permission to use microphone for speech recognition.

## ModelController configuration

Set LipSync object to `Audio Source` and set the object that has the shape keys for face expression to `Skinned Mesh Renderer`. Lastly set the shape key that close the eyes for blink to `Blink Blend Shape Name`.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/06.png" width="640">


# Hello world example

Here is how to configure and run "Hello world example".

1. Add `Examples/HelloWorld/HelloWorldExample` to the 3D model game object.

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/07.png" width="640">

1. Add `SimpleMessageWindow` prefab to the scene from `Examples` directory and set `Font`. Arial is the default.

1. Set the `SimpleMessageWindow` to the `Message Window` of the `Hello World Example`.

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/08.png" width="640">

1. Put something to say to the `Dummy Text` in the `Dummy Request Provider`. This text is sent to the Chatdoll as a request message instead of using speech recognition.

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/09.png" width="640">


Play and click the `Start Chat` button in inspector. Confirm that she asks `呼びました？`, the value put in the dummy text is shown in the message box, she recognizes it as the hello intent and says `はいは〜い`, lastly she says `こんにちは` as the result of hello dialog.


# Customize Hello world

## IntentExtractor

`IntentExtractor` is automatically added with `HelloWorldExample`. You implement the rules to extract the intent and entities from what the user is saying. You see the static rule by default.

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

You can add your own skill to chatdoll by creating and adding the DialogProcessors implements `IDialogProcessor`. When the value of `TopicName` property is set to the `request.Intent` in the `IntentExtractor`, the DialogProcessor is called and the `TopicName` is set to `Context.Topic.Name` to continue the successive conversation.

## RequestProvider

`DummyRequestProvider` is just a mock to walk through the HelloWorld example. To create a pratical chatdoll you should replace it to `AzureVoiceRequestProvider`, `GoogleCloudSpeechRequestProvider` or your own RequestProvider implements `IRequestProvider` to recognize what the user is saying. Please refer the RequestProviders in `Chatdoll.Extension` to create your own.


# Deep Dive

We are now preparing contents to create more complex virtual assistant using ChatdollKit.

Basically, you can make the character more lively to improve the animations and set it to the idle animations and each situational actions. This activity requires the skill of Unity, so chatdoll provides the easy way for Unity beginner　(like me) to control the 3D model easily with just coding.

You can make the more useful virtual assistant to improve the conversation logic and backend functions. This acticity requires the skill to build chatbot, so chatdoll provides the basic framework to build chatbot that allows you to concentrate in coding the rules of intent extraction and each logic of dialogs.
