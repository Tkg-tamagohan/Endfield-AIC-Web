# Phase 20 実装詳細計画

**対象フェーズ**: Phase 20（生産リスト行の改修と選択窓・既定値の調整）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AU〜AZ）
**関連ドキュメント**: [test-specification-phase20.md](test-specification-phase20.md)（本 Phase のテスト仕様）

> 本書は Phase 20 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。
> マスタ構成（モデル、スキーマ、JSON）は変更しない。UI の表示・既定値のみを対象とする。

## 1. スコープ

### やること

- 公開アプリ生産リストの行レイアウトを改修し、個/分のレート入力が常に見えるようにする（仕様決定 AU）
- 選択窓の空値ラベルを「カテゴリ」「アイテム」に改める（仕様決定 AX）
- 期間入力・レート入力の欄幅を 3 桁余裕へ縮小する（仕様決定 AY）
- 管理ツールの既定値を見直す: 新規エンティティの `VersionAdded` を 1.0.0（仕様決定 AV）、設備ペアのサイクル秒を 2（仕様決定 AW）、生産リストの既定レートを 30 個/分（仕様決定 AZ）

### やらないこと

- 管理ツール計算プレビューの構造変更。すでに理想形（カテゴリ＋アイテム＋レート＋削除の横並び）のため、本 Phase ではラベル・既定値のみ触る
- マスタ構成・計算ドメイン・検証ルールの変更
- アイテム以外の選択欄（設備・環境・マップ等）の空値ラベル変更。「（未選択）」のままとする（AX の対象外）
- 管理ツール CSS に残っている使われなくなった `.combo` 系スタイルの整理（Phase 19 残課題。本 Phase は公開 App 側のみ片づける）

## 2. 変更対象一覧

| 場所 | 現状 | 変更 |
|---|---|---|
| 公開 App 生産リスト行（`App/Pages/Home.razor`） | カテゴリ select＋テキスト検索コンボ＋レート入力＋削除ボタン。コンボ内 input が固有幅を保持して行が溢れ、レート欄と × が右にはみ出して見えない。ドロップダウンは「?」アイコン＋名前右切れで崩れて見える | テキスト検索コンボを廃止し、ネイティブのアイテム select に置き換える。管理ツール計算プレビューと同型の横並び（カテゴリ＋アイテム＋レート＋削除）とする（AU） |
| 空値ラベル | カテゴリ「すべてのカテゴリ」、アイテム「（アイテムを選択）」・「（未選択）」 | 「カテゴリ」「アイテム」に改める（AX）。対象は `ItemPicker`・`ListPane`・`Home.razor`・`PreviewPage` |
| 期間・レート入力欄 | 期間欄 56px・レート欄 84px | 3 桁の入力に余裕がある程度へ縮小（AY）。App・Admin 両方の CSS |
| 新規エンティティの `VersionAdded` | `Store.Document.DataVersion` を継承 | `EntityFactory` の既定値 "1.0.0" を全種別で使う（AV） |
| 設備ペアの新規 `CycleTime` | 4 | 2 に改める（AW）。`RecipesPage.AddPair` と `EntityFactory.NewRecipe` の初期ペア |
| 生産リスト行の既定レート | "60" | "30" に改める（AZ）。`Home.razor` と `PreviewPage` |

## 3. 確定した UI 上の判断（2026-10-02 確定）

1. 公開 App のアイテム選択は検索コンボを廃止し、管理ツール計算プレビューと同型のネイティブ select ペア（カテゴリ＋アイテム）に置き換える（要協議 A）。母集団制限（AT）で候補が少数になったためテキスト検索の利点がなく、全選択窓をネイティブ select に統一する
2. アイテム側 select はカテゴリの絞り込み条件を `@key` に含めて select を再生成させる（Phase 14 からの表示化け対策を踏襲）
3. 選択中のアイテムが絞り込み・母集団の条件外のとき、選択肢の末尾に現在値を残す（AS・AT の規則を継承）。呼び出しは `ItemCatalog.OptionsForSelection` を使う
4. `VersionAdded` 既定 "1.0.0" はアイテムに限らず設備・環境・イベント・マップ・レシピの新規追加にも適用する（要協議 B）
5. 「時間記述欄を狭くする」は期間入力（日・時・分）と個/分のレート入力の双方に適用する（要協議 C）

