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


# Appendix 3. uLipSyncの利用

OVRLipSyncのかわりにuLipSyncを使用する場合は、以下の公式READMEまたは作者さまのブログの内容に従ってセットアップしてください。（iOSアプリの場合、OVRLipSyncを利用していると審査が通らないようです🙃）

- https://github.com/hecomi/uLipSync
- https://tips.hecomi.com/entry/2021/02/27/144722

uLipSyncを利用する場合、ModelControllerのセットアップで3Dモデルに自動でアタッチされる`OVRLipSyncHelper`を3Dモデルから削除してください。なおuLipSyncは表情をリセットするための機能を提供していないようなのでChatdollKitとしてヘルパーを提供していませんが、特に問題なく動くと思います。
