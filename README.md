# Taskbar Runner

Windows のタスクバーの上を、ロボットが走り続ける小さなゲームです。

<img width="800" height="160" alt="demo" src="https://github.com/user-attachments/assets/4b0a9bb8-e4ac-4487-9946-78a414be0902" />


## ダウンロード

[Releases](../../releases) から `TaskbarRunner.exe` を取得。

## 遊び方

1. exe を実行すると、通知領域（時計のとなり）にロボットのアイコンが出ます
2. アイコンを**左クリック**するとゲームが開きます
3. **SPACE** でスタート

| キー | 動作 |
| --- | --- |
| SPACE / ↑ | ジャンプ（空中でもう一度押すと2段ジャンプ） |
| ↓ | しゃがむ |
| ← → | 左右に移動 |
| ESC | 1回で一時停止、もう一度で元の作業に戻る |

ESC を1回押すと、画面はそのままでロボットだけが止まります。**SPACE** で続きから再開、**R** で続きを捨てて最初から。もう一度 **ESC** を押すと画面が消えて、元のウィンドウに戻ります。

他のウィンドウをクリックしたときも、走行が残ったまま止まって画面が消えます。アイコンをもう一度左クリックすると続きが出ます。

終了するときは、アイコンを右クリックして Exit を選びます。同じメニューから、速さ・キャラクターの大きさ・表示位置・描画頻度を変えられます。

## 保存されるもの

設定と記録は `%AppData%\TaskbarRunner` に保存されます。

- `settings.json` — 速さや大きさの設定
- `save.json` — ベストスコア、プレイ回数、累計距離

## ソースからビルドする

```powershell
git clone https://github.com/takashi145/taskbar_runner.git
cd taskbar_runner
./scripts/verify.ps1     # ビルドとテスト
./scripts/publish.ps1    # artifacts/ に exe を生成
```

.NET 10 SDK が必要です。

## ライセンス

[MIT](LICENSE)
