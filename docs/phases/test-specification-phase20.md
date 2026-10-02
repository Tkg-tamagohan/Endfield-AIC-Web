# Phase 20 テスト仕様書

**対象**: Phase 20 成果物（生産リスト行の改修と選択窓・既定値の調整）
**前提ドキュメント**: [implementation-plan-phase20.md](implementation-plan-phase20.md)（§3 の確定判断を含む）

> 本書は Phase 20 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> 従来のテスト項目は Phase 19 以前の仕様書を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit（`tests/EndfieldAicWeb.Application.Tests` の `AdminToolsTests` に追加） |
| 実行コマンド | `dotnet test`、および `~/.venvs/validate/bin/python tools/validate_master.py`（マスタ未変更でも回帰確認として実行） |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | App・Admin を `dotnet run` で起動し、ブラウザプレビューで §3 の項目を確認する（ui-mock-first ルールに従い、ユーザー確認を先に取る） |

## 2. 自動テスト項目

`EntityFactory` の新規既定値を対象とする。既存の ENT-01〜07 は引数渡しのため影響しない。

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| ENT-08 | `versionAdded` 省略時の既定値 | `EntityFactory.NewItem("item-x")` | `VersionAdded` が "1.0.0"（仕様決定 AV） |
| ENT-09 | 新規レシピの初期ペアのサイクル秒 | `EntityFactory.NewRecipe("recipe-x", "1.0.0", "i-a", "f-a")` | 初期ペアの `CycleTime` が 2（仕様決定 AW） |
| ENT-10 | 既定 `VersionAdded` は他種別でも共通 | `NewFacility`・`NewEnvironment`・`NewGameEvent`・`NewGameMap`・`NewRecipe` の `versionAdded` 省略 | 各 `VersionAdded` が "1.0.0"（仕様決定 AV） |

razor の UI 状態遷移（select の切替、行ごとの独立状態）は bUnit 未導入のため対象外とする（remaining-issues.md「Razor ページ内 UI 状態遷移のテスト空白」）。
`dotnet test` は回帰確認として全件実行し、全緑であることを受け入れ条件とする。

## 3. 手動確認項目（ブラウザプレビュー）

実装のブラウザプレビューをユーザーに操作してもらい、次を確認する。
Admin ホームでマスタを読み込み、ナビの各ページと公開 App で確認する。

| ID | 確認内容 |
|---|---|
| MN-62 | 公開 App 生産リストの行がカテゴリ・アイテム・レート・削除の横並びで表示され、個/分のレート欄と × ボタンが見える。アイテム select を開くと候補名が途切れず読める（仕様決定 AU） |
| MN-63 | 公開 App でカテゴリを選ぶとアイテム候補が絞られ、選択中のアイテムが条件外になっても選択肢の末尾に残り別アイテム表示に化けない（AS・AT の継承、Phase 14 MN-34 の横展開） |
| MN-64 | 選択窓の空値ラベルがカテゴリ欄「カテゴリ」・アイテム欄「アイテム」に変わっている（公開 App・Admin 計算プレビュー・各編集ページ・アイテム一覧の ListPane）（仕様決定 AX） |
| MN-65 | 期間入力（日・時・分）と個/分のレート入力の欄が狭くなり、3 桁の入力が問題なく行える（仕様決定 AY） |
| MN-66 | Admin レシピ編集で設備ペアを追加したとき・新規レシピを追加したとき、サイクル秒が 2 になる（仕様決定 AW） |
| MN-67 | Admin の各ページで新規追加したエンティティの `VersionAdded` が "1.0.0" になる（仕様決定 AV） |
| MN-68 | 生産リスト（公開 App・Admin 計算プレビュー）の新規行のレート既定が 30 になる（仕様決定 AZ） |

## 4. 受け入れ条件との対応

- `dotnet test` 全緑、`validate_master.py` 通過: §2 の回帰確認で担保
- 生産リスト行の横並び表示・選択窓ラベル・欄幅・既定値の動作: §3 のユーザー確認で担保
