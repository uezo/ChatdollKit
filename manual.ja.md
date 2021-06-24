# ChatdollKit マニュアル

version 0.3.0 | June 21, 2021 | &copy;2020 uezo


- [セットアップ](#セットアップ)
    - [パッケージのインポート](#パッケージのインポート)
    - [リソースの準備](#リソースの準備)
    - [ChatdollKitのセットアップ](#chatdollkitのセットアップ)
        - [カスタムアプリケーションの作成](#カスタムアプリケーションの作成)
        - [アプリケーションの設定](#アプリケーションの設定)
        - [ModelControllerの設定](#modelcontrollerの設定)
        - [Animatorの設定](#animatorの設定)
    - [動作確認](#動作確認)

- [モデル動作の制御](#モデル動作の制御)
    - [発話](#発話)
        - [共通](#共通)
        - [ローカルファイルの発声](#ローカルファイルの発声)
        - [Web](#web)
        - [テキスト読み上げサービスとの連携](#テキスト読み上げサービスとの連携)
    - [アニメーション](#アニメーション)
    - [表情](#表情)
        - [ModelControllerのインスペクターを利用](#modelcontrollerのインスペクターを利用)
        - [スクリプトで登録](#スクリプトで登録)
    - [アニメーション・発話・表情の組み合わせ](#アニメーション・発話・表情の組み合わせ)
    - [アイドル時の振る舞い](#アイドル時の振る舞い)
    - [まばたき](#まばたき)

- [対話の制御](#対話の制御)
    - [カスタムスキル追加の基本](#カスタムスキル追加の基本)
    - [リクエストとステート](#リクエストとステート)
        - [Request](#request)
        - [State](#state)
    - [話題の継続](#話題の継続)
    - [事前処理](#事前処理)
    - [対話処理のルーティング](#対話処理のルーティング)
        - [インテントの抽出](#インテントの抽出)
        - [話題の優先度の設定](#話題の優先度の設定)
        - [アドホックな話題の割り込み](#アドホックな話題の割り込み)
        - [エンティティの抽出](#エンティティの抽出)

- [入出力の制御](#入出力の制御)
    - [音声による処理要求](#音声による処理要求)
        - [Cancellation Settings](#cancellation-settings)
        - [Test and Debug](#test-and-debug)
        - [Voice Recorder Settings](#voice-recorder-settings)
        - [UI](#ui)
    - [カメラによる処理要求](#カメラによる処理要求)
        - [WakeWordにリクエストタイプを指定](#wakewordにリクエストタイプを指定)
        - [Skillの中でリクエストタイプを指定](#skillの中でリクエストタイプを指定)
    - [QRコードによる処理要求](#qrコードによる処理要求)
        - [WakeWordにリクエストタイプを指定](#wakewordにリクエストタイプを指定-1)
        - [Skillの中でリクエストタイプを指定](#skillの中でリクエストタイプを指定-1)
        - [QRコードのデコード処理](#qrコードのデコード処理)
    - [ウェイクワード](#ウェイクワード)
        - [WakeWord Settings](#wakeword-settings)
        - [Test and Debug](#test-and-debug-1)
        - [Voice Recorder Settings](#voice-recorder-settings-1)
    - [プロンプト](#プロンプト)
    - [高度な利用方法](#高度な利用方法)
        - [形態素解析](#形態素解析)
        - [対話処理のサーバーサイド実装](#対話処理のサーバーサイド実装)

- [おわりに](#おわりに)


# セットアップ

Echoアプリケーションを動かすまでの基本的なセットアップ方法は[README](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)の通りですが、ここではカスタムアプリケーションを作成する前提で説明します。スクリーンショットなどは省略していますので、必要に応じて[README](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)も合わせて参照ください。

## パッケージのインポート

- ChatdollKit
- [JSON.NET for Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)
- [OVR LipSync](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)

## リソースの準備

- シーンへの3Dモデルの配置。Shaderの設定や揺れものの設定なども行う
- アニメーションクリップを入手して`Assets/Animations`フォルダへ配置。オススメは[Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406)
- [Azure Speech Services](https://azure.microsoft.com/ja-jp/services/cognitive-services/speech-services/) または [Google Cloud Speech API](https://cloud.google.com/speech-to-text/) のAPIキーの取得

## ChatdollKitのセットアップ

ChatdollKitを3Dモデルに適用するには、以下の通りカスタムアプリケーションを作成・アタッチして、関連するコンポーネントを設定します。

### カスタムアプリケーションの作成

`Assets/Scripts`など任意の場所に以下の内容の`MyChatdollApp.cs`を作成します。名前は何でも構いません。作成したら3Dモデルにアタッチしてください。

```csharp
using UnityEngine;
// using ChatdollKit.Extension.Google;
using ChatdollKit.Extension.Azure;

namespace MyChatdollApp
{
    // public class MyApp : GoogleApplication
    public class MyApp : AzureApplication
    {

    }
}
```

### アプリケーションの設定

`MyApp`のインスペクターで以下の通り設定します。

- Wake Word: こんにちは
- Cancel Word: おしまい
- Prompt Voice: どうしたの？
- Prompt Voice Type: TTS
- Api Key: （Azure Speech ServicesまたはGoogle Cloud SpeechのAPIキー）
- Region: （リージョン。Azureの場合のみ設定）
- Language: ja-JP

### ModelControllerの設定

インスペクターのコンテキストメニューから`Setup ModelController`を実行します。もし`Blink Blend Shape Name`が空欄のままなときは、3Dモデルの目を閉じるためのシェイプキーの名前を入力してください。まばたきに使用します。

### Animatorの設定

インスペクターのコンテキストメニューから`Setup Animator`を実行します。読み込むアニメーションクリップの格納先を尋ねられますので、アニメーションクリップの配置先を選択してください。

## 動作確認

動作確認用に`ChatdollKit/Examples/Dialogs`から`EchoSkill`を3Dモデルにアタッチして、Unityエディタの実行ボタンを押下してください。以下の通り対話を進行できるか確認してみましょう。

- ユーザー「こんにちは」
- Chatdoll「どうしたの？」
- ユーザー「これはテストです」
- Chatdoll「これはテストです」


# モデル動作の制御

3Dモデルをしゃべらせたり、動かしたり、表情を変えたりするには`ModelController`の`AnimatedSay`メソッドを使用します。このメソッドへの指示内容は`AnimatedVoiceRequest`オブジェクトに設定し、以下のように使用します。

```csharp
// 指示内容を作成
var animatedVoiceRequest = new AnimatedVoiceRequest();
animatedVoiceRequest.AddVoiceTTS("おはよー");
animatedVoiceRequest.AddAnimation("stand");
animatedVoiceRequest.AddFace("Smile");

// 実行
modelController.AnimatedSay(animatedVoiceRequest, CancellationToken.None);
```

なお`ModelController`はセットアップ時に作成したアプリのベースクラスの変数`modelController`に設定されていますので、アプリ内で任意に作成したメソッド等で動作を確認することができます。

## 発話

発話するには`AnimatedVoiceRequest`に音声を追加します。そのためのメソッドとしてローカルに保存されたファイルを再生する`AddVoice`、WebからHTTP(S)により取得したデータを再生する`AddVoiceWeb`、そして読み上げサービスを使用する`AddVoiceTTS`の3種類が提供されています。

### 共通

以下の引数は3種類の音声追加メソッドの引数として共通なものです。

- preGap: 発話前の空白期間（秒）を指定します。省略した場合は`0.0f`
- postGap: 発話後の空白期間（秒）を指定します。省略した場合は`0.0f`
- description: モデル処理の履歴に記録する説明を指定します。省略可
- asNewFrame: 新規の同期枠を設定したいときにtrueを指定します。省略した場合は`false`。同期枠については後述します

```csharp
animatedVoiceRequest.AddVoice("Hello", preGap: 0.5f, postGap: 1.0f, description: "テスト用", asNewFrame: true);
```

なお複数の音声を追加した場合は追加順に発話が行われます。以下の例では、「こんにちは」のあとに0.5秒待って「今日はいい天気ですね」と発話されます。

```csharp
animatedVoiceRequest.AddVoiceTTS("こんにちは。", postGap: 0.5f);
animatedVoiceRequest.AddVoiceTTS("今日はいい天気ですね。");
```


### ローカルファイルの発声

`AddVoice`を使用します。第1引数の`name`には音声ファイルを読み込む際に指定した名称を指定します。

```csharp
animatedVoiceRequest.AddVoice("Hello");
```

そのため、音声ファイルの読み込みは`Awake`などで予め行っておきましょう。以下はResourcesフォルダのVoicesフォルダ配下に配置した音声ファイルを一括読み込みする例です。

```csharp
foreach (var audioClip in Resources.LoadAll<AudioClip>("Voices"))
{
    modelController.AddVoice(audioClip.name, audioClip);
}
```

### Web

`AddVoiceWeb`を使用します。第1引数の`url`には音声データを取得するためのURLを指定します。

```csharp
animatedVoiceRequest.AddVoiceWeb("https://~~~~~/hoge.wav");
```

なお読み込みのタイミングは`AnimatedSay`が実行されたタイミングとなるため、初回はダウンロードによる開始遅延が発生します。ダウンロードされたデータはURLをキーにキャッシュされるため、次回以降に遅延は発生しません。また、音声の実行順序により初回の遅延を回避することができますので、詳細は[アニメーション・発話・表情の組み合わせ]()を参照してください。


### テキスト読み上げサービスとの連携

`AddVoiceTTS`を使用します。第1引数の`text`には読み上げてもらいたい文字列を指定します。

```csharp
animatedVoiceRequest.AddVoiceTTS("これは動的に生成した文字列です。");
```

`AddVoiceTTS`を使用するには、`TTSLoader`という音声読み上げサービスとの接続部品を3Dモデルにアタッチしておく必要があります。ChatdollKitでは以下3種類のTTSLoaderを提供していますが、`WebVoiceLoaderBase`を拡張して好みの読み上げサービスを利用することもできます。

- AzureTTSLoader: [Azure Speech Services](https://azure.microsoft.com/ja-jp/services/cognitive-services/speech-services/)
- GoogleTTSLoader: [Google Cloud Speech](https://cloud.google.com/speech-to-text?hl=ja)
- VoiceroidTTSLoader: [Voiceroid Daemon](https://github.com/Nkyoku/voiceroid_daemon)

また読み上げサービスのパラメータをフレーズごとに指定したい場合は`ttsConfig`で指定します。ここで指定できるパラメータは読み上げサービス毎に異なりますので、各種`TTSLoader`の仕様をご確認ください。

```csharp
// パラメータの指定
var ttsConfig = new TTSConfiguration();
ttsConfig.Params.Add("Pitch", 2.0f);
ttsConfig.Params.Add("Speed", 2.0f);
// 音声の追加
animatedVoiceRequest.AddVoiceTTS("この文言は二倍速で高い声でしゃべります。", ttsConfig: ttsConfig);
```

## アニメーション

身振り手振りなどをするには`AnimatedVoiceRequest`にアニメーションを追加します。そのためのメソッドとして`AddAnimation`が提供されています。

引数は以下の通り。

- name: アニメーション名。正確にはAnimator Controllerのステート名
- layerName: アニメーションのレイヤー名。省略した場合はベースレイヤー
- duration: アニメーションの継続時間（秒）
- fadeLength: 現在のポーズからアニメーションの動作を適用しきるまでの時間（秒）。省略した場合はインスペクターの`Animation Fade Length`の値が適用
- weight: アニメーションの適用度合い。最大1.0。省略した場合は`1.0f`
- preGap: アニメーション開始前の空白期間（秒）。省略した場合は`0.0f`
- description: モデル処理の履歴に記録する説明。省略可
- asNewFrame: 新規の同期枠を設定したいときにtrueを指定します。省略した場合は`false`。同期枠については後述します

```csharp
// 開始から1秒後に歩き始め、3秒間歩く
animatedVoiceRequest.AddAnimation("walk", duration: 3.0f, preGap: 1.0f);
// 0.5秒かけて止まる
animatedVoiceRequest.AddAnimation("stand", fadeLength: 0.5f);
```

なおベースレイヤー以外のアニメーションは上記のようにシーケンシャルに実行されるのではなく、ベースレイヤーと並行して実行されます。


```csharp
// 開始から1秒後に歩き始め、3秒間歩く
animatedVoiceRequest.AddAnimation("walk", duration: 3.0f, preGap: 1.0f);
// 0.5秒かけて止まる
animatedVoiceRequest.AddAnimation("stand", fadeLength: 0.5f);
// 上記アニメーション全体は、両手を振りながら実行される
animatedVoiceRequest.AddAnimation("wavinghands", "Upper Body");
```


## 表情

表情を変更するには`AnimatedVoiceRequest`に表情を追加します。そのためのメソッドとして`AddFace`が提供されています。

引数は以下の通り。

- name: 登録した表情の名前を指定します
- duration: 表情の継続時間。省略した場合は次に変更するまで継続します
- description: モデル処理の履歴に記録する説明を指定します。省略可
- asNewFrame: 新規の同期枠を設定したいときにtrueを指定します。省略した場合は`false`。同期枠については後述します

```csharp
animatedVoiceRequest.AddFace("Smile", 2.0f);
animatedVoiceRequest.AddFace("Neutral");
```

なお表情を登録するには以下の2通りの方法があります。表情を登録しない場合はすべてのシェイプキーを0としたものがデフォルトの表情として適用されます。

### ModelControllerのインスペクターを利用

表情のシェイプキーをお好みの表情にセットしたら、ModelControllerのインスペクターの最下部にある`Capture`というボタンを探します。その左側のテキストボックスに表情名（Smileなど）を入力したら、`Capture`ボタンを押すことで、表情名とシェイプキーの組み合わせを登録することができます。

### スクリプトで登録

`ModelController.AddFace`で登録することができます。引数は以下の通り。シェイプキーをコードベースで登録するのは大変なので基本的には先に挙げたインスペクターでの登録をお勧めします。

- name: 登録する表情の名前を指定します
- weights: 各シェイプキー名とそのウェイトの辞書。`Dictionary<string, float>`。指定しないシェイプキーは操作しません
- asDefault: この表情をデフォルトの表情として登録するかどうか


## アニメーション・発話・表情の組み合わせ

実際の対話においては、身振り手振りを交え、ときに表情を変えながら発話してもらいたいものです。これらの組み合わせは、単純に`AnimatedVoiceRequest`にそれぞれ追加することで実現できます。

```csharp
// 左手を腰に当てながら笑顔でお昼がお蕎麦としゃべる
animatedVoiceRequest.AddVoiceTTS("こんにちは。今日のお昼ごはんはお蕎麦です。");
animatedVoiceRequest.AddAnimation("left_hand_on_waist");
animatedVoiceRequest.AddFace("Smile");
```

特定の発話と動作・表情を同期させたいときには、`asNewFrame`に`true`を指定することで動作・表情の開始を発話に合わせることができます。

```csharp
// 笑顔で手を振りながら挨拶
animatedVoiceRequest.AddVoiceTTS("みんなー！こんにちはー！", postGap: 2.0f);
animatedVoiceRequest.AddAnimation("wavehands");
animatedVoiceRequest.AddFace("Smile");
// 話し始めに手をふるのをやめると同時に表情をニュートラルに戻してお礼を言う
animatedVoiceRequest.AddVoiceTTS("今日は会いに来てくれてありがとう！", asNewFrame: true);
animatedVoiceRequest.AddAnimation("stand");
animatedVoiceRequest.AddFace("Neutral");
```


## アイドル時の振る舞い

対話処理を行っていない待機状態のアニメーションとしては、デフォルトではBase Layerの`Default`ステートが実行されます。複数かつ詳細なアニメーションを指定するには、`ModelController`の`AddIdleAnimation`メソッドを使用します。

引数は以下の通り。

- animationName: アニメーション名。正確にはAnimator Controllerのステート名
- faceName: 表情名。省略可
- duration: 継続時間（秒）。省略した場合はインスペクターの`Idle Animation Default Duration`の値が適用
- fadeLength: 前のポーズからの切り替わりにかける時間（秒）。省略した場合はインスペクターの`Animation Fade Length`の値が適用
- preGap: アニメーション開始前の空白期間（秒）。省略した場合は`0.0f`
- disableBlink: この間にまばたきを停止するかどうか。省略した場合は`false`＝まばたきを継続。目を閉じた表情を指定した場合は`true`にするとよい
- weight: 複数のアニメーションを`AddIdleAnimation`した場合の登場割合。省略した場合は`1`

たとえば以下の通り4つのアイドルアニメーションを登録した場合、`Idle Animation Default Duration`の秒数おきにこれらのうちどれを実行するかランダムに決定されます。この際、どれが採用されるかの確率は`weight`の値に比例します。

```csharp
modelController.AddIdleAnimation("idle01", weight: 20);
modelController.AddIdleAnimation("idle02", "Doya", weight: 20);
modelController.AddIdleAnimation("idle03", "Smile", disableBlink: true, weight: 5);
modelController.AddIdleAnimation("idle04");
```

なおこれらのパラメータで表現しきれない複雑なものを追加したい場合、`AnimatedVoiceRequest`を引数にとるオーバーロードを利用してください。

```csharp
// 複数のアニメーションを、同期枠を設けて表情を変えながら実行
var idle05 = new AnimatedVoiceRequest();
idle05.AddAnimation("wave_hands", "Others", duration: 3.0f);
idle05.AddFace("Smile");
idle05.AddAnimation("AGIA_Idle_calm_02_hands_on_front", duration: 20.0f, asNewFrame: true);
idle05.AddFace("Neutral");
modelController.AddIdleAnimation(idle05, weight: 2);

// 追加
modelController.AddIdleAnimation(animatedVoiceRequest);
```

## まばたき

まばたきは自動で開始・停止が制御されますので、特別な操作を行う必要はありません。まばたきの間隔などは`ModelController`のインスペクター上で設定することができます。

- Blink Blend Shape Name: 目を閉じるためのシェイプキーの名称。ModelControllerのコンテキストメニュー「Setup ModelController」で自動設定されない場合は手動で設定します
- Min Blink Interval To Close: まばたき間隔の最小値（秒）
- Max Blink Interval To Close: まばたき間隔の最大値（秒）
- Min Blink Interval To Open: 目を閉じている時間の最小値（秒）
- Max Blink Interval To Open: 目を閉じている時間の最大値（秒）
- Blink Transition To Close: 目を閉じ始めてから閉じ切るまでの時間（秒）
- Blink Transition To Open: 目を開け始めてから開け切るまでの時間（秒）


# 対話の制御

話題に応じた対話処理を、ChatdollKitでは`Skill`と呼んでおり、作成するには`ISkill`インターフェイスを実装します。また、基本的な処理を実装済みの`SkillBase`を継承することでより簡単な手順で作成することもできます。

## カスタムスキル追加の基本

以下はおうむ返しのSkillの実装です。最小限の実装としては、この例のように`ProcessAsync`をオーバーライドして各種処理の実行やその結果に応じたレスポンスメッセージの組み立てを行います。

```csharp
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.Dialogs
{
    public class EchoSkill : SkillBase
    {
        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            // レスポンスの生成
            var response = new Response(request.Id);

            // ユーザーの発話内容の読み上げをレスポンスにセット
            response.AddVoiceTTS($"{request.Text}");

            return response;
        }
    }
}
```

なお`response`には`AnimatedVoiceRequest`の各種Addメソッド群が用意されいるため、上記例は`response.AnimatedVoiceRequest.AddVoiceTTS`と同義です。`response`に設定した発話やアニメーションはChatdollKitにより自動的に実行されます。

## リクエストとステート

`ProcessAsync`の引数として渡される`request`と`state`には、それぞれ今回のターンの要求情報とこれまでの文脈に関する情報が格納されています。これはWebアプリケーションにおけるリクエストとセッションとの関係と同じです。

### Request

リクエストの都度生成・破棄される情報。このうち`User`については永続化される情報です。

- Entities: リクエストに含まれる情報を格納。「東京の天気は？」の「東京」など
- Id: リクエストを一意に特定するID
- Intent: 発話の意図。詳細は対話処理のルーティングを参照
- IntentPriority: 発話の意図の優先度。詳細は対話処理のルーティングを参照
- IsAdhoc: アドホックな割り込み要求かどうか。詳細は対話処理のルーティングを参照
- IsCanceled: 既にキャンセルされた要求かどうか
- Payloads: 発話された文言以外のデータ。写真など
- Text: 発話された文言
- Timestamp: リクエストが生成された日時
- Type: 要求の形式。`Voice`、`Camera`、`QRCode`
- User: リクエスト元のユーザー情報
    - Data: 永続化対象のユーザー情報。たとえば生年月日や趣味など
    - DeviceId: デバイスを一意に指定するID
    - Id: ユーザーを一意に特定するID
    - Name: ユーザーの名前
    - Nickname: ユーザーのニックネーム
- Words: 形態素解析結果。詳細は高度な利用方法参照

なおユーザー情報を更新すると、その内容は自動的に保存されます。ただし対話処理が以上終了した場合は保存されません。

```csharp
// 対話処理が正常終了したとき、以下の情報が保存されます
request.User.Nickname = "うえぞうちゃん";
request.User.Data["FavoriteFood"] = "そば";
```

### State

複数のリクエストを跨いで一定時間維持される情報。このうち`Topic`については話題の終了時に破棄されます。

- Data: 文脈データ
- Id: ステートを一意に特定するID
- IsNew: 対話処理がちょうど今始まったところかどうか
- Timestamp: ステートが最後に更新された日時
- Topic: 対話中の話題
    - ContinueTopic: 次のリクエストにおいても話題を継続するかどうか
    - IsNew: この話題が今始まったところかどうか
    - Name: 話題の名称
    - Previous: 前回のリクエスト時の話題
    - Priority: 話題の優先度。インテントの優先度がセットされる。また、話題を継続しているとき、インテントの優先度がこの値よりも大きければ話題が切り替わる
    - RequiredRequestType: 次回リクエストの形式を指定。詳細はカメラによる要求・QRコードによる要求を参照
    - Status: 話題のステータス。対話シナリオの進行管理のために自由に利用
- UserId: ユーザーのID。ステートの取得・永続化キー

なお`Topic.Status`の利用例は以下の通り。

```csharp
// 何らかの処理。この処理結果に応じてStatusを更新
　：
// ステータスに応じた応答の組み立て
if (state.Topic.Status == "Success")
{
    response.AddVoiceTTS($"今日の{state.Data["place"]}の天気は{state.Data["weather"]}だよ。");
}
else if (state.Topic.Status == "NoPlace")
{
    response.AddVoiceTTS("どこの天気が知りたいの？");
}
```

## 話題の継続

リクエスト・レスポンスの1ターンで終わらせず文脈を維持して話題を継続したい場合、`state.Topic.IsFinished`を`false`にします。

```csharp
if (state.Topic.IsFirstTurn)
{
    // 初回ターンでは翻訳すべき文言の問いかけ
    response.AddVoiceTTS("翻訳ですね？何を翻訳しますか？");
    // 次回の発話も翻訳として処理されるように話題終了フラグを下げる
    state.Topic.IsFinished = false;
}
else
{
    var translatedText = Translate(request.Text);
    response.AddVoiceTTS(translatedText);
}
```

## 事前処理

`PreProcessAsync`を実装することで、`ProcessAsync`の事前処理として処理されます。引数・戻り値は`ProcessAsync`と全く同じですが、特筆すべき点として、`PreProcessAsync`のレスポンスの発話・アニメーションが`ProcessAsync`と並列的に処理されます。

したがって`ProcessAsync`で実行する処理に時間がかかる場合、`PreProcessAsync`で数秒間の発話内容を含むレスポンスを返すことによって、本体処理の待ち時間を軽減またはなくすことができます。

例：天気予報
- PreProcessAsyncで「千葉県の天気が知りたいの？わかった。調べるからちょっと待ってて。」と応答
- 上記応答の実行
- 応答のバックグラウンドで並行してProcessAsyncで千葉県のジオコーディング、ジオコーディング結果を利用して天気予報の検索を実行
- 天気予報結果の応答の実行


## 対話処理のルーティング

ユーザーが何の話題について話そうとしているかを理解し適切な`Skill`を呼び出す機能を、ChatdollKitでは`SkillRouter`と呼んでおり、作成するには`ISkillRouter`インターフェイスを実装します。また、基本的な処理を実装済みの`SkillRouterBase`を継承することでより簡単な手順で作成することもできます。その場合に実装すべきメソッドは`ExtractIntentAsync`のみです。

### インテントの抽出

ユーザーの発話の意図をインテントと呼びます。ChatdollKitでは抽出されたインテントと対話の文脈を考慮して、現在の話題を継続すべきか新たな話題に切り替えるべきかを判断しています。

以下はユーザーの発話内容に「天気」が含まれているとき`weather`を、「翻訳」が含まれているとき`translate`を、それ以外の場合には`chat`をインテントとして抽出する例です。抽出したインテントは`request.Intent`に設定します。

```csharp
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.MultiDialog
{
    public class Router : SkillRouterBase
    {
        public override async Task ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            if (request.Text.Contains("天気"))
            {
                request.Intent = "weather";
            }
            else if (request.Text.Contains("翻訳"))
            {
                request.Intent = "translate";
            }
            else
            {
                request.Intent = "chat";
            }
        }
    }
}
```

実際にプロダクトに仕上げていくためには、単に含んでいるかどうかの判定ではなくLUIS等のインテント抽出サービスや形態素解析結果を利用してユーザーが真にその話題について話したいか判定すると良いでしょう。

### 話題の優先度の設定

例えば「おしまい」と言うまで翻訳し続ける対話の最中に天気が知りたくなったとしましょう。しかしながら「今日の天気は？」と聞いても「What's the weather today?」と返されてしまうことでしょう。このようなときは、`IntentPriority`を設定することで天気の話題を優先度高く対応してもらうことができます。

```csharp
if (request.Text.Contains("天気"))
{
    // 天気はプライオリティ「高」
    request.Intent = "weather";
    request.IntentPriority = Priority.High;
}
else if (request.Text.Contains("翻訳"))
{
    // 翻訳は無指定＝プライオリティ標準
    request.Intent = "translate";
}
else
{
    // 雑談はプライオリティ最低
    request.Intent = "chat";
    request.IntentPriority = Priority.Lowest;
}
```

### アドホックな話題の割り込み

先に挙げた優先度の例だと、翻訳が続いている中で天気を聞くと、これまで継続していた翻訳が終わってしまいます。優先度高く別の話題を割り込みつつ終わったら元の話題に戻るためにはリクエストに`IsAdhoc`フラグを立てます。

```csharp
if (request.Text.Contains("天気"))
{
    // 天気はプライオリティ「高」
    request.Intent = "weather";
    request.IntentPriority = Priority.High;
    request.IsAdhoc = true;
}
```

制約事項としてアドホックリクエストは1ターンで完結する必要があります。具体的なイメージは以下の通りです。

- ユーザー「翻訳して」
- Chatdoll「何を翻訳しますか？」
- ユーザー「これはペンです。」
- Chatdoll「This is a pen.」
- ユーザー「これはオレンジですか？」
- Chatdoll「Is this an orange?」
- ユーザー「今日の天気は？」 ←Adhocリクエスト
- Chatdoll「晴れです」
- ユーザー「ありがとう」
- Chatdoll「Thank you.」 ←元に戻っている

### エンティティの抽出

要求の中に対話を進行する上で必要な情報が含まれている場合があり、これをエンティティと呼んでいます。たとえば「マルゲリータピザを2枚」というリクエストがあったとき、商品名の「マルゲリータピザ」と数量「2」がエンティティに該当します。

`ExtractIntentAsync`の中でエンティティが取得できている場合には、リクエストの`Entities`に情報をセットしておきます。

以下は「翻訳」では翻訳インテントとして扱わず、「フランス語に翻訳して」などと翻訳先言語の指定があった場合に翻訳インテントとし、後続処理に「フランス語」を引き継ぐ例です。

```csharp
if (request.Text.Contains("翻訳"))
{
    // 翻訳先言語を取得
    var targetLanguage = GetLanguage(request.Text);
    if (!string.IsNullOrEmpty(targetLanguage))
    {
        // 翻訳先言語が指定されている場合のみ翻訳インテントとして処理
        request.Entities.Add("language", targetLanguage)
        request.Intent = "translate";
    }
}
```


# 入出力の制御

## 音声による処理要求

音声によるリクエストは、`VoiceRequestProvider`により取得します。実際に利用するコンポーネントは特定の音声認識サービスを利用したものになるため、`AzureVoiceRequestProvider`や`GoogleVoiceRequestProvider`となります。`IRequestProvider`を実装または`VoiceRequestProviderBase`を継承してAzure・Google以外のサービスを利用したプロバイダーを追加することもできます。

### Cancellation Settings

- Cancel Words: 対話を終了する合言葉。複数登録可
- Ignore Words: 句読点など音声認識後の文字列から削除したいもの。複数登録可

### Test and Debug

- Use Dummy: マイクで聞き取った音声の代わりにダミーテキストをリクエスト文言として利用するかどうかの設定
- Dummy Text: ダミーのリクエスト文言
- Print Result: コンソールにリクエスト文言を出力するかどうかの設定

### Voice Recorder Settings

- Voice Detection Threshold: 聞き取りの最低音量。ノイズの多い環境ではこの値を大きくしてください
- Voice Detection Minimum Length: リクエストとして認識する最低長（秒）。この値よりも長い発話をリクエストとして認識します
- Silence Duration To End Recording: 発話終了とみなすまでの空白長（秒）
- Listening Timeout: リクエストの受付を終了するまでの時間（秒）

### UI

- Message Window: リクエスト内容を表示するメッセージウィンドウ。無指定の場合はSimpleMessageWindowが利用されます


## カメラによる処理要求

撮影した写真をリクエストとするには、`CameraRequestProvider`を使用します。以下の2通りの方法で利用することができます。

### WakeWordにリクエストタイプを指定

WakeWordListenerにウェイクワードを登録する際、Request Typeに`Camera`を指定することで当該ウェイクワードが呼ばれた際にカメラが起動します。写真によるリクエストを確実に処理できるようにするため、ウェイクワードにはIntentもあわせて指定することを検討してください。

対話の流れ
- 「写真撮って。」（ウェイクワード）
- 「いいよ。笑ってね。」（プロンプト）
- カメラ起動、撮影（リクエスト）
- 「笑えって言ったのに。」（レスポンス）

### Skillの中でリクエストタイプを指定

対話処理の中で`state.Topic.RequiredRequestType`に`RequestType.Camera`を指定することで、次回のリクエストをカメラによる撮影にすることができます。

```csharp
if (state.Topic.IsFirstTurn)
{
    // 次のリクエストの形式をカメラに設定して話題を継続
    state.Topic.RequiredRequestType = RequestType.Camera;
    state.Topic.IsFinished = false;
    response.AddVoiceTTS("いいよ。笑ってね。");
}
else
{
    if (IsSmile(request.Payloads))
    {
        response.AddVoiceTTS("笑ってるね。");
    }
    else
    {
        response.AddVoiceTTS("笑えって言ったのに。");
    }
}
```

対話の流れ
- 「妹ちゃん」（ウェイクワード）
- 「どうしたの？」（プロンプト）
- 「写真撮って。」（リクエスト）
- 「いいよ。笑ってね。」（レスポンス）
- カメラ起動、撮影（リクエスト）
- 「笑えって言ったのに。」（レスポンス）


## QRコードによる処理要求

読み取ったQRコードをリクエストとするには、`QRCodeRequestProvider`を使用します。以下の2通りの方法で利用することができます。

### WakeWordにリクエストタイプを指定

WakeWordListenerにウェイクワードを登録する際、Request Typeに`QRCode`を指定することで当該ウェイクワードが呼ばれた際にQRコードリーダーが起動します。QRコードを確実に処理できるようにするため、ウェイクワードにはIntentもあわせて指定することを検討してください。

対話の流れ
- 「受付して。」（ウェイクワード）
- 「いらっしゃいませ。受付用のQRコードを見せてください。」（プロンプト）
- QRコードリーダー起動、読み取り（リクエスト）
- 「沼津銀行の国木田さんですね。お待ちしておりました。」（レスポンス）

### Skillの中でリクエストタイプを指定

対話処理の中で`state.Topic.RequiredRequestType`に`RequestType.QRCode`を指定することで、次回のリクエストをQRコードリーダーによる読み取りにすることができます。

```csharp
if (state.Topic.IsFirstTurn)
{
    // 次のリクエストの形式をQRコードリーダーに設定して話題を継続
    state.Topic.RequiredRequestType = RequestType.QRCode;
    state.Topic.IsFinished = false;
    response.AddVoiceTTS("いらっしゃいませ。受付用のQRコードを見せてください。");
}
else
{
    // QRコードのデコード結果はリクエストのTextに格納されている
    var guest = GetGuest(request.Text);
    response.AddVoiceTTS($"{guest.Company}の{guest.Name}さんですね？お待ちしておりました。");
}
```

対話の流れ
- 「こんにちは。」（ウェイクワード）
- 「何かご用でしょうか？」（プロンプト）
- 「受付して。」（リクエスト）
- 「いらっしゃいませ。受付用のQRコードを見せてください。」（レスポンス）
- QRコードリーダー起動、読み取り（リクエスト）
- 「沼津銀行の国木田さんですね。お待ちしておりました。」（レスポンス）


### QRコードのデコード処理

QRコードのデコード処理は、`Texture2D`を引数にとってデコード結果の文字列を返す関数として実装し、`ChatdollCamera.DecodeCode`に割当てください。QRコードの読み取りエンジンは同梱していませんので別途ご用意をお願いします。以下はZXing（ゼブラクロッシング）を使用した例です。アプリケーションの`Awake()`に以下コードを追加します。

```csharp
ChatdollCamera.DecodeCode = (texture) =>
{
    var luminanceSource = new Color32LuminanceSource(texture.GetPixels32(), texture.width, texture.height);
    var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(luminanceSource)));
    return result != null ? result.Text : string.Empty;
};
```

## ウェイクワード

AIスマートスピーカーの多くは、その製品名などの合言葉を呼ぶことでリクエスト受付状態となりますが、この合言葉のことをウェイクワードと呼びます。ChatdollKitでは`WakeWordListener`を使用することでウェイクワードによって特定の処理（対話処理の開始など）を呼び出すことができます。
実際に利用するコンポーネントは特定の音声認識サービスを利用したものになるため、`AzureWakeWordListener`や`GoogleWakeWordListener`となります。`WakeWordListenerBase`を継承してAzure・Google以外のサービスを利用したリスナを追加することもできます。

### WakeWord Settings

- Wake Words: 処理を起動する合言葉およびその付随情報。複数登録可
    - Text: 合言葉の文言。音声認識サービスが認識可能な一般的な文言が望ましい
    - Prefix Allowance: 合言葉よりも前に付加された文字列の許容数。例えば「ねえ、」を許容したい場合は3を設定
    - Suffix Allowance: 合言葉よりも後に付加された文字列の許容数。例えば「ちゃん」を許容したい場合は3を設定
    - Intent: 特定のウェイクワードを呼ばれた場合に特定の話題を開始したい場合に指定
    - Request Type: 特定のウェイクワードを呼ばれた場合に、例えばQRコードによるリクエストとしたい場合に指定
    - Inline Request Minumum Length: ウェイクワードに続けてリクエスト本文を含む場合の最低長。例えば「妹ちゃん」がウェイクワードのとき、「妹ちゃん、天気教えて」で「天気教えて」をリクエストとして送信したい場合は5以上を設定
- Cancel Words: 対話を終了する合言葉。複数登録可
- Ignore Words: 句読点など音声認識後の文字列から削除したいもの。複数登録可
- Auto Start: 起動時に自動でウェイクワードの監視を開始するかどうかの設定。デフォルトはtrue

### Test and Debug

- Print Result: コンソールに聞き取った文言を出力するかどうかの設定

### Voice Recorder Settings

- Voice Detection Threshold: 聞き取りの最低音量。ノイズの多い環境ではこの値を大きくしてください
- Voice Detection Minimum Length: ウェイクワードとして認識する最低長（秒）。この値よりも長い発話をウェイクワードとして認識します
- Silence Duration To End Recording: 発話終了とみなすまでの空白長（秒）
- Voice Recognition Maximum Length: これ以上長い発話は音声認識しないようにする設定


## プロンプト

リクエストを要求する発話・アニメーションなどをChatdollKitではプロンプトと呼んでおり、これをカスタマイズすることができます。イメージは以下の通りです。

- 妹ちゃん（ウェイクワード）
- どうしたの？（プロンプト）　←これ
- 今日の天気は？（リクエスト）
- 晴れだよ（レスポンス）

アプリケーション雛形の`ChatdollApplication`や`AzureApplication`またはそれらのサブクラスを利用している場合、インスペクター上で設定することもできますが、コードベースでより詳細な指定をすることも可能です。

```csharp
// プロンプトの発話・アニメーション・表情を定義
var promptAnimatedVoiceRequest = new AnimatedVoiceRequest();
promptAnimatedVoiceRequest.AddVoiceTTS("どうしたの、お兄ちゃん？", preGap: 0.5f);
promptAnimatedVoiceRequest.AddAnimation("PromptPose");
promptAnimatedVoiceRequest.AddFace("Smile");
　：
　：

// プロンプトの処理に割当
chatdoll.OnPromptAsync = async (preRequest, user, state, token) =>
{
    await modelController.AnimatedSay(promptAnimatedVoiceRequest, token);
};
```


# 高度な利用方法

## 形態素解析

形態素解析を利用することで、ユーザーの発話意図（インテント）をより正確に理解したり、より多くの情報（エンティティ）を取得することができます。

例
- 形容詞の有無により「今日はいい天気ですね。（雑談）」と「今日の天気は？（天気）」を判別
- 「嫁の実家の最寄駅にいるよ」から、「嫁の実家の最寄駅」をひと続きの単語として取得

ChatdollKit本体には形態素解析機能はありませんが、外部の形態素解析結果を格納するために`Request`オブジェクトには`Words`プロパティが用意されています。`Words`プロパティは`WordNode`型のリストで、MeCabによる解析結果を格納するためのプロパティを持っています。具体的なクラス定義は以下の通りです。

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

なおUnityのサポート対象のあらゆるプラットフォームで動作する形態素解析エンジンを探すのは難しいかもしれませんので、その観点からも対話処理全体をサーバーサイドで実装し、形態素解析も当該サーバ上で行うことをお勧めします。


## 対話処理のサーバーサイド実装

実機のアプリケーションを差し替えてのデバッグ作業に時間がかかる場合や対話処理の変更を直ちに反映させたい場合には、対話処理（SkillRouterおよびSkill）をサーバーサイドに配置することができます。

このコンポーネントは実験的なものですが、`HttpSkillRouter`をアタッチすることで対話処理APIと連携できるようになります。

サーバーサイドのSDKについてはPythonベースのものを公開していますので参考にしてください。
https://github.com/uezo/chatdollkit-server-python/blob/main/README.ja.md


# おわりに

ChatdollKitを利用するための一通りの説明は以上となります。わからないことがありましたらお気軽にお問い合わせください。


以上
