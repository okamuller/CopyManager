# CopyManager - CLAUDE.md

## プロジェクト概要

CopyManager は Windows 専用の FastCopy ジョブ管理ツールです。
複数の大容量ファイル・フォルダを大容量 NAS へコピーする際のジョブをキュー管理・進捗監視する GUI アプリケーションです。

- **プラットフォーム**: Windows 10/11 専用
- **UI フレームワーク**: WPF (.NET 8 以降)
- **言語**: C# 12+
- **FastCopy 連携**: コマンドライン (CLI) 経由

---

## 技術スタック

| 項目 | 内容 |
|------|------|
| Runtime | .NET 8 (Windows) |
| UI | WPF (Windows Presentation Foundation) |
| 言語 | C# 12 |
| FastCopy 連携 | `System.Diagnostics.Process` で FastCopy.exe を起動 |
| 設定永続化 | JSON (System.Text.Json) |
| ジョブ永続化 | JSON ファイル (ローカル) |

---

## リポジトリ構成

```
CopyManager/
├── CLAUDE.md
├── spec.md
├── CopyManager.sln
├── src/
│   └── CopyManager/
│       ├── CopyManager.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Models/
│       │   ├── CopyJob.cs          # ジョブデータモデル
│       │   └── AppSettings.cs      # アプリ設定モデル
│       ├── ViewModels/
│       │   ├── MainViewModel.cs    # メイン画面 VM
│       │   └── JobViewModel.cs     # 個別ジョブ VM
│       ├── Services/
│       │   ├── FastCopyService.cs  # FastCopy.exe 呼び出しラッパー
│       │   ├── JobQueueService.cs  # キュー管理・実行制御
│       │   └── SettingsService.cs  # 設定の読み書き
│       ├── Views/
│       │   ├── JobEditDialog.xaml  # ジョブ追加・編集ダイアログ
│       │   └── SettingsDialog.xaml # アプリ設定ダイアログ
│       └── Converters/             # WPF 値コンバーター
└── tests/
    └── CopyManager.Tests/
        ├── CopyManager.Tests.csproj
        ├── FastCopyServiceTests.cs
        └── JobQueueServiceTests.cs
```

---

## ビルド・実行方法

```bash
# ビルド
dotnet build CopyManager.sln

# 実行
dotnet run --project src/CopyManager/CopyManager.csproj

# テスト
dotnet test tests/CopyManager.Tests/CopyManager.Tests.csproj

# リリースビルド (Windows 自己完結型)
dotnet publish src/CopyManager/CopyManager.csproj \
  -c Release -r win-x64 --self-contained true \
  -o ./publish
```

---

## 主要な設計方針

### MVVM パターン
WPF 標準の MVVM を採用。`INotifyPropertyChanged` + `ICommand` を使用する。
外部ライブラリ (Prism, CommunityToolkit.Mvvm 等) は最小限に抑える方針だが、
`CommunityToolkit.Mvvm` は採用可とする（ソースジェネレーター活用）。

### FastCopy 連携
- `FastCopyService` が FastCopy.exe のパスと引数を組み立て、`Process` で起動する
- 標準出力・標準エラーを非同期で読み取り、進捗・エラーを `JobViewModel` に通知する
- FastCopy の終了コードでジョブの成否を判定する (0 = 成功)

### ジョブキュー
- `JobQueueService` は `ConcurrentQueue<CopyJob>` でキューを管理する
- 同時実行数は設定で変更可能 (デフォルト: 1)
- ジョブ状態: `Pending → Running → Completed / Failed / Cancelled`
- 失敗時は設定されたリトライ回数まで自動再試行する

### 設定・ジョブの永続化
- `%APPDATA%\CopyManager\settings.json` にアプリ設定を保存する
- `%APPDATA%\CopyManager\jobs.json` にジョブリストを保存する
- アプリ起動時にジョブリストを復元し、未完了ジョブは `Pending` 状態に戻す

---

## FastCopy コマンドライン仕様

FastCopy.exe の主要引数（参考）:

```
FastCopy.exe /cmd=<コマンド> [オプション] "source1" "source2" /to="destination"

コマンド:
  diff        差分コピー (新規・更新のみ)
  force_copy  全ファイル上書きコピー
  move        移動

主要オプション:
  /bufsize=256m     バッファサイズ
  /speed=full       転送速度 (full / autoslow / 1..9)
  /log              ログ有効化
  /logfile="path"   ログファイルパス
  /auto_close       完了後に FastCopy ウィンドウを自動クローズ
  /no_confirm       確認ダイアログを表示しない
  /error_stop       エラー発生時に停止
  /filelog          ファイル単位のログ出力
```

---

## コーディング規約

- 命名: C# 標準 (PascalCase / camelCase)
- 非同期: `async/await` を積極的に使用する。UI スレッドブロッキングは禁止
- エラー処理: 外部プロセス呼び出しは必ず例外をキャッチしてジョブに記録する
- ログ: `Microsoft.Extensions.Logging` を使用する
- 単体テスト: `xUnit` を使用する。`FastCopyService` と `JobQueueService` は必ずテストを書く

---

## 依存関係

```xml
<!-- src/CopyManager/CopyManager.csproj -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.*" />

<!-- tests/CopyManager.Tests/CopyManager.Tests.csproj -->
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="Moq" Version="4.*" />
```

---

## よくある作業パターン

### ジョブ追加フロー
1. ユーザーが「追加」ボタンをクリック
2. `JobEditDialog` でソース・宛先・オプションを入力
3. `MainViewModel.AddJobCommand` が `CopyJob` を生成して `JobQueueService` に追加
4. `JobQueueService` がキューに積み、実行可能なら即座に `FastCopyService` を起動

### 進捗取得フロー
1. `FastCopyService` が FastCopy.exe の標準出力を非同期で読み取る
2. パース結果を `JobViewModel.Progress` にバインドする
3. WPF の `ProgressBar` がリアルタイム更新される
