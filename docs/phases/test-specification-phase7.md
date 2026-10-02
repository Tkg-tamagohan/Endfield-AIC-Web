# Phase 7 テスト仕様書

**対象**: Phase 7 成果物（アイコン取り込み・マニフェスト出力のパイプライン、App の IconKey 表示）
**前提ドキュメント**: [requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)、[implementation-plan.md](../implementation-plan.md)、[implementation-plan-phase7.md](implementation-plan-phase7.md)

> 本書は Phase 7 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> Phase 3 で採番済みの `ICO-01`〜`ICO-12`（マニフェスト照合・解決）は test-specification-phase3.md を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。Infrastructure 層は `tests/EndfieldAicWeb.Infrastructure.Tests`、管理ツールの編集セッションは `tests/EndfieldAicWeb.Admin.Tests`（新設）に配置する |
| テストデータ | コード上で `MasterDocument` と PNG バイト列のフィクスチャを構築する。画像の実体はバイト列一致が本質のため任意のバイト列でよい |
| 実行コマンド | `dotnet test` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | Admin・App を `dotnet run` で起動し、ブラウザプレビューで §5 の項目を確認する（ui-mock-first ルールに従い、ユーザー確認を先に取った） |
| Blazor UI 自動テスト | 対象外（bUnit・E2E は導入しない。implementation-plan-phase6.md §1 と同じ方針） |
| 128×128 正規化 | ブラウザ Canvas に依存するため自動テスト対象外。§5 の手動確認で扱う |
| validate_master.py | CI で実行される Python スクリプトのため単体テストは持たず、§5 で実データに対する実行結果を確認する |

## 2. フィクスチャ定義

### I-01: アイコン参照を含む最小文書

`MasterDocument`。Phase 6 の M-01 と同じ構造に IconKey を加えたもの。

- アイテム: `i-ore`（IconKey `icon-ore`）、`i-part`（IconKey `icon-part`）、`i-none`（IconKey なし）
- 設備: `f-asm`（IconKey `icon-fac`）
- 環境: `env-gas`（IconKey なし、供給設備 `f-asm`、消費アイテム `i-ore`）
- イベント: `ev-on`（IconKey なし）
- レシピ: `r-part`（IconKey なし、入力 `i-ore`×2、出力 `i-part`×1、ペア `f-asm 4秒`）
- `Icons`: `icon-ore`・`icon-part`・`icon-fac` のエントリ（File・Sha256・Bytes は対応バイト列から算出）

### I-02: アイコンバイト列

各キーに対応する任意の PNG バイト列（内容は問わない。`icon-ore`→`0xA0..` 32 バイトなど固定値）。

## 3. テスト項目一覧

### IMP: InMemoryIconFileProvider

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| IMP-01 | 登録と読み取り | `Set("icons/a.png", bytes)` | `ReadAllBytes("icons/a.png")` が同じバイト列を返し、`Paths` に `icons/a.png` がある |
| IMP-02 | 区切り・先頭スラッシュの正規化 | `Set("icons\\a.png", …)` または `Set("/icons/a.png", …)` | `icons/a.png` で読める |
| IMP-03 | 不正パスの拒否 | `Set("../x.png", …)`・`Set("a/../x.png", …)`・`Set("", …)` | false を返し登録されない |
| IMP-04 | 未登録パスの読み取り | 登録なしで `ReadAllBytes` | null |
| IMP-05 | クリア | 登録後に `Clear()` | `Paths` が空、`ReadAllBytes` が null |

### EXP: IconExportPlanner（[test-specification-phase2.md](test-specification-phase2.md) §EXP の EXP-01〜05（需要展開）とは同番号の別対象）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| EXP-01 | 参照キーの列挙 | I-01 | `icon-ore`・`icon-part`・`icon-fac` が文書内の出現順で 1 回ずつ |
| EXP-02 | 未設定・プレースホルダ・無効キーの除外 | IconKey に null・`icon-placeholder`・文字種外の値を持つエンティティ | いずれも列挙されない |
| EXP-03 | エントリ生成 | キー `icon-a` と PNG バイト列 | `File` は `icons/icon-a.png`、`Sha256`・`Bytes` は実バイト列からの算出値 |
| EXP-04 | 既定キー提案 | エンティティ Id `i-ore` | `icon-i-ore` |
| EXP-05 | 既定キー提案（無効文字） | エンティティ Id `a b.c` | 無効文字が `-` に置き換わり `IconKeyRules.IsValid` を満たすキー |
| EXP-06 | 既定キー提案（長過ぎ） | 100 文字の Id | 64 文字以内かつ `IsValid` を満たすキー |
| EXP-07 | マニフェスト構築（正常） | I-01 の文書と全ファイルを持つストア | 参照 3 キーのエントリ。Sha256/Bytes は実ファイルから再計算される |
| EXP-08 | 参照キーがマニフェスト未登録 | I-01 から `icon-part` のエントリを除去 | `icon-part` の `Key` エラーが集約され、他キーはエントリとして出る |
| EXP-09 | ファイル実体の欠落 | ストアに `icons/icon-ore.png` がない | `icon-ore` の `File` エラーが集約される |
| EXP-10 | 孤立エントリの除外 | I-01 に未参照キーのエントリを追加 | 出力マニフェストに含まれず、エラーもない |

### ZIP: IconArchive

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| ZIP-01 | 生成→読み取りの往復 | マスタ JSON 文字列と I-01 のマニフェスト・ファイル群 | `TryReadZip` が true、JSON が一致、アイコンが `icons/<名>.png` キーで全件取れる |
| ZIP-02 | zip 内の配置 | 同上 | エントリ名が `data/master.json` と `data/icons/<名>.png` |
| ZIP-03 | `data/` 前置きなしの読み取り | `master.json`・`icons/a.png` のみの zip | `TryReadZip` が true で両方取れる |
| ZIP-04 | 壊れた zip | 非 zip のバイト列 | `TryReadZip` が false |
| ZIP-05 | マスタ JSON なし | アイコンのみの zip | `TryReadZip` が false |

