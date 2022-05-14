# ChatdollKit Documentation

version 0.5.0 | 🎏 May 5, 2022 | &copy;2020 uezo | [🇯🇵Japanese version](./manual.ja.md)

- [Setup](#Setup)
    - [ChatdollKit configurations](#chatdollkit-configurations)
    - [DialogController configurations](#dialogcontroller-configurations)

- [Control 3D model](#Control-3D-model)
    - [Speech](#Speech)
        - [Common arguments for AddVoice](#Common-arguments-for-AddVoice)
        - [Local voice](#Local-voice)
        - [Web voice](#Web-voice)
        - [Text-to-Speech voice](#Text-to-Speech-voice)
    - [Motion](#Motion)
    - [Face expression](#Face-expression)
        - [Configure on inspector](#Configure-on-inspector)
        - [Configure in script](#Configure-in-script)
    - [Syncronize voice with motion and face](#Syncronize-voice-with-motion-and-face)
    - [Idling motion](#Idling-motion)
    - [Blink](#Blink)

- [Conversation](#Conversation)
    - [Conversation flow](#Conversation-flow)
    - [Prompt](#Prompt)
    - [Skill](#Skill)
    - [Request, State and User](#request-state-and-user)
        - [Request](#request)
        - [State](#state)
        - [User](#user)
    - [Multi-turn conversation](#Multi-turn-conversation)
    - [Pre-processing](#Pre-processing)
    - [Routing](#Routing)
        - [Extract intent](#Extract-intent)
        - [Extract entities](#Extract-entities)
        - [Priority](#Priority)
        - [Adhoc intent](#Adhoc-intent)

- [I/O](#I/O)
    - [Wake word](#Wake-word)
        - [WakeWord Settings](#WakeWord-Settings)
        - [Test and Debug](#test-and-debug)
        - [Voice Recorder Settings](#voice-recorder-settings)
    - [Voice Request](#Voice-Request)
        - [Cancellation Settings](#cancellation-settings)
        - [Test and Debug](#test-and-debug-1)
        - [Voice Recorder Settings](#voice-recorder-settings-1)
        - [UI](#ui)
    - [Camera Request](#Camera-Request)
        - [Use camera for the first request](#Use-camera-for-the-first-request)
        - [Use camera in conversation](#Use-camera-in-conversation)
    - [QRCode Request](#QRCode-Request)
        - [Read for the first request](#Read-for-the-first-request)
        - [Read in conversation](#Read-in-conversation)
        - [Decode QR Code](#Decode-QR-Code)

- [Deep dive](#Deep-dive)
    - [Processing request on remote server](#processing-request-on-remote-server)
    - [Morphological Analysis (Japanese only)](#Morphological-Analysis-(Japanese-only))

- [Support](#Support)


# Setup

See [README](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md) to learn how to setup. To learn to make your own custom skills see [Skill](#Skill) , or, to learn to switch skills by request message see [Routing](#Routing).

## ChatdollKit configurations

- Application Name: (Required) Name of application
- Speech Service: (Required) Cloud service for speech recognition and text-to-speech. You can select Azure/Google/Watson or Other to setup by yourself manually.
- API Key, etc: Service specific configurations

## DialogController configurations

- Wake Word: (Required) The word to make app waiting for request. This is like "Hey Siri" or "Okay, Google" for AI smart speakers. See also see [WakeWord](#wake-word) if you want to configure multiple wakewords or wakeword that triggers a topic.
- Cancel Word: (Required) The word to stop the conversation.
- Prompt Voice: (Required) Prompt message to require the request message from user.
- Prompt Voice Type: (Required) Local (File on local computer) / Web (File on Web) / TTS (Text-to-Speech service)
- Prompt Face: Face expression on prompt
- Prompt Animation: Animation on prompt. The name of status in AnimatorController.
- Error Voice: Error message
- Error Voice Type: Local (File on local computer) / Web (File on Web) / TTS (Text-to-Speech service)
- Error Face: Face expression on error
- Error Animation: Animation on error. The name of status in AnimatorController.
- Use Remote Server: Use skills on remote server instead of local skills.
- Base Url: Url of skill server
- Message Window: Window to show the message from user. Default is `SimpleMessageWindow`.
- ChatdollCamera: Camera to capture photo or QR Code. Default is `ChatdollCamera`.


# Control 3D model

ChatdollKit provides code-based features to make your 3D model speak, play motion and change face expression. These features are available without deep understanding of Unity's motion control and state machine.
Mainly use `ModelController.AnimatedSay()` method to control your 3D model.

```csharp
// Make request
var animatedVoiceRequest = new AnimatedVoiceRequest();
animatedVoiceRequest.AddVoiceTTS("Good morning!");
animatedVoiceRequest.AddAnimation("wavehands");
animatedVoiceRequest.AddFace("Smile");

// Say good morning with wave hands and smiling
modelController.AnimatedSay(animatedVoiceRequest, CancellationToken.None);
```

The reference of ModelController instance is set to `modelController` by default in the base class of some ChatdollKit components including Skill, Router and application.

## Speech

To make your 3D model speak add voices to `AnimatedVoiceRequest` with `AddVoice*` methods. `AddVoice` for local audio file, `AddVoiceTTS` for Text-to-Speech service and `AddVoiceWeb` for audio file on web are available.

### Common arguments for AddVoice

- preGap: Silence before speech (sec). Default is `0.0f`.
- postGap: Silence after speech (sec). Default is `0.0f`
- description: Description of this voice. This will be recorded to history for debug.
- asNewFrame: Create new frame to synchronize with motion and face. Default is `false`. See also [Syncronize voice with motion and face](#Syncronize-voice-with-motion-and-face).

```csharp
animatedVoiceRequest.AddVoice("Hello", preGap: 0.5f, postGap: 1.0f, description: "This is test description", asNewFrame: true);
```

Chatdoll will speak sequencially when you add multiple voices. In this example, say "Hello", wait 0.5 sec and say "It's fine today".

```csharp
animatedVoiceRequest.AddVoiceTTS("Hello.", postGap: 0.5f);
animatedVoiceRequest.AddVoiceTTS("It's fine today");
```

### Local voice

Use `AddVoice`. Set resource name of audio file to `name` argument.

```csharp
animatedVoiceRequest.AddVoice("Hello");
```

If you want to load audio files in `Resources` directory, the easiest snippet is like below. Put this in `Awake` or `Start` in your application. The name to identify each voice is the file name without extension.

```csharp
foreach (var audioClip in Resources.LoadAll<AudioClip>("Voices"))
{
    modelController.AddVoice(audioClip.name, audioClip);
}
```

### Web voice

Use `AddVoiceWeb`. Set url for the voice to `url` argument.

```csharp
animatedVoiceRequest.AddVoiceWeb("https://~~~~~/voice.wav");
```

Note that the first time the voice is used it takes some delay because ChatdollKit starts loading from web when `AnimatedSay` is called. The voice will be cached to speak without delay next time. See also [Syncronize voice with motion and face](#Syncronize-voice-with-motion-and-face) to prevent delay.

### Text-to-Speech voice

Use `AddVoiceTTS`. Set the text you want to make your 3D model speak to `text` argument.

```csharp
animatedVoiceRequest.AddVoiceTTS("This voice is created dynamically by Text-to-Speech service.");
```

Attach `TTSLoader` to your 3D model to use `AddVoiceTTS`. You can make your TTSLoader by extending `WebVoiceLoaderBase` if you want to use other Text-to-Speech service other than these 4 services.

- AzureTTSLoader: [Azure Speech Services](https://azure.microsoft.com/ja-jp/services/cognitive-services/speech-services/)
- GoogleTTSLoader: [Google Cloud Speech](https://cloud.google.com/speech-to-text?hl=ja)
- WatsonTTSLoader: [Watson](https://cloud.ibm.com/)
- VoiceroidTTSLoader: [Voiceroid Daemon](https://github.com/Nkyoku/voiceroid_daemon)

Use `ttsConfig` if you want to configure parameters like pitch and speed. See the document of TTS service you use to know what keys/values to set.

```csharp
var ttsConfig = new TTSConfiguration();
ttsConfig.Params.Add("Pitch", 2.0f);
ttsConfig.Params.Add("Speed", 2.0f);

animatedVoiceRequest.AddVoiceTTS("I speak this message in a high-pitched voice at twice the speed.", ttsConfig: ttsConfig);
```

## Motion

To make your 3D model play motion, add animation to `AnimatedVoiceRequest` with `AddAnimation` method.

- name: State name on Animator Controller.
- layerName: Layer name of the animation to play. Default is `Base Layer`.
- duration: Duration to keep playing animation (sec).
- fadeLength: Cross fade time from current animation to next animation (sec). Default is `Animation Fade Length` value on inspector.
- weight: Weight for applying animation. Default is `1.0f`(=Max).
- preGap: Gap before starting animation (sec). Default is `0.0f`.
- description: Description of this animation. This will be recorded to history for debug.
- asNewFrame: Create new frame to synchronize with voice and face. Default is `false`. See also [Syncronize voice with motion and face](#Syncronize-voice-with-motion-and-face).

```csharp
// Start walking for 3 seconds after waiting 1 second.
animatedVoiceRequest.AddAnimation("walk", duration: 3.0f, preGap: 1.0f);
// Stop walking smoothly in 0.5 seconds.
animatedVoiceRequest.AddAnimation("stand", fadeLength: 0.5f);
```

Note that animations other than the base layer will not be executed sequentially as the example above, but in parallel with the base layer.

```csharp
// Start walking for 3 seconds after waiting 1 second.
animatedVoiceRequest.AddAnimation("walk", duration: 3.0f, preGap: 1.0f);
// Stop walking smoothly in 0.5 seconds.
animatedVoiceRequest.AddAnimation("stand", fadeLength: 0.5f);
// Waving hands from start to end in parallel
animatedVoiceRequest.AddAnimation("wavinghands", "Upper Body");
```


## Face expression

To change face expression, add face to `AnimatedVoiceRequest` with `AddFace` method.

- name: Name of face expression.
- duration: Time to keep the face expression (sec). Default it forever.
- description: Description of this face expression. This will be recorded to history for debug.
- asNewFrame: Create new frame to synchronize with voice and motion. Default is `false`. See also [Syncronize voice with motion and face](#Syncronize-voice-with-motion-and-face).

```csharp
animatedVoiceRequest.AddFace("Smile", 2.0f);
animatedVoiceRequest.AddFace("Neutral");
```

ChatdollKit provides 2 ways to configure face expression.

### Configure on inspector

Make face expression you like by inspector of the object that has the shapekeys. Then press `Capture` button on the bottom of inspector of ModelController to save the current face expression with its name (e.g. Smile).

### Configure in script

Use `ModelController.AddFace()` method in your script.

- name: Name of this face expression.
- weights: Name of each shapekey and its weight value as `Dictionary<string, float>`. Note that the shapekeys that is not configured will not be changed in runtime. In other words all shapekeys should be configured if you want to reset face before change.
- asDefault: Register this face expression as the default face expression or not. Default is `false`.


## Syncronize voice with motion and face

Simply add voices, animations and faces to `AnimatedVoiceRequest` to make combined expression.

```csharp
// Talk about today's lunch with left hand on waist and smile face.
animatedVoiceRequest.AddVoiceTTS("Hello. I will have soba noodle for lunch today.");
animatedVoiceRequest.AddAnimation("left_hand_on_waist");
animatedVoiceRequest.AddFace("Smile");
```

Set `true` to `asNewFrame` to synchronize motion and face with voice.

```csharp
// Say hello with waving hands
animatedVoiceRequest.AddVoiceTTS("Hello, everyone!", postGap: 2.0f);
animatedVoiceRequest.AddAnimation("wavehands");
animatedVoiceRequest.AddFace("Smile");
// Stop waving hands and start saying thank you
animatedVoiceRequest.AddVoiceTTS("Thank you for comming today!", asNewFrame: true);
animatedVoiceRequest.AddAnimation("stand");
animatedVoiceRequest.AddFace("Neutral");
```


## Idling motion

`Default` state on `Base Layer` is playing when no conversations or animation requests are processing. Use `ModelController.AddIdleAnimation()` to register more complicated idling motion like combination of multiple animation and faces.

- animationName: State name on Animator Controller
- faceName: Name of face expression. (Optional)
- duration: Length to keep playing (sec). Default is `Idle Animation Default Duration` value on inspector.
- fadeLength: Cross fade time from pose to pose (sec). Default is `Animation Fade Length` value on inspector.
- preGap: Wait time to start animation (sec). Default is `0.0f`.
- disableBlink: Stop blinking or not. Default is `false`. Set `true` to use face expression with eyes closed.
- weight: Probability of using this idle animation. Default is `1`. This configuration is for the case that you register multiple idle animations.

For example, these 4 idle motions are played for seconds configured to `Idle Animation Default Duration` at random. In this case the probability to be played for `idle01` is 60%, `idle02` is 30%, `idle03` is 9% and `idle04` is 1%.

```csharp
modelController.AddIdleAnimation("idle01", weight: 60);
modelController.AddIdleAnimation("idle02", "Doya", weight: 30);
modelController.AddIdleAnimation("idle03", "Smile", disableBlink: true, weight: 9);
modelController.AddIdleAnimation("idle04", weight: 1);
```

You can use another overload that takes `AnimatedVoiceRequest` to add more complicated idle animations.

```csharp
var idle05 = new AnimatedVoiceRequest();
idle05.AddAnimation("wave_hands", "Others", duration: 3.0f);
idle05.AddFace("Smile");
idle05.AddAnimation("AGIA_Idle_calm_02_hands_on_front", duration: 20.0f, asNewFrame: true);
idle05.AddFace("Neutral");
modelController.AddIdleAnimation(idle05, weight: 2);
```

## Blink

Blink is controlled fully automatically. You can configure interval and speed on inspector of `ModelController`.

- Blink Blend Shape Name: Name of shapekey to close eyes.
- Min Blink Interval To Close: Minimum interval of blink (sec).
- Max Blink Interval To Close: Maximum interval of blink (sec).
- Min Blink Interval To Open: Minimum length to keep eyes closing (sec).
- Max Blink Interval To Open: Maximum length to keep eyes closing (sec).
- Blink Transition To Close: Length from start closing eyes to finish closing (sec).
- Blink Transition To Open: Length from start opening eyes to finish opening (sec).


# Conversation

ChatdollKit provides features to control conversation. Basically these features are are designed based on the general concepts of chatbot development.

## Conversation flow

This is the basic conversation frow.

- User: "Hello" <- wake word
- App: "May I help you?" <- prompt
- User: "How is the weather today?" <- request
- App: (determine WeatherSkill to process this request) <- routing
- App: (process this request with WeatherSkill) <- skill
- App: "It's fine today" <- response

## Prompt

Prompt is the message and motion that requires to user to input request. Set function to `ChatdollKit.OnPromptAsync` in your main application like below or set prompt message on inspector instead if you use `ChatdollKit` or its subclass. 

```csharp
// Message, motion and face expression for prompt
var promptAnimatedVoiceRequest = new AnimatedVoiceRequest();
promptAnimatedVoiceRequest.AddVoiceTTS("May I help you?", preGap: 0.5f);
promptAnimatedVoiceRequest.AddAnimation("PromptPose");
promptAnimatedVoiceRequest.AddFace("Smile");
　：
// Set function to chatdoll.OnPromptAsync
chatdoll.OnPromptAsync = async (preRequest, user, state, token) =>
{
    await modelController.AnimatedSay(promptAnimatedVoiceRequest, token);
};
```

## Skill

`Skill` is the function that solves user's intent in conversation. You can make it by implementing `ISkill` interface or extending `SkillBase` that has some basic features. To make skill at least you have to override `ProcessAsync` that returns `Response`. This example is the echo skill. 

```csharp
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;

public class EchoSkill : SkillBase
{
    public override async Task<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
    {
        // Make response with request id
        var response = new Response(request.Id);

        // Set echo voice
        response.AddVoiceTTS(request.Text);

        return response;
    }
}
```

`response` has `AnimatedVoiceRequest` internally and has some `Add*` methods for shortcut. For example `response.AddVoiceTTS` equals to `response.AnimatedVoiceRequest.AddVoiceTTS`. If you make your skill by extending `SkillBase`, the voices, animations and face expressions will be played by `SkillBase.ShowResponseAsync()`.


## Request, State and User

`Request request`, `State state` and `User user` passed as arguments for `ProcessAsync` are similar to Request and Session in Web application.

### Request

`Request` is the input from user in current turn.

- Id: Key to identify the request.
- Type: Type of request. `Voice`, `Camera` and `QRCode`.
- CreatedAt: The date and time of this request is created.
- Text: Input message as text.
- Payloads: Data attached to this request like photo.
- Intent: Intent extracted from user's input.
    - Name: Name of intent. (weather, translation, chatting, etc)
    - Priority: Priority to determine to start new conversation for this intent. Compared with `State.Topic.Priority`.
    - IsAdhoc: Adhoc intent or not. See also [Routing](Routing).
- Entities: Data included in this request. e.g. "Tokyo" for weather intent.
- Words: Result of morphological analysis. See also [Morphological Analysis](#Morphological-Analysis-(Japanese-only)).
- IsCanceled: Cancellation request or not.
- ClientId: Identifier of this application. (and its owner, if required)
- Tokens: Tokens to verify the client.


### State

`State` is the information kept in multi-turn as long as the conversation continues.

- Id: Key to indentify this state.
- UserId: User's Id who owns this state.
- UpdatedAt: Last update date and time.
- IsNew: Newly created or get from store
- Topic: Topic of conversation
    - Name: Name
    - Status: Status
    - IsFirstTurn: First turn or not.
    - Priority: Priority to determine to continue this topic in comparison with `Intent.Priority`.
- Data: Key-value data of this topic. This data is available as long as the topic continues.


The usage of `Topic.Status` is like below.

```csharp
// Processing. Update state here
　：
// Make response for each status
if (state.Topic.Status == "Success")
{
    response.AddVoiceTTS($"Weather in {state.Data["place"]} is {state.Data["weather"]}.");
}
else if (state.Topic.Status == "NoPlace")
{
    response.AddVoiceTTS("Where do you want to know the weather?");
}
```

### User

`User` is the information of the user.

- Id: key to identify the user.
- DeviceId: Key to identify the device.
- Name: Name of the user.
- Nickname: Nickname of the user.
- Data: Key-Value data of the user.

Note that updated `User` information is saved automatically except for the case that the conversation has errors.

```csharp
// Update user info in skill. This will be saved automatically at the end of this turn.
user.Nickname = "uezo";
user.Data["FavoriteFood"] = "soba";
```


## Multi-turn conversation

Set `false` to `response.EndTopic` in your skill to continue the current topic in the next turn.

```csharp
if (state.Topic.IsFirstTurn)
{
    // First turn comes here

    response.AddVoiceTTS("Tell us what you want us to translate.");
    // Set data for next turn
    if (request.Entities.ContainsKey("lang") && !string.IsNullOrEmpty(request.Entities["lang"]))
    {
        state.Data["target_language"] = request.Entities["lang"];
    }
    else
    {
        state.Data["target_language"] = "en-US";
    }

    // Set false to continue
    response.EndTopic = false;
}
else
{
    // 2nd turn comes here

    // Use data set in previous turn
    var translatedText = Translate(request.Text, state.Data["target_language"]);
    response.AddVoiceTTS(translatedText);
}
```

If you want to stop conversation completely set `true` to `response.EndConversation`. The message window will be hidden and the app stop waiting request after this response.

## Pre-processing

Override `PreProcessAsync` if you want to process something before `ProcessAsync`. Note that `ProcessAsync` is executed in the background of the speech, animation and face expressions in the response from `PreProcessAsync` concurrently.

It is a nice hack to use `PreProcessAsync` for reducing wait time for the user like below.

e.g. Weather skill
- `PreProcessAsync` returns short voice message
- `ShowResponseAsync` plays "Okay. The weather in Tokyo is" (2-3 sec)
- Concurrently `ProcessAsync` returns weather information voice message
- `ShowResponseAsync` plays "fine today." after both `ShowResponseAsync` and `ProcessAsync` finishes.

-> User can hear "Okay. The weather in Tokyo is fine today." in one sentence, not separated.


## Routing

`SkillRouter` is a components for selecting the right skill to process the user's intent. You can make it by implementing `ISkillRouter` interface or extending `SkillRouterBase` that has some basic features.

### Extract intent

Implement or override `ExtractIntentAsync` that returns `IntentExtractionResult`. In this example, weather intent will be extracted when the request message contains "weather", or translation intent when contains "translate".

```csharp
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

public class Router : SkillRouterBase
{
    public override async Task<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
    {
        if (request.Text.Contains("weather"))
        {
            return new IntentExtractionResult("weather");
        }
        else if (request.Text.Contains("translate"))
        {
            return new IntentExtractionResult("translation");
        }
    }
}
```

This example simply determine the intent just contains keyword or not but using NLU services like LUIS (Microsoft) or DialogFlow (Google) is recommended for production use.


### Extract entities

Entity is extracted information from user input that is required to process skill. In this example `language` to translate to is the entity.

```csharp
if (request.Text.Contains("translate"))
{
    var targetLanguage = GetLanguage(request.Text);
    if (!string.IsNullOrEmpty(targetLanguage))
    {
        return new IntentExtractionResult(
            new Intent("translation"),
            new System.Collections.Generic.Dictionary<string, object>() {
                { "language", targetLanguage }
            }
        );
    }
}
```

### Priority

ChatdollKit supports prioritization to determine whether to continue current topic or to start new topic. In this example, when user says "How is the weather?", weather topic starts even if translation topic was continueing.

```csharp
if (request.Text.Contains("weather"))
{
    return new IntentExtractionResult("weather", Priority.High);
}
else if (request.Text.Contains("translate"))
{
    return new IntentExtractionResult("translation");
}
```

### Adhoc intent

In the example of [Priority](Priority), conversation ends after chatdoll answers the weather. If you want to go back to the previous topic ("translation" in this case) set `true` to `IsAdhoc` of current intent.

```csharp
if (request.Text.Contains("weather"))
{
    return new IntentExtractionResult(new Intent("weather", Priority.High, true));
}
else if (request.Text.Contains("translate"))
{
    return new IntentExtractionResult("translation");
}
```

Note that Adhoc intent should finish in the first turn, can't continue topic.

- User: "Translate to Japanese please"
- App: "Tell me the sentence to translate" <-translation(first turn)
- User: "I will have soba for lunch"
- App: "私はお昼にそばを食べます" <-translation(continue)
- User: "Udon for dinner"
- App: "夜はうどんです" <-translation(continue)
- User: "How is the weather in Tokyo?"
- App: "It's fine today" <-weather(first turn)
- User: "Thank you"
- App: "ありがとう" <-translation(continue)


# I/O

ChatdollKit provides features that use device functions like camera and microphone.

## Wake word

You can configure wake word like "Hey siri" for iOS devices or "OK Google" for Android/Google home devices. To make it available use `AzureWakeWordListener`, `GoogleWakeWordListener`, `WatsonWakeWordListener` or your own WakeWordListener by extending `WakeWordListenerBase`.

### WakeWord Settings

You can configure wake words on the inspector of WakeWordListener.

- Wake Words: Keyword to start conversation. You can configure multiple wake words.
    - Text: Wake word sentence. e.g. "hello"
    - Prefix Allowance: If the wake word is "hello", set 5 or higher to allow "Hey, hello".
    - Suffix Allowance: If the wake word is "chatdoll", set 5 or higher to allow "chatdoll-chan".
    - Intent: Specific intent for this wake word. Default is null. If set, the skill for this intent starts immediately without prompt, input request and extracting intent.
    - Request Type: Specific request type like camera or QRCode for this wake word. See also [Voice Request](#Voice-Request), [Camera Request](#Camera-Request) and [QRCode Request](#QRCode-Request).
    - Inline Request Minumum Length: Minimum length for request included in the sentence of wake word. e.g. 7 or larger should be set to recognize "weather" as request in "Hey, weather", when "hey" is registered as wake word.
- Cancel Words: Keywords to stop conversation.
- Ignore Words: Chars you want to remove from recognized text. e.g. ",", "?"
- Auto Start: Start WakeWordListener automatically when the app starts. Default is `true`.

### Test and Debug

- Print Result: Set `true` to print the word WakeWordListener recognized in console.

### Voice Recorder Settings

- Voice Detection Threshold: Minimum volume to listen. Set larger value when noisy environment.
- Voice Detection Minimum Length: Minimum length for wake word (sec). Ignore if shorter.
- Voice Recognition Maximum Length: Maximum length for wake word (sec). Ignore if longer.
- Silence Duration To End Recording: Seilence to recognize the end of speech (sec). 


## Voice Request

`VoiceRequestProvider` provides request message that includes transcription of voice. `AzureVoiceRequestProvider`, `GoogleVoiceRequestProvider` and `WatsonVoiceRequestProvider` is available and you can make your own provider by implementing `IRequestProvider` interface or extending `VoiceRequestProviderBase` with other Speech-to-Text services you like.

### Cancellation Settings

- Cancel Words: Keywords to stop conversation.
- Ignore Words: Chars you want to remove from recognized text. e.g. ",", "?"

### Test and Debug

- Use Dummy: Set `true` to use dummy text instead of get transcribed text from microphone.
- Dummy Text: Dummy text
- Print Result: Set `true` to print the word VoiceRequestProvider recognized in console.

### Voice Recorder Settings

- Voice Detection Threshold: Minimum volume to listen. Set larger value when noisy environment.
- Voice Detection Minimum Length: Minimum length for request (sec). Ignore if shorter.
- Silence Duration To End Recording: Seilence to recognize the end of speech (sec). 
- Listening Timeout: Maximum length for request (sec). Stop listening and start voice recognition if it comes to timeout.

### UI

- Message Window: Message window to show the text of request. Default is `SimpleMessageWindow`


## Camera Request

`CameraRequestProvider` provides request message that includes captured picture with camera.

### Use camera for the first request

To launch camera when the wake word recognized, set `Camera` to the RequestType on inspector of WakeWordLister. Also set an intent to process the picture with.

- User: "Hey, take an picture" <- wake word
- App: "Okay. Smile!" <- prompt
(Launch camera and take an picture)  <- request
- App: "Sent you by messanger" <- response

### Use camera in conversation

Set `RequestType.Camera` to `response.NextTurnRequestType` to use camera request in the next turn.

```csharp
if (state.Topic.IsFirstTurn)
{
    // Set camera to request type for the next turn
    response.NextTurnRequestType = RequestType.Camera;
    response.EndTopic = false;
    response.AddVoiceTTS("Okay, smile!");
}
else
{
    if (IsSmile(request.Payloads))
    {
        await SendPictureAsync(request.Payloads);
        response.AddVoiceTTS("Sent you by messanger.");
    }
    else
    {
        response.AddVoiceTTS("You didn't smile.");
    }
}
```

- User: "Hey, chatdoll" <- wake word
- App: "May I help you?" <- prompt
- User: "Take an picture" <- voice request (1st turn)
- App: "Okay. Smile!" <- response (1st turn)
(Launch camera and take an picture)  <- camera request (2nd turn)
- App: "Sent you by messanger" <- response (2nd turn)


## QRCode Request

`QRCodeRequestProvider` provides request message that includes decoded value of QRCode.

### Read for the first request

To launch QRCode reader when the wake word recognized, set `QRCode` to the RequestType on inspector of WakeWordLister. Also set an intent to process the QR Code with.

- User: "Hello" <- wake word
- App: "Hello. Let me see QR Code for check in" <- prompt
(Launch QR Code reader and decode it)  <- request
- App: "Nice to meet you, Kunikida-san from Numazu Bank" <- response

### Read in conversation

Set `RequestType.QRCode` to `response.NextTurnRequestType` to use QRCode request in the next turn.

```csharp
if (state.Topic.IsFirstTurn)
{
    // Set QRCode to request type for the next turn
    response.NextTurnRequestType = RequestType.QRCode;
    response.EndTopic = false;
    response.AddVoiceTTS("Let me see QR Code for check in.");
}
else
{
    // Decoded value of QRCode is set in request.Text
    var guest = GetGuest(request.Text);
    response.AddVoiceTTS($"Nice to meet you, {guest.Name}-san from {guest.Company}.");
}
```

- User: "Hey, chatdoll" <- wake word
- App: "May I help you?" <- prompt
- User: "Check in, please" <- (1st turn)
- App: "Let me see QR Code for check in" <- response (1st turn)
(Launch QR Code reader and decode it)  <- QRCode request (2nd turn)
- App: "Nice to meet you, Kunikida-san from Numazu Bank" <- response (2nd turn)


### Decode QR Code

Set function that takes `Texture2D` and returns string to `ChatdollCamera.DecodeCode` like below. This example uses [ZXing](https://github.com/micjahn/ZXing.Net/releases) and is expected to be put in `Awake` in your main application.

```csharp
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

ChatdollCamera.DecodeCode = (texture) =>
{
    var luminanceSource = new Color32LuminanceSource(texture.GetPixels32(), texture.width, texture.height);
    var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(luminanceSource)));
    return result != null ? result.Text : string.Empty;
};
```

# Deep dive

## Processing request on remote server

You can host skills as a REST API server on any platforms you like and use it from ChatdollKit so that you can update skills without updating client application.

### Setup server

We provide server side SDK for Python.

https://github.com/uezo/chatdollkit-server-python/blob/main/README.md

Install it from PyPI and start sample application.

```bash
$ pip install chatdollkit
```

### Setup client (Unity)

Set `true` to `Use Remote Server` and set the url of the server to `Base Url` on the inspector of `DialogController`. Make sure that the server is already running then press Play button to run the application.

## Morphological Analysis (Japanese only)

Morphological analysis helps you to understand the intent and to extract entities.
ChatdollKit itself doesn't have a function of morphological analysis but `Request` has `List<WordNode> Words` property to set the result of morphological analysis by MeCab.

```csharp
public class WordNode
{
    public string Word { get; set; }
    public string Part { get; set; }
    public string PartDetail1 { get; set; }
    public string PartDetail2 { get; set; }
    public string PartDetail3 { get; set; }
    public string StemType { get; set; }
    public string StemForm { get; set; }
    public string OriginalForm { get; set; }
    public string Kana { get; set; }
    public string Pronunciation { get; set; }
}
```

Note that the supported platform of MeCab is limited so we recommend you to host skill as REST API on the server that MeCab supports.


# Support

Feel free to post issue to this repository if you have any questions, suggestions or any kinds of opinions.

- [Issues](https://github.com/uezo/ChatdollKit/issues)
- [@uezochan](https://twitter.com/uezochan) (Twitter)
