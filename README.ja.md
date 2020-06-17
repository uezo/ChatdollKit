# ChatdollKit
ChatdollKitは、お好みの3Dモデルを使って音声対話可能なチャットボットを作るためのフレームワークです。

[🇬🇧English version is here](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)

<!-- 
# Quick start guide

Watch this 2 minutes video to learn how ChatdollKit works and the way to use quickly. -->

<img src="https://uezo.blob.core.windows.net/github/chatdoll/chatdollkit_architecture.png" width="640">


# 🚀 クイックスタート

1. 📦パッケージのインポート
    - [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347) と [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/) のインポート
    - [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) のインポート

1. 🐟リソースの準備
    - 3Dモデルをインポートしてシーンに追加
    - 音声ファイルをリソースディレクトリに、アニメーションクリップをアニメーションディレクトリに配置

1. 🍣セットアップ
    - インスペクターのコンテキストメニューから`Setup ModelController`と`Setup Animator`を実行
    - まばたき用のシェイプキーの名前を設定


# 📦 パッケージのインポート

最新版の [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) をダウンロードして、任意のUnityプロジェクトにインポートしてください。また、以下の依存ライブラリもインポートが必要です。

- [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)
- [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)


# 🐟 リソースの準備

## 3Dモデル

お好みの3Dモデルをシーンに配置してください。シェーダーやダイナミックボーンなど必要に応じてセットアップしておいてください。なおこの手順で使っているモデルはシグネットちゃんです。とてもかわいいですね。 https://booth.pm/ja/items/1870320

## Voices