### ADM: AdminDocumentService のアイコン操作

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| ADM-01 | 取り込み（キー未設定） | IconKey 未設定の `i-none` に PNG 登録 | IconKey が `icon-i-none` に補完され、マニフェストにエントリ追加、ストアから読める |
| ADM-02 | 取り込み（既存キー） | IconKey `icon-ore` の `i-ore` に別 PNG 登録 | IconKey 維持、エントリの Sha256/Bytes が新バイト列で更新される |
| ADM-03 | クリア | `i-ore` のアイコンをクリア | IconKey が null。マニフェストエントリは残る（エクスポートで孤立除外） |
| ADM-04 | 実効キー（レシピ） | IconKey なしの `r-part` | 主出力 `i-part` の `icon-part` へフォールバック |
| ADM-05 | 実効キー（自前優先） | IconKey を持つレシピ | 自身のキーが返る |
| ADM-06 | zip エクスポート（正常） | I-01 を読み込み `ExportZip` | 成功。zip は `data/master.json`＋参照分のアイコンを含み、マニフェストの孤立エントリは落ちる |
| ADM-07 | zip エクスポート（アイコン未取得） | 参照キーのファイルがストアにない状態で `ExportZip` | 失敗し `File` エラーを返す。zip は出ない |
| ADM-08 | zip 往復 | ADM-06 の zip を `LoadZip` で読み直し | 読み込み成功し `IconFilesLoaded` がマニフェスト件数に一致 |

### CIV: 同梱データの整合

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| CIV-01 | 整合したリポジトリ | 現行 `data/` | エラーなく終了する |
| CIV-02 | Bytes/Sha256 不一致 | マニフェストの値を改竄 | エラーとして失敗する（CIV-06 で常時担保） |
| CIV-03 | ファイル欠落 | `data/icons/` の実ファイルを除去 | エラーとして失敗する（CIV-06 で常時担保） |
| CIV-04 | 警告のみの状態 | 孤立エントリ・未収録ファイル | 警告出力のみで終了コードは成功 |
| CIV-05 | data/master.json の読み込み | 現行 `data/master.json` | 構文・構造・意味検証を通過（自動テスト BundledMasterDataTests） |
| CIV-06 | Icons マニフェスト↔実ファイル | 現行 `data/` | Verify エラーなし（自動テスト BundledMasterDataTests） |

CIV-02・CIV-03 の違反検出は dotnet test の CIV-06 へ移管した（スクリプトを対象データへ一時変更して実行する手動検証としては CIV-04 のみ残す）。
CIV-05・CIV-06 は dotnet test の `BundledMasterDataTests` として常時実行する。
CIV-01 は CIV-05・CIV-06 が実質包含する（構文・構造・意味検証とマニフェスト↔実ファイル照合の常時実行）。
CIV-04 は `tools/validate_master.py` の警告経路（`icon_warnings`）として CI の build-test ジョブで常時実行されている。
スクリプトはスキーマ適合と警告系の検査のみを担い、失敗系チェック（文字種・File 形式・実在・Bytes/Sha256）は C# 側の規則で担保する。

## 4. 受け入れ条件との対応

- 全項目緑であること。
- EXP・ADM で「エクスポートで `icons/` フォルダとマニフェストを出力する」（Phase 7 チェックリスト）を担保する。
- IMP・ZIP・ADM-08 でエクスポート物のコミット → CI 検証 → Pages 配信に必要な往復整合を担保する。
- アイコン欠落時のプレースホルダ表示は `IconResolver` 側（ICO-05〜08、Phase 3 実装済み）と §5 の手動確認で担保する。

## 5. 手動確認項目（ブラウザプレビュー）

実装のブラウザプレビューをユーザーに操作してもらい、次を確認した。

| ID | 確認内容 | 結果 |
|---|---|---|
| MN-01 | Admin の同梱マスタ読み込みでアイコン取得数（例: 5/5）が表示される | OK（Devin 実機確認） |
| MN-02 | エンティティ編集のアイコン欄で画像選択 → 128×128 正規化プレビューが出る | OK（Devin 実機確認、ブラウザコンソールで正規化関数の出力を確認） |
| MN-03 | クリアで IconKey が空になりプレビューがプレースホルダへ戻る | OK（Devin 実機確認） |
| MN-04 | エクスポートで `master-export.zip` がダウンロードされ、中身が `data/master.json`＋`data/icons/*.png` | OK（Devin 実機確認、ダウンロード zip を展開して確認） |
| MN-05 | 計算プレビューで素材・設備・環境行にアイコンが出る | OK（Devin 実機確認） |
| MN-06 | App でアイコン設定済みエンティティに画像、未設定には `?` プレースホルダが出る | OK（Devin 実機確認、ユーザー確認） |
| MN-07 | IconKey 未設定のレシピ行が主出力アイテムのアイコンを表示する | OK（Devin 実機確認） |
| MN-08 | ユーザー自身による操作確認（ui-mock-first） | OK |

## 6. 備考

- 128×128 正規化は `wwwroot/js/icons.js` の Canvas 処理であり、自動テストの対象外。MN-02 で実機確認済み。
- `data:` URI の `fetch` がブラウザ環境で失敗するため、正規化のデコードは `Image` 要素を使う実装になっている（MN-02 で確認した経路）。
- Admin の読み込み経路別の挙動（URL・同梱は HTTP 取得、.json はストア温存、.zip は両方置き換え）は ADM-08 と §5 で確認する。
