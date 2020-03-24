# ChatdollKit
ChatdollKitは、お好みの3Dモデルを使って音声対話可能なチャットボットを作るためのフレームワークです。

<!-- 
# Quick start guide

Watch this 2 minutes video to learn how ChatdollKit works and the way to use quickly. -->


# インストール

このリポジトリをクローンまたはダウンロードして、`ChatdollKit`ディレクトリを任意のUnityプロジェクトに追加してください。また依存ライブラリは以下の通りですので、事前にプロジェクトへのインポートが必要です。

- [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)
- [Oculus Lipsync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)
- 音声認識機能を利用するためには[Azure Speech SDK for Unity](https://docs.microsoft.com/ja-jp/azure/cognitive-services/speech-service/speech-sdk?tabs=windows) または [Google Cloud Speech Recognition](https://assetstore.unity.com/packages/add-ons/machinelearning/google-cloud-speech-recognition-vr-ar-mobile-desktop-pro-72625?locale=ja-JP) をインストールしたのち、`ChatdollKit.Extension`の中の`AzureVoiceRequestProvider` か `GoogleCloudSpeechRequestProvider`を追加してください。Azure SDKはMacOSをサポートしていないので注意が必要です。`HelloWorldExample`を試すだけであれば音声認識ライブラリの導入は一旦スキップして大丈夫です。
- Gateboxアプリを開発する場合は[GateboxSDK](https://developer.gatebox.biz/document) をプロジェクトに追加してください。SDKを入手するにはGatebox Developer Programへのサインアップが必要です。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/01.png" width="640">


# 関連リソースの準備

## 3Dモデル

お好みの3Dモデルをシーンに配置してください。シェーダーやダイナミックボーンなど必要に応じてセットアップしておいてください。なおこの手順で使っているモデルはシグネットちゃんです。とてもかわいいですね。 https://booth.pm/ja/items/1870320

モデル自体の設定が完了したら、リップシンクの設定もしておきます。モデルのオブジェクトまたは適当な場所に追加したオブジェクトに、インスペクターから以下3つのコンポーネントを追加してください。

- OVR Lip Sync Context Morph Target
- OVR Lip Sync Mic Input
- OVR Lip Sync Context

表情関連のシェイプキーを`OVR Lip Sync Context Morph Target`の`Skinned Mesh Renderer`に設定し、`OVR Lip Sync Context`の`Audio Loopback`のチェックをオンにすれば設定完了です。これによって3Dモデルにおしゃべりをさせると発話内容に応じて口が動くようになります。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/02.png" width="640">


## Voices

`/Resources/Voices`ディレクトリを作成し、モデルにしゃべらせたい音声ファイルを配置してください。とりあえずHelloWorldを動かしたい場合は、[ここ](https://soundeffect-lab.info/sound/voice/line-girl1.html)から以下3つの音声をダウンロードするとよいでしょう。

- こんにちは: `line-girl1-konnichiha1.mp3`
- 呼びました？: `line-girl1-yobimashita1.mp3`
- はいは〜い: `line-girl1-haihaai1.mp3`

<img src="https://uezo.blob.core.windows.net/github/chatdoll/03.png" width="640">


## Animations

Animator Controllerを作成してBase Layerに`Default`というステートを追加し、各モーションを配置してください。そして`Default`ステートにも任意のアイドル状態のアニメーションを追加しましょう。Base Layer以外にも、たとえば上半身のマスクを適用したレイヤーを追加し、モーションを追加しておくこともできます。その場合は必ずすべてのレイヤーに`Default`ステートを追加し、モーションには何も設定しない（＝`None`のまま）ようにしてください。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/04.png" width="640">

設定が完了したら、3Dモデルの`Animator`コンポーネントの`Controller`に設定しましょう。なおこの手順では[Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406)というモーション集を利用しています。大変使い勝手が良いので気に入ったら有償版の購入をオススメします。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/05.png" width="640">


# 基本的な設定

## ChatdollKitの追加

`Chatdoll/chatdoll.cs`を3Dモデルに追加してください。以下2つのコンポーネントも自動的に追加されます。

- `ModelController` 3Dモデルのアニメーション、発話、表情を制御
- `MicEnabler` 音声認識のためのマイクの利用権限を取得

## ModelControllerの設定

LipSyncを設定したオブジェクトにはAudio Sourceが自動的に追加されているので、これを`ModelController`の`Audio Source`に設定します。また、表情関連のシェイプキーの設定されたオブジェクトを`Skinned Mesh Renderer`に設定します。最後に、まばたきをするため、目を閉じる表現のためのシェイプキーの名前を`Blink Blend Shape Name`に設定しましょう。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/06.png" width="640">

以上で設定は完了です。3Dモデルを動かしたり喋らせたりする手順は、以下の"Hello world example"を参考にしてください。


# Hello world example

"Hello world example"を動かすための手順は以下の通りです。

1. `Examples/HelloWorld/HelloWorldExample`を3DモデルのGameObjectに追加

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/07.png" width="640">

1. `Examples`の中にある`SimpleMessageWindow`プレファブをシーンに追加したら、`Font`を設定。デフォルトではArialが適用されています

1. `Hello World Example`コンポーネントの`Message Window`に、今シーンに追加した`SimpleMessageWindow`を設定

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/08.png" width="640">

1. `Dummy Request Provider`の`Dummy Text`に、音声認識されたことにするダミーの文言を入力。ここで入力した内容がChatdollに送られます

    <img src="https://uezo.blob.core.windows.net/github/chatdoll/09.png" width="640">


以上で設定は完了です。ゲームを開始してインスペクター上の`Start Chat`ボタンをクリックしましょう。`呼びました？`と尋ねられると、ユーザーからの要求文言としてメッセージボックスにダミー入力テキストが表示され、`はいは〜い`と要求が受託された旨の応答があります。最後に（処理結果として）`こんにちは`と挨拶してくれます。


# Hello worldの改造方法

## IntentExtractor

`IntentExtractor`は`HelloWorldExample`追加時に自動的に追加されます。ここには、ユーザーが何を要求しているか（＝インテント）を抽出するロジックを実装します。初期状態では常に「hello」というインテントが抽出され、リクエストに設定されるようになっています。

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

`RequestProvider`はユーザーからの要求内容をモデルに伝えるための部品で、音声認識やカメラで撮影した画像などをリクエスト情報として引き渡すように実装します。なお`DummyRequestProvider`はHelloWorldのサンプルを動かすためのモック用の部品です。実用性のあるバーチャルアシスタントを開発するには、`AzureVoiceRequestProvider`や`GoogleCloudSpeechRequestProvider`、または`IRequestProvider`を実装した独自の部品を利用してください。実装方法は`AzureVoiceRequestProvider`などを参考にしていただけると幸いです。


# Deep Dive

ChatdollKitを利用した複雑で実用的なバーチャルアシスタントの開発方法については、現在コンテンツを準備中です。

ChatdollKitの基本的な価値として、Unity初心者であっても簡単なコーディング（モーションやボイスの名前を指定するなど）だけで3Dモデルを制御することができるようにしていたり、チャットボット初心者であっても対話制御の作り込みをすることなく自然言語処理や機能開発に集中できるようにしています。より豊かな表現をするためには各シチュエーションで呼び出される3Dモデルの制御処理をUnityの機能を使いこなしてリッチにすることができますので、自身のスキル習得に応じて`ModelController`を卒業していただければと考えています。
