# ModelControllerの使い方

ModelControllerは、3Dモデルにアニメーションをつけたりしゃべらせたり表情を変えたりをコードベースで制御するコンポーネントです。ステートマシンを使いこなすより簡単ですが、よりきめ細やかな制御がしたくなったら本コンポーネントを卒業しましょう。

# 初期設定

[README](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)の内容にしたがってChatdollKitをセットアップしてください。また、モデルを操作するスクリプトとして`ModelControlExample.cs`を追加しましょう。

# アニメーションの実行

3Dモデルを動かすには`Animate()`メソッドを使用します。

## 事前準備

[README.ja.md](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)記載のHelloWorld exampleの準備と同じになります。Animator Controllerにステートを作成し、モーションを登録しておきます。この文書では[Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406)の使用を前提として例を示しています。
`01_Idles`の中身を`Base Layer`にドロップしてステートを作成してください。また、`Upper Body`というレイヤーを追加し、ここには`02_Layers`の中身をドロップしておいてください。また`Default`ステートの作成もお願いします。

## 基本的な使い方

モーションとして両手を腰にあてて怒っているポーズ`AGIA_Idle_angry_01_hands_on_waist`（ステート名）をやってもらうには以下の通りです。インスペクターのAnimateボタンで開始、Stopボタンで終了します。

```Csharp
// アニメーション要求の作成
var request = new AnimationRequest();
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist");

// アニメーションの実行
await chatdoll.ModelController.Animate(request, GetToken());
```

アニメーションの実行時間を指定するには、第二引数に`float`型で長さを指定してください。単位は秒です。例の場合は3秒後にStopボタンを押さずとも元のアイドル状態に戻ります。

```Csharp
// アニメーション要求の作成
var request = new AnimationRequest();
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist", 3.0f);

// アニメーションの実行
await chatdoll.ModelController.Animate(request, GetToken());
```

また、現在の状態から何秒かけて指定したアニメーションの状態に移行するかについて、第三引数で指定することもできます。単位は秒です。例の場合は2秒かけてゆっくり怒った状態に遷移します。

```Csharp
// アニメーション要求の作成
var request = new AnimationRequest();
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist", 3.0f, 2.0f);

// アニメーションの実行
await chatdoll.ModelController.Animate(request, GetToken());
```

アニメーションを続けて実行したいときは、`AddAnimation()`を複数回実行してアニメーションを追加します。以下の例では3種類のアニメーションをそれぞれ3秒ずつ実行し、最後にアイドル状態に戻ります。

```CSharp
// アニメーション要求の作成
var request = new AnimationRequest();
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist", 3.0f);
request.AddAnimation("AGIA_Idle_brave_01_hand_on_chest", 3.0f);
request.AddAnimation("AGIA_Idle_energetic_01_right_fist_up", 3.0f);

// アニメーションの実行
await chatdoll.ModelController.Animate(request, GetToken());
```

## 別レイヤーのアニメーションの実行

手や頭を振るなど体の一部をアニメーションさせるときにはレイヤー機能を利用します。事前にAnimator Controllerにレイヤーを作成し、適当なレイヤーマスクを設定したら、そこにモーションを配置してステートを作成しておきます。
このレイヤーに配置したアニメーションを実行するには、アニメーションの名前に続いて第二引数としてレイヤー名を指定します。以下の例では、2秒間体を揺らしたあと2秒間アイドルして、最後に2秒間プイッと横を向いたら元に戻ります。

```CSharp
// アニメーション要求の作成
var request = new AnimationRequest();
request.AddAnimation("AGIA_Layer_swinging_body_01", "Upper Body", 2.0f);
request.AddAnimation("Default", "Upper Body", 2.0f);
request.AddAnimation("AGIA_Layer_look_away_01", "Upper Body", 2.0f);

// アニメーションの実行
await chatdoll.ModelController.Animate(request, GetToken());
```

歩きながら手を振るなど、複数のレイヤーのアニメーションを重ねて実行したいケースがあります。そんな場合も、これまで使ってきた`AddAnimation()`で表現することができます。

