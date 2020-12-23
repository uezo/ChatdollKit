# ChatdollKit
ChatdollKitは、お好みの3Dモデルを使って音声対話可能なチャットボットを作るためのフレームワークです。

[🇬🇧English version is here](https://github.com/uezo/ChatdollKit/blob/master/README.ja.md)

<img src="https://uezo.blob.core.windows.net/github/chatdoll/chatdollkit_architecture.png" width="640">

# 🚀 クイックスタート

セットアップ手順についてはこちらの2分程度の動画をご覧いただくとより簡単に理解できます: https://www.youtube.com/watch?v=aJ0iDZ0o4Es

1. 📦パッケージのインポート
    - [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347) と [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/) のインポート
    - [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) のインポート

1. 🐟リソースの準備
    - 3Dモデルをインポートしてシーンに追加
    - アニメーションクリップをアニメーションディレクトリに配置 👉チュートリアル用 [Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406)
    - [Azure Speech Services](https://azure.microsoft.com/ja-jp/services/cognitive-services/speech-services/) または [Google Cloud Speech API](https://cloud.google.com/speech-to-text/) のAPIキーの取得

1. 🍣セットアップ
    - おうむ返し（Echo）のExampleを3Dモデルに追加してインスペクターでAPIキーなどを設定
    - インスペクターのコンテキストメニューから`Setup ModelController`と`Setup Animator`を実行

本READMEのほか、[ChatdollKit マニュアル](https://github.com/uezo/ChatdollKit/blob/master/manual.ja.md)に各機能の網羅的な説明がありますので参照ください。


# 📦 パッケージのインポート

最新版の [ChatdollKit.unitypackage](https://github.com/uezo/ChatdollKit/releases) をダウンロードして、任意のUnityプロジェクトにインポートしてください。また、以下の依存ライブラリもインポートが必要です。

- [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)
- [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)

[Gatebox](https://www.gatebox.ai/)アプリを作る場合、ChatdollKitのリリースパッケージと一緒に公開されている[ChatdollKit Gatebox Extension](https://github.com/uezo/ChatdollKit/releases)もインポートしてください。

# 🐟 リソースの準備

## 3Dモデル

お好みの3Dモデルをシーンに配置してください。シェーダーやダイナミックボーンなど必要に応じてセットアップしておいてください。なおこの手順で使っているモデルはシグネットちゃんです。とてもかわいいですね。 https://booth.pm/ja/items/1870320

<img src="https://uezo.blob.core.windows.net/github/chatdoll/camera_light.png" width="640">

## Animations

`/Animations`ディレクトリを作成し、アニメーションクリップを配置してください。
なおこの手順では[Anime Girls Idle Animations Free](https://assetstore.unity.com/packages/3d/animations/anime-girl-idle-animations-free-150406)というモーション集を利用しています。大変使い勝手が良いので気に入ったら有償版の購入をオススメします。


# 🍣 セットアップ

## ChatdollKitの追加

`ChatdollKit/Excamples/Echo` から `EchoAppAzure` または `EchoAppGoogle` を3Dモデルに追加してください。アニメーション、音声、表情をコントロールする`ModelController`やその他必要なコンポーネントが合わせて追加されます。

## Configure Application

必要最小限の設定としては、APIキー、リージョン（Azureの場合のみ）、言語のみ設定すればOKです。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/add_echoapp_mark.png" width="640">


## ModelControllerの設定

インスペクターのコンテキストメニューから`Setup ModelController`を選択すると、LipSync等が自動的に設定されます。その後、まばたきをするために目を閉じる表現のシェイプキーの名前を`Blink Blend Shape Name`に設定しましょう。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/setup_mc.png" width="640">

手動で設定したい場合は [Appendix1. ModelControllerの手動設定](#Appendix%201.%20ModelControllerの手動設定) を参照してください。

## Animatorの設定

インスペクターのコンテキストメニューから`Setup Animator`を選択するとフォルダ選択ダイアログが表示されるので、アニメーションクリップが配置されたフォルダを選択してください。サブフォルダが含まれる場合、それらと同名のレイヤーが`AnimatorController`に作成され、サブフォルダ内のアニメーションクリップはそのレイヤーに配置されます。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/choose_animation_dir.png" width="640">

このケースでは、フォルダを選択したのちにベースレイヤー（`Base Layer`）またはそれぞれのレイヤー（`01_Idles`、`02_Layers`、`03_Others`）に配置するか確認ダイアログが表示され、配置先を選択することができます。

デフォルトのアイドルアニメーションを変更したい場合はアニメーターコントローラーの`Default`ステートに紐づけられたアニメーションクリップを変更しましょう。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/change_idle.png" width="640">


## 動作確認

UnityのPlayボタンを押します。3Dモデルがまばたきをしながらアイドル時のアニメーションを行っていることを確認してください。

<img src="https://uezo.blob.core.windows.net/github/chatdoll/run_echo.png" width="640">

それでは準備が整いましたので、3Dモデルと会話してみましょう。

- 「こんにちは」またはインスペクターの`Wake Word`に設定した文言を話しかける
- 「どうしたの？」または`Prompt Voice`に設定した文言で応答
- 「ハローワールド！」など、話しかけたい言葉をしゃべる
- 「ハローワールド」と、話しかけたのと同じ内容を応答


# カスタムアプリケーションの作り方

Examplesに同梱の`MultiDialog`の実装サンプルを確認ください。

- 対話のルーティング：`Router`には、発話内容からユーザーが話したいトピックを選択するロジックの例が実装されています
- 対話の処理：`TranslateDialog`をみると、リクエスト文言を利用して翻訳APIを叩き、結果を応答する一連の例が実装されています

ChatdollKitを利用した複雑で実用的なバーチャルアシスタントの開発方法については、現在コンテンツを準備中です。


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
