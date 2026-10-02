# Phase 19 実装詳細計画

**対象フェーズ**: Phase 19（アイテム選択のカテゴリ絞り込み全展開）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AS・AT）
**関連ドキュメント**: [test-specification-phase19.md](test-specification-phase19.md)（本 Phase のテスト仕様）

> 本書は Phase 19 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。
> マスタ構成（モデル、スキーマ、JSON）は変更しない。UI の入力補助のみを対象とする。

## 1. スコープ

### やること

すべての素材入力・アイテム検索窓にカテゴリ絞り込みを実装し、カテゴリ欄とアイテム欄を横並びに配置する（仕様決定 AS）。
あわせてアイテム選択肢の母集団を制限する（仕様決定 AT）。

### やらないこと

- マスタ構成の変更。`Item.Category` は引き続き自由文字列とし、`data/master.json`、スキーマ、`MasterValidator` の必須違反は変えない
- 公開 App のテキスト検索コンボの仕組み変更（ドロップダウンのグループ化・カテゴリ見出しなどはしない）
- `RefSelect` を検索コンボ化するような部品の作り替え。ネイティブ select を維持する
- 計算ドメインの変更。母集団制限は UI の選択肢生成のみに適用し、計算への入力経路（手入力の Id 等）は変えない

## 2. 対象一覧

| 場所 | 現状 | 変更 |
|---|---|---|
| 公開 App 生産リスト（`App/Pages/Home.razor` 検索コンボ） | テキスト検索のみ、母集団は `TransportKind != None` | 行の左にカテゴリ select を追加し、候補をカテゴリ＋クエリで絞り込み。母集団をレシピ出力のあるアイテムに制限 |
| Admin 計算プレビュー生産リスト（`Admin/Pages/PreviewPage.razor`） | 全アイテムの `RefSelect` | 行をカテゴリ＋アイテムの横並び化。母集団をレシピ出力のあるアイテムに制限 |
| Admin 環境編集の消費アイテム（`Admin/Pages/EnvironmentsPage.razor`） | 全アイテムの `RefSelect` | 同一フォーム行内で横並び化。母集団を「気体」「液体」カテゴリに制限 |
| Admin マップ編集の採取レート行（`Admin/Pages/MapsPage.razor`） | 天然資源の `RefSelect` | 行を横並び化（母集団は `IsGatherable` のまま） |
| Admin レシピ編集の入力・出力・固定消費（`Admin/Pages/RecipesPage.razor`） | 欄ごと共有の絞り込み select を表の上に配置（Phase 14） | 共有絞り込みを廃止し、行ごとの「カテゴリ＋アイテム」横並びへ置き換え。固定消費の母集団は「気体」「液体」カテゴリに制限 |
| Admin アイテム一覧の検索窓（`Admin/Components/ListPane.razor`、`ItemsPage.razor`） | テキスト絞り込みのみ | `ListPane` に任意のカテゴリ絞り込み select を追加し、ItemsPage で有効化 |

## 3. 確定した UI 上の判断（2026-10-02 確定）

1. カテゴリ select はアイテム選択の左に横並びに置く。Admin 側は 2 つの select を持つ部品として 1 コンポーネント化し、カテゴリの選択状態を内部に持たせる
2. レシピ編集の絞り込みは行ごとに独立して行う。Phase 14 の欄ごと共有フィルタは「すべて横並び」の指示と行ごと独立絞り込みの上位互換性から廃止する（要協議 A で確定）
3. アイテム一覧の検索窓（`ListPane`）にもカテゴリ select を追加する（要協議 B で確定）
4. Phase 14 の確定仕様を継承する（要協議 C で確定）: 選択中の値が絞り込み条件外のとき選択肢の末尾に残す、カテゴリ候補はその欄の母集団からの初出順、空値ラベルは「すべてのカテゴリ」
5. 生産リストの目標候補はレシピの成果物（Outputs）に登場するアイテムのみとする。採取素材でもレシピを持たないものは候補に出さない（仕様決定 AT。当初の棚上げ論点「生産経路のないアイテムを目標選択肢から外すか」をこの形で確定）
6. 環境の消費アイテムと設備ペアの固定消費の候補は「気体」「液体」カテゴリのアイテムのみとする（仕様決定 AT）
7. 既存値が母集団外のときも選択肢の末尾に残す（末尾残し規則を母集団制限にも拡張）
8. 行追加時のカテゴリ既定は「すべてのカテゴリ」。行追加のアイテム既定はその欄の絞り込み後の先頭アイテム（Phase 14 と同じ規則を行ごとに適用）

## 4. 実装構成

### Application 層

母集団制限とカテゴリ絞り込みの判定は Application の純粋関数に置き、razor は結果を部品へ渡すだけにする（remaining-issues.md の UI 状態遷移テスト空白を増やさない方針）。