```CSharp
// アニメーション要求の作成
var request = new AnimationRequest();

// ベースレイヤーのアニメーション
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist", 3.0f);
request.AddAnimation("AGIA_Idle_brave_01_hand_on_chest", 3.0f);
request.AddAnimation("AGIA_Idle_energetic_01_right_fist_up", 3.0f);

// 上半身のアニメーション
request.AddAnimation("AGIA_Layer_swinging_body_01", "Upper Body", 2.0f);
request.AddAnimation("Default", "Upper Body", 2.0f);
request.AddAnimation("AGIA_Layer_look_away_01", "Upper Body", 2.0f);

// アニメーションの実行
await chatdoll.ModelController.Animate(request, GetToken());
```

かなりしっちゃかめっちゃかですが、混ざっていることが確認できたと思います。経過時間の積み上げは各レイヤー毎に行われますので、複数レイヤーで同期したい場合は`preGap`を指定して実行時間を合わせていきます。以下の例は体を揺さぶるのを手を胸に当てるのと同期しています。

```CSharp
// アニメーション要求の作成
var request = new AnimationRequest();

// ベースレイヤーのアニメーション
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist", 3.0f);
request.AddAnimation("AGIA_Idle_brave_01_hand_on_chest", 3.0f);
request.AddAnimation("AGIA_Idle_energetic_01_right_fist_up", 3.0f);

// 上半身のアニメーション
request.AddAnimation("AGIA_Layer_swinging_body_01", "Upper Body", 2.0f, preGap: 3.0f);
request.AddAnimation("Default", "Upper Body", 2.0f);
request.AddAnimation("AGIA_Layer_look_away_01", "Upper Body", 2.0f);

// アニメーションの実行
await chatdoll.ModelController.Animate(request, GetToken());
```

注意が必要なのは、ベースレイヤーのアニメーションが終わると、他のレイヤーのアニメーションの完了を待たずにアイドル状態のアニメーションがスタートします。そのため、上半身レイヤーのアニメーションの最後を2秒ではなく5秒や10秒にしてもベースアニメーションの完了時点で上半身も元に戻ります。


## ウェイト調整（Experimental）

アニメーションの効かせ具合を`AddAnimation`の引数に`weight`を渡すことで調整できるようにしていますが、前後のアニメーションで値が異なるとき滑らかにつなぐことができないため、現在は実験的なFeatureとして実装されています。


# 発話の実行

3Dモデルに音声ファイルの内容を喋らせるには、`Say()`メソッドを使用します。

## 事前準備

喋ってもらいたい音声が収録されたファイルを事前に`ModelController`のインスタンスに登録します。以下の例では`Resources`ディレクトリの中の`Voices`ディレクトリに格納した音声ファイルを、ファイル名をキーに`ModelController`に登録しています。アプリケーションの`Awake()`など初期処理の中で読み込んでおきましょう。

```Csharp
// 音声の登録
foreach (var ac in Resources.LoadAll<AudioClip>("Voices"))
{
    chatdoll.ModelController.AddVoice(ac.name, ac);
}
```

## 基本的な使い方

「呼びました？」という声が録音された音声ファイル`line-girl1-yobimashita1`を喋ってもらうには以下の通りです。インスペクターのSayボタンで開始、話している途中ではStopボタンで終了します。

```Csharp
// 発声要求の作成
var request = new VoiceRequest();
request.AddVoice("line-girl1-yobimashita1");

// 発声の実行
await chatdoll.ModelController.Say(request, GetToken());
```

複数の音声ファイルを連続的に喋らせるには、`AddVoice()`を複数回呼び出して要求に音声を追加していきます。

```Csharp
// 発声要求の作成
var request = new VoiceRequest();
request.AddVoice("line-girl1-yobimashita1");
request.AddVoice("line-girl1-haihaai1");
request.AddVoice("line-girl1-konnichiha1");

// 発声の実行
await chatdoll.ModelController.Say(request, GetToken());
```

前後に空白を挿入するには`preGap`または`postGap`を指定します。

```Csharp
// 発声要求の作成
var request = new VoiceRequest();
request.AddVoice("line-girl1-yobimashita1", postGap: 1.0f);
request.AddVoice("line-girl1-haihaai1");
request.AddVoice("line-girl1-konnichiha1", preGap: 1.0f);

// 発声の実行
await chatdoll.ModelController.Say(request, GetToken());
```

# 表情の変更