## 4. 実装構成

### Application 層

- `EntityFactory` に `public const string DefaultVersionAdded = "1.0.0"` を追加し、各 `New*` メソッドの `versionAdded` 引数を任意引数（既定値 `DefaultVersionAdded`）にする（AV）
- `EntityFactory.NewRecipe` の初期ペア `CycleTime` を 2 にする（AW）

### Admin 側

- `Components/ItemPicker.razor`: `EmptyLabel` パラメータの既定値を「（未選択）」→「アイテム」に、カテゴリ `RefSelect` の `EmptyLabel` を「すべてのカテゴリ」→「カテゴリ」に改める
- `Components/ListPane.razor`: カテゴリ option の空値ラベルを「すべてのカテゴリ」→「カテゴリ」に改める
- `Pages/PreviewPage.razor`: `ItemPicker` へ渡す `EmptyLabel="（アイテムを選択）"` を外し既定値に任せる。行の `RateText` 既定を "30" に改める
- `Pages/RecipesPage.razor`: `AddPair` の `CycleTime` を 2 に改める
- 各ページ（`ItemsPage`・`FacilitiesPage`・`EnvironmentsPage`・`EventsPage`・`MapsPage`・`RecipesPage`）: `EntityFactory` 呼び出しから `DataVersion` 引数を外し、既定値 "1.0.0" を使う
- CSS（`Admin/wwwroot/css/app.css`）: `.period-inputs .input` を 56px → 44px、`.input.rate` を 84px → 56px に縮小

### 公開 App 側

- `Home.razor`: 生産リスト行のアイテム選択をネイティブ select に置き換える
  - `TargetRow` から `Filter`・`Open` を削除し、検索コンボの状態を持たない構成にする
  - アイテム select は `ItemCatalog.OptionsForSelection(_selectableItems, row.Category, row.ItemId, _snapshot.Items)` で候補を作り、空値 option に「アイテム」を置く。カテゴリを `@key` に含める
  - `OnItemFilterInput`・`SelectItem` を select 用の `OnItemChanged` に置き換える
  - カテゴリ option の空値ラベルを「カテゴリ」に、行の `RateText` 既定を "30" に改める
  - 検索コンボ化で使っていた `CalculationInputBuilder.ShouldDeselectItem` の呼び出しは廃止される（利用箇所が残れば見直す）
- CSS（`App/wwwroot/css/app.css`）: `.period-inputs .input` を 56px → 44px、`.input.rate` を 84px → 56px に縮小。使用箇所がなくなった `.combo`・`.combo-list`・`.combo-item`・`.combo-item:hover` を削除し、行の横並びは `.target-row` と `.item-picker` 相当の規則で揃える

### 文書

- 本書と `test-specification-phase20.md` を作成する
- `implementation-plan.md` に Phase 20 節を追加する
- `decision-records.md` に仕様決定 AU〜AZ を追加する
- `requirements.md` の本文へ AU・AX の選択窓仕様と AV・AW・AY・AZ の既定値を反映する

## 5. テスト

[test-specification-phase20.md](test-specification-phase20.md) に従う。
`EntityFactory` の既定値は xUnit で採番してカバーし、razor の UI 状態遷移は残課題のとおり対象外とする。
ブラウザプレビューによるユーザー確認は実装後に別途挟む。

## 6. 受け入れ条件

- 公開 App の生産リスト行が管理ツール計算プレビューと同型の横並びで表示され、カテゴリ・アイテム・レート・削除がすべて見える
- アイテム選択窓の空値ラベルが「カテゴリ」「アイテム」になり、期間・レート入力欄が 3 桁余裕の幅になる
- 新規追加した各エンティティの `VersionAdded` が "1.0.0"、新規設備ペアのサイクル秒が 2、生産リスト新規行のレートが 30 になる
- `dotnet build` と `dotnet test` が全緑である
