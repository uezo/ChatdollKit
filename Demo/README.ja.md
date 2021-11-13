# ChatdollKit Demo

MultiSkillsのExampleを組み込んだデモです。

# 機能

- 天気予報。指定した都市の天気と最高or最低気温を教えてくれます
- 翻訳。Azure または Googleの翻訳APIによる日本語→英語の翻訳をしてくれます（要APIキー）
- 雑談。A3RTの雑談APIによる雑談トークをしてくれます（要APIキー）

# 設定方法

- [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347)、 [Oculus LipSync Unity](https://developer.oculus.com/downloads/package/oculus-lipsync-unity/)、 [ユニティちゃんトゥーンシェーダー Ver.2.0.7](https://unity-chan.com/download/releaseNote.php?id=UTS2_0)をインポート
- MainAzure または MainGoogleのインスペクターに以下のAPIキー等を入力
    - Azure Speech Services / Google Cloud Speech APIのAPIキー（必須）、リージョン（Azureのみ必須）
    - Translation API Key（任意。指定しない場合は翻訳スキルは起動しない）
    - Chat A3RT API Key（任意。指定しない場合は雑談のかわりにおうむ返しスキルが起動）
    - 天気予報で確認したい都市

# 実行方法

- Unityの再生ボタンを押下。アイドル状態になりまばたきを開始したことを確認
- 「こんにちは」と話しかけると、「どうしたの？」と聞き返され、メッセージウィンドウに「Listening...」と表示されたことを確認
- 3Dモデルに話しかける。このとき、発話文言に「天気」が含まれれば天気スキルを、「翻訳」が含まれれば翻訳スキルを、それ以外の場合は雑談スキルが起動される
- 雑談スキルと翻訳スキルは発話・応答後も会話が自動継続される。「おしまい」と発話することにより会話を終了

# 3Dモデルについて

デモに含まれるモデルは、フリー素材キャラクター「つくよみちゃん」（© Rei Yumesaki）を使用しています。

- つくよみちゃん公式サイト: https://tyc.rei-yumesaki.net/
- 3Dモデル配布先: https://tyc.rei-yumesaki.net/material/avatar/3d-a/
- プロジェクトTwitterアカウント: @TYC_Project

The model in this demo is Tsukuyomi-chan ((c)Rei Yumesaki).

- Official site: https://tyc.rei-yumesaki.net/
- Distribution: https://tyc.rei-yumesaki.net/material/avatar/3d-a/
- Twitter: @TYC_Project


以上