3Dモデルの表情を変更するには、`SetFace()`メソッドを使用します。

## 事前準備

シェイプキーの組み合わせによって作成した表情をあらかじめ`ModelController`に登録します。表情の名前をキーに、変更したいシェイプキーの名前と適用量（0〜100）のペアを`AddFace()`に渡します。

```Csharp
// 笑顔の定義
chatdoll.ModelController.AddFace("Smile", new Dictionary<string, float>() {
    {"eyes_close_1", 100.0f }
});
// 悲しい顔の定義
chatdoll.ModelController.AddFace("Sad", new Dictionary<string, float>() {
    {"eyes_close_2", 15.0f },
    {"mouth_:0", 60.0f },
    {"mouth_:(", 70.0f },
});
```

## 基本的な使い方

表情を設定するには、事前準備で登録した表情名を`SetFace()`に渡します。第二引数に継続時間（秒）を指定すれば、一定時間だけ表情を変更することもできます。

```Csharp
chatdoll.ModelController.SetFace("Smile", 2.0f);
```

表情設定中にはまばたきをしたくない場合、以下の通り前後でまばたきを停止・再開します。このとき、表情の継続時間を待ってからまばたきを再開するため、`await`をつけて実行してください。

```Csharp
chatdoll.ModelController.StopBlink();
await chatdoll.ModelController.SetFace("Smile", 2.0f);
chatdoll.ModelController.StartBlink();
```

## FaceClipEditorの利用

シェイプキーを組み合わせをコードで定義したりあとで中身を確認するのは骨の折れる作業です。そこで、インスペクタで調整した表情のスナップショットをChatdollKitで読み込み可能な形式で保存することができるようにしました。是非ご活用ください。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/faceclipeditor.png" width="640">


# 発話・アニメーション・表情変更を組み合わせる

発話・アニメーション・表情変更を組み合わせて実行するには、`AnimatedSay()`メソッドを使用します。それぞれの事前準備については同様です。

## 基本的な使い方

「呼びました？」と喋りながら手を腰に当てるポーズをとるには、以下の通り要求オブジェクトに`AddVoice()`で音声、`AddAnimation`でアニメーションを追加し、`AnimatedSay()`を呼び出します。
このとき、アニメーションに`Duration`を指定しなくても喋り終わると自動的にアニメーションが終了してアイドル状態に戻ります。声に`preGap`や`postGap`を指定した場合は、前後の空白時間の間もアニメーションが実行されます。

```Csharp
// 発話・アニメーション要求の作成
var request = new AnimatedVoiceRequest();
request.AddVoice("line-girl1-yobimashita1", 1.0f, 1.0f);
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist");

// 発話・アニメーションの実行
await chatdoll.ModelController.AnimatedSay(request, GetToken());
```

また、複数のフレーズを発話し、並行して複数のアニメーションを実行するには、以下の通り`AddVoice()`と`AddAnimation()`を行います。

```Csharp
// 発話・アニメーション要求の作成
var request = new AnimatedVoiceRequest();
request.AddVoice("line-girl1-yobimashita1");
request.AddVoice("line-girl1-haihaai1");
request.AddVoice("line-girl1-konnichiha1");
request.AddAnimation("Default", 2.0f);
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist", 2.0f);

// 発話・アニメーションの実行
await chatdoll.ModelController.AnimatedSay(request, GetToken());
```

このとき、発話とアニメーションは全く同期することなく、声は声、アニメーションはアニメーションでAddされたものを順次連続実行します。


## 音声とアニメーションを同期する

音声とアニメーションのタイミングを合わせるには、音声やアニメーションに`preGap`を指定して開始タイミングを調整するほか、発話とアニメーションを同期するための「フレーム」という概念を`AnimatedVoiceRequest`が提供しています。
以下の例では、「はいは〜い」の発声にあわせて腰に手をあててもらうために、「はいは〜い」を`AddVoice()`する際に`asNewFrame: true`を指定しています。これにより`preGap`を指定しなくてもアニメーションを音声と同じタイミングで実行することができます。なお前のフレームで実行中のアニメーションがあっても中断されます。