`/Resources/Voices`ディレクトリを作成し、モデルにしゃべらせたい音声ファイルを配置してください。とりあえずHelloWorldを動かしたい場合は、[ここ](https://soundeffect-lab.info/sound/voice/line-girl1.html)から以下3つの音声をダウンロードするとよいでしょう。

- こんにちは: `line-girl1-konnichiha1.mp3`
- 呼びました？: `line-girl1-yobimashita1.mp3`
- はいは〜い: `line-girl1-haihaai1.mp3`

<img src="https://uezo.blob.core.windows.net/github/chatdoll/03_2.png" width="640">


## Animations

`/Animations`ディレクトリを作成し、アニメーションクリップを配置してください。
なおこの手順では[Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406)というモーション集を利用しています。大変使い勝手が良いので気に入ったら有償版の購入をオススメします。

# 🍣 セットアップ

## ChatdollKitの追加

`ChatdollKit/ChatdollKit/Scripts/chatdoll.cs`を3Dモデルに追加してください。以下のコンポーネントも自動的に追加されます。

- `ModelController` 3Dモデルのアニメーション、発話、表情を制御。使い方は[ModelControllerの使い方](https://github.com/uezo/ChatdollKit/blob/master/ModelController.ja.md)を参照

## ModelControllerの設定

インスペクターのコンテキストメニューから`Setup ModelController`を選択すると、LipSync等が自動的に設定されます。その後、まばたきをするために目を閉じる表現のシェイプキーの名前を`Blink Blend Shape Name`に設定しましょう。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/mceditor.png" width="640">

手動で設定したい場合は [Appendix1. ModelControllerの手動設定](#Appendix%201.%20ModelControllerの手動設定) を参照してください。

## Animatorの設定

インスペクターのコンテキストメニューから`Setup Animator`を選択するとフォルダ選択ダイアログが表示されるので、アニメーションクリップが配置されたフォルダを選択してください。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/setupanimator01.png" width="640">

サブフォルダが含まれる場合には、サブフォルダと同じ名前のレイヤーがAnimatorControllerに作成され、そのレイヤーにサブフォルダ内のアニメーションクリップが配置されます。（下図のCase1）

<img src="https://uezo.blob.core.windows.net/github/chatdoll/setupanimator02.png" width="640">

手動で設定したい場合は [Appendix2. Setup Animator manually](#Appendix%202.%20Animatorの手動設定)


## 動作確認

UnityのPlayボタンを押します。3Dモデルがまばたきをしながらアイドル時のアニメーションを行っていれば正しく設定できています。（音声周り以外）

<img src="https://uezo.blob.core.windows.net/github/chatdoll/07_2.png" width="640">

以上で基本的な設定は完了です。3Dモデルを動かしたり喋らせたりする手順は、以下のHello worldの exampleを参考にしてください。


# Hello world example

"Hello world"のexampleを動かすための手順は以下の通りです。

1. `Examples/HelloWorld/Scripts`の中にある`HelloWorld.cs`と`IntentExtractor.cs`を3DモデルのGameObjectに追加

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/08_2.png" width="640">

1. `Examples`の中にある`SimpleMessageWindow`プレファブをシーンに追加したら、`Font`を設定

1. `Hello World`コンポーネントの`Message Window`に、今シーンに追加した`SimpleMessageWindow`を設定

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/09_2.png" width="640">

1. `Request Provider`の`Dummy Text`に、音声認識されたことにするダミーの文言を入力。ここで入力した内容がChatdollに送られます

以上で設定は完了です。ゲームを開始してインスペクター上の`Start Chat`ボタンをクリックしましょう。`呼びました？`と尋ねられると、ユーザーからの要求文言としてメッセージボックスにダミー入力テキストが表示され、`はいは〜い`と要求が受託された旨の応答があります。最後に（処理結果として）`こんにちは`と挨拶してくれます。


# Hello worldの改造方法

## IntentExtractor

`IntentExtractor`は`HelloWorld`追加時に自動的に追加されます。ここには、ユーザーが何を要求しているか（＝インテント）を抽出するロジックを実装します。初期状態では常に「hello」というインテントが抽出され、リクエストに設定されるようになっています。

```Csharp
request.Intent = "hello";
```

以下のように条件式で設定するように書き換えたり、LUISなどのNLUサービスを利用した結果を設定するように改造するとよいでしょう。

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

これに加えて、抽出されたインテント＝処理要求を受諾した旨の応答内容をカスタマイズすることもできます。例では`response.Payloads`にアニメーションや発話内容を設定した`animatedVoiceRequest`を設定し、これを`ShowResponseAsync()`で3Dモデルに演じさせていますが、必ずしも`ModelController`を通じてモデルを操作する必要はありません。なお処理要求受諾後の応答は、後続の処理で時間がかかる場合の体感上の時間稼ぎにもなります。

```Csharp
var animatedVoiceRequest = new AnimatedVoiceRequest();
animatedVoiceRequest.AddVoice("line-girl1-haihaai1", preGap: 1.0f, postGap: 2.0f);
animatedVoiceRequest.AddAnimation("Default");
```

## DialogProcessor

HelloWorldの例では`hello`という`DialogProcessor`＝対話処理部品が1つだけ追加されていますが、スタティックにこんにちはの発生を応答するだけですので例では何も処理を実装していません。
天気予報やしりとり、雑談など各種機能をそれぞれ`IDialogProcessor`を実装したオブジェクトとして作成することでChatdollにさまざまな対話処理部品を追加することができます。対話処理部品はチャットボットやスマートスピーカーではスキルと呼ばれるものに相当します。
なお`DialogProcessor`の`TopicName`に設定した値と同じものが`IntentExtractor`で`request.Intent`にセットされると、それを契機として対応する`DialogProcessor`が呼び出され、処理を開始します。この`TopicName`の値はコンテキストにも`Context.Topic.Name`として保持されるため、同一のトピックで継続して会話を続けることもできます。

## RequestProvider

`RequestProvider`はユーザーからの要求内容をモデルに伝えるための部品で、音声認識やカメラで撮影した画像などをリクエスト情報として引き渡すように実装します。なお`RequestProvider`はHelloWorldのサンプルを動かすためのモック用の部品です。実用性のあるバーチャルアシスタントを開発するには、`AzureVoiceRequestProvider`や`GoogleVoiceRequestProvider`を使用するか、`VoiceRequestProviderBase`を継承してお好みのSpeech-to-Textサービスを利用したRequestProviderを作成してください。

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

ChatdollKitを利用した複雑で実用的なバーチャルアシスタントの開発方法については、現在コンテンツを準備中です。

ChatdollKitの基本的な価値として、Unity初心者であっても簡単なコーディング（モーションやボイスの名前を指定するなど）だけで3Dモデルを制御することができるようにしていたり、チャットボット初心者であっても対話制御の作り込みをすることなく自然言語処理や機能開発に集中できるようにしています。より豊かな表現をするためには各シチュエーションで呼び出される3Dモデルの制御処理をUnityの機能を使いこなしてリッチにすることができますので、自身のスキル習得に応じて`ModelController`を卒業していただければと考えています。


# Appendix 1. ModelControllerの手動設定

モデルのオブジェクトまたは適当な場所に追加した空のオブジェクトに、インスペクターから以下2つのコンポーネントを追加してください。

- OVR Lip Sync Context
- OVR Lip Sync Context Morph Target

また、その設定内容は以下の通りです。

- `OVR Lip Sync Context`の`Audio Loopback`のチェックをオンにする
- 表情関連のシェイプキーを`OVR Lip Sync Context Morph Target`の`Skinned Mesh Renderer`に設定
- 単語の読み上げ時の口の形にそれぞれ適切なものを指定

<img src="https://uezo.blob.core.windows.net/github/chatdoll/02_2.png" width="640">

その後、ModelControllerのインスペクターを表示して以下の通り設定します。

- LipSyncを設定したオブジェクトを`ModelController`の`Audio Source`に設定
- 表情関連のシェイプキーの設定されたオブジェクトを`Skinned Mesh Renderer`に設定
- まばたきをするため、目を閉じる表現のためのシェイプキーの名前を`Blink Blend Shape Name`に設定

<img src="https://uezo.blob.core.windows.net/github/chatdoll/06_2.png" width="640">


# Appendix 2. Animatorの手動設定

Animator Controllerを作成してBase Layerに`Default`というステートを追加し、各モーションを配置してください。そして`Default`ステートにも任意のアイドル状態のアニメーションを追加しましょう。Base Layer以外にも、たとえば上半身のマスクを適用したレイヤーを追加し、モーションを追加しておくこともできます。その場合は必ずすべてのレイヤーに`Default`ステートを追加し、モーションには何も設定しない（＝`None`のまま）ようにしてください。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/04.png" width="640">

設定が完了したら、3Dモデルの`Animator`コンポーネントの`Controller`に設定しましょう。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/05_2.png" width="640">