- `ItemCatalog` を拡張する。XML コメントの「管理ツールの」表記は公開 App も利用するため「アイテム選択補助」に修正する
  - `FuelCategories`: 「気体」「液体」のカテゴリ名リスト（仕様決定 AT）
  - `WithRecipeOutput(items, recipes)`: レシピの `Outputs` に登場するアイテムだけを返す（生産リストの候補、仕様決定 AT）
  - `FuelItems(items)`: `FuelCategories` 一致のアイテムだけを返す（環境消費・固定消費の候補、仕様決定 AT）
  - `OptionsForSelection(items, category, currentItemId, lookupItems = null)`: 引数 `lookupItems` を追加し、現在値の検索先を母集団と分離できるようにする（省略時は従来どおり `items` を使う。母集団外の既存値も末尾に残すため、呼び出し側は文書全アイテムを渡す）
- `ItemSearch.Filter(items, query, category = null)` に任意のカテゴリ引数を追加し、カテゴリ絞り込みとクエリ絞り込みの合成を公開 App でもテスト可能な純粋関数にする

### Admin 側

- `Components/ItemPicker.razor`（仮称）を新設する。カテゴリ select＋アイテム `RefSelect` の横並びペアを 1 部品とし、カテゴリの選択状態を内部に持つ
  - パラメータ: `Items`（母集団）、`LookupItems`（現在値の検索先、省略時は `Items`）、`Value`/`ValueChanged`、`EmptyLabel`
  - カテゴリ候補は `ItemCatalog.Categories(Items)`、アイテム候補は `ItemCatalog.OptionsForSelection(Items, _category, Value, LookupItems)`
  - アイテム側 `RefSelect` は `_category` を `@key` に含めて select を再生成する（Phase 14 の表示化け不具合の対策を踏襲）
- `RecipesPage`: 欄ごとの絞り込みブロック（`.fc-row` の select）を撤去し、各行のアイテム列に `ItemPicker` を置く。入力・出力は全アイテム、固定消費は `FuelItems` の母集団を渡す。`AddInput`/`AddOutput`/`ToggleFixedConsumption` の既定はそれぞれの母集団の先頭
- `MapsPage`: 採取レート行のアイテム列に `ItemPicker`（母集団は `IsGatherable`）を置く。`AddRate` の既定は未使用の天然資源の先頭（現行ロジック維持）
- `EnvironmentsPage`: 消費アイテムのフォーム行に `ItemPicker`（母集団は `FuelItems`）を置く
- `PreviewPage`: 生産リスト行に `ItemPicker`（母集団はレシピ出力のあるアイテム）を置く
- `ListPane`: `Func<TItem, string?>? CategoryOf` パラメータを追加し、指定時は絞り込み入力の横にカテゴリ select を出す。テキスト絞り込みと AND で合成する。`ItemsPage` で `CategoryOf="i => i.Category"` を渡す
- CSS: `.item-picker` 相当の横並びスタイル（flex・gap、カテゴリ select は固定幅）を Admin の `app.css` に追加する

### 公開 App 側

- `Home.razor` の `TargetRow` に `Category` を持たせ、検索コンボの左にカテゴリ select を置く。候補は `ItemSearch.Filter(_selectableItems, row.Filter, row.Category)`
- `_selectableItems` の母集団を `TransportKind != None` かつ `WithRecipeOutput` 一致に制限する（仕様決定 AT）
- 選択済みアイテムはカテゴリ変更で消さない（`ItemId` は保持。コンボはネイティブ select ではないため末尾残しは不要）
- CSS: カテゴリ select を固定幅にし、`.target-row` の既存 flex 構成に追加する

### 文書

- 本書と `test-specification-phase19.md` を作成する
- `implementation-plan.md` に Phase 19 節を追加し、Phase 14・16・17 の未チェック `[ ]` を `[x]` に同期する
- `decision-records.md` に仕様決定 AS・AT を追加する
- `requirements.md` の本文へ AT の母集団制限と AS の UI 規則を反映する

## 5. テスト

[test-specification-phase19.md](test-specification-phase19.md) に従う。
`ItemCatalog`・`ItemSearch` の純粋関数は xUnit で採番してカバーし、razor の UI 状態遷移は残課題のとおり対象外とする。
ブラウザプレビューによるユーザー確認は実装後に別途挟む。

## 6. 受け入れ条件

- すべての素材入力・アイテム検索窓でカテゴリ絞り込みが使え、カテゴリ欄とアイテム欄が横並びになっている
- 生産リストの候補にレシピ出力のないアイテムが出ず、環境消費・固定消費の候補に気体・液体以外のカテゴリが出ない
- 絞り込み中でも、条件外の既存値の行が別アイテム表示に化けない
- `dotnet build` と `dotnet test` が全緑である