```Csharp
// 発話・アニメーション要求の作成
var request = new AnimatedVoiceRequest();

request.AddVoice("line-girl1-yobimashita1");
request.AddAnimation("Default");

request.AddVoice("line-girl1-haihaai1", 1.0f, 1.0f, asNewFrame: true);
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist");

request.AddVoice("line-girl1-konnichiha1", asNewFrame: true);
request.AddAnimation("Default");

// 発話・アニメーションの実行
await chatdoll.ModelController.AnimatedSay(request, GetToken());
```

## 表情を設定する

「はいは〜い」という時だけにっこり笑ってもらうには、以下の通り当該フレームにて`AddFace()`を使って表情を設定します。なお表情は`AnimatedVoiceRequest`に登録されたすべての処理が終わるまで元に戻らないため、`Duration`で継続時間を指定した後明示的に`Default`に戻すような対応が必要です。

```Csharp
// 発話・アニメーション要求の作成
var request = new AnimatedVoiceRequest();

request.AddVoice("line-girl1-yobimashita1");
request.AddAnimation("Default");

request.AddVoice("line-girl1-haihaai1", 1.0f, 1.0f, asNewFrame: true);
request.AddAnimation("AGIA_Idle_angry_01_hands_on_waist");
request.AddFace("Smile", 2.0f);   // 笑顔を2秒間継続
request.AddFace("Default");       // 元に戻す

request.AddVoice("line-girl1-konnichiha1", asNewFrame: true);
request.AddAnimation("Default");

// 発話・アニメーションの実行
await chatdoll.ModelController.AnimatedSay(request, GetToken());
```

なお`AnimatedSay()`を実行するとアイドル状態に戻るまでの間自動的にまばたきが停止されます。まばたきの停止・再開制御をせずに実行するには、リクエストの`disableBlink`を`false`に設定してください。

```Csharp
// 発話・アニメーション要求の作成
var request = new AnimatedVoiceRequest(disableBlink: false);
```

# アイドル状態の定義

アイドル状態を定義するには2通りの方法があります。

1. アイドル状態のアニメーションを`AnimationRequest`として複数登録しランダム実行
1. ユーザー実装による任意の処理を実行

前者はモーションの定義をすればすぐに使えるようになる反面、自由度が高くありません。後者は高度な機能やアニメーションなど自由に組み込むことができる反面、全てを自ら開発する必要があります。目的やスキルレベルに合わせてお好みで選択してください。

## アイドル状態のアニメーションをランダム実行

`List<AnimationRequest>`をnewしてアニメーションを追加した後に`ModelController.IdleAnimationRequests`に設定することでも問題ありませんが、`ModelController.AddIdleAnimation()`を利用することでより簡単にアイドル状態を定義することができます。以下の例では、デフォルトの立ち状態と腰に手を当てた状態とが一定時間（デフォルトで5秒）おきにランダムで入れ替わり実行されます。

```Csharp
chatdoll.ModelController.AddIdleAnimation("Default");
chatdoll.ModelController.AddIdleAnimation("AGIA_Idle_angry_01_hands_on_waist");
```

また、以下の最後の行のように`addToLastRequest: true`と指定することで、直前に追加したアニメーションに重ねて実行するレイヤーのアニメーションを登録することもできます。

```Csharp
chatdoll.ModelController.AddIdleAnimation("Default");
chatdoll.ModelController.AddIdleAnimation("AGIA_Idle_angry_01_hands_on_waist");
chatdoll.ModelController.AddIdleAnimation("Default");
chatdoll.ModelController.AddIdleAnimation("AGIA_Layer_swinging_body_01", "Upper Body", addToLastRequest: true);
```

## ユーザー実装による任意の処理を実行

任意の処理を作成して、`ModelController.IdleFunc`に設定します。引数として`CancellationToken`が渡されてきますので、キャンセルされた場合は処理を終了するように実装しましょう。

```Csharp
// アイドル処理を実装
private async Task IdleAsync(CancellationToken token)
{
    // 任意の処理
}

// IdleFuncに設定
chatdoll.ModelController.IdleFunc = IdleAsync;
```

# さいごに

`ModelController`は簡単に動かせる反面、制約事項もいろいろあると思います。そのあたりのバランスについては試行錯誤中ですので、ぜひご意見をいただけると嬉しいです。

twitter: [@uezochan](https://twitter.com/uezochan)
