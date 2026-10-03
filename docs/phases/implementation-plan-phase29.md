# Phase 29 実装詳細計画

**対象フェーズ**: Phase 29（レシピの Id・名前の自動入力）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 BU。関連: AV・AW・BN）
**関連ドキュメント**: [test-specification-phase29.md](test-specification-phase29.md)（本 Phase のテスト仕様）

> 本書は Phase 29 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

- 管理ツールのレシピ編集で、Id と名前を主産物から自動提案する（仕様決定 BU）
- 提案値への自動追随を実装する。値が直近の提案値と一致する間は主産物の変更に追随し、手動編集で固定する
- 手動編集後にも提案値を適用できる「提案」ボタンを設ける
- レシピの新規 Id 採番を `recipe-NNN` からスラッグ採番（`recipe-<slug>`・衝突時 2 桁連番）へ置き換える

### やらないこと

- 他種別（Item・Facility・Environment・GameEvent・GameMap）の Id 採番・名前既定値の変更。`EntityFactory.SuggestId` と `prefix-NNN` 採番は残す
- 同梱マスタ（`data/master.json`）の既存レシピの Id・名前の変更。自動提案は編集画面の入力補助であり、保存済みデータを書き換えない
- `Description`・アイコン・`VersionAdded` など他フィールドの自動化
- `CommonFieldsEditor`（種別共通の共通属性エディタ）の変更。提案ボタンはレシピ固有の欄に置く

## 2. 変更一覧

### Application

| ファイル | 変更 |
|---|---|
| `EntityFactory.cs` | スラッグ採番 `SuggestRecipeId` と、主産物アイテム名を返す名前提案を追加する |
| `RecipeAutoFill.cs`（新規） | 直近の提案値を保持し、追随・固定・適用の状態遷移を行う純粋クラスを追加する |

### Admin

| ファイル | 変更 |
|---|---|
| `Pages/RecipesPage.razor` | 提案状態の保持、主産物変更時の追随呼び出し、提案ボタンの配置を行う。新規レシピ生成 `CreateRecipe` で作成直後の提案適用を行う |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | BU（本計画で追加済み） |
| `docs/requirements.md` | §5.5 への反映（済） |
| `docs/implementation-plan.md` | Phase 29 行（済）。実装 PR でチェックを `[x]` にする |

### テスト

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Application.Tests/AdminToolsTests.cs` または新規ファイル | スラッグ採番（IDF-06〜）と提案状態遷移（RCP-01〜）のテストを追加する |

## 3. 変更詳細

### 3-1. 提案値の規則（BU）

- **主産物**：`Outputs` のうち `SortOrder` 最小の行（同率は先頭行）を主産物とする。`SortOrder=0` が存在しないデータでも最小行を主産物とみなす基準は、既存の主出力判定（`IconKeyFallback` のフォールバック元）と同じである。`Outputs` が空、または主産物の `ItemId` が文書のアイテムに存在しないときは提案を計算せず、現在値を維持する
- **名前**：主産物アイテムの `Name` とする。重複名は従来どおり許容し、識別は Description の併記（BN）で行う
- **Id**：`recipe-<slug>` とする。slug は ItemId 先頭の `item-` を除いた部分で、`item-` で始まらない ItemId は全体を使う。既存レシピとの衝突時のみ `<slug>` に 2 桁連番（01 から）を付け、最初の空きを採番する。編集中レシピ自身の Id は衝突判定から除外する（既存の `recipe-cuprium` が `recipe-cuprium01` へずれないため）
- 提案計算・採番は Application の純粋関数とし、`EntityFactory` 系の IDF/ENT 群と同じ検査経路に乗せる

### 3-2. 追随と固定の状態遷移

Id・名前それぞれについて「直近に適用した提案値」を保持し、フィールドの現在値がそれと一致する間だけ新しい提案へ更新する。手動編集で一致しなくなった時点で固定になり、手動で提案値と同じ値を入れ直した場合は追随へ復帰する。選択中レシピの切替時に直近提案値を現在の提案値で初期化する。

- 追随の発火点は、出力アイテムの変更（`SetOutputItem`）・`SortOrder` の変更（`SetOutputSortOrder`）・出力行の追加削除（`AddOutput`・`RemoveOutput`）で、これらの操作後に主産物を再解決して追随判定を行う
- Id の自動変更後はペアの `RecipeId` 伝搬（既存の `PropagateRecipeId` と同じ処理）を行う
- 提案値の追跡と状態遷移は Application の純粋クラス（`RecipeAutoFill`）に置き、razor の `@code` には呼び出しだけを残す（remaining-issues.md「Razor ページ内 UI 状態遷移のテスト空白」をこれ以上広げない）

### 3-3. 提案ボタン

- Id・名前を現在の提案値へ戻すボタンをレシピ固有の欄（`EntityFields` フラグメント先頭付近）に置く。文言・配置は実装時に確定し、ブラウザプレビューで確認する
- 適用後は両フィールドが提案値と一致するため、追随状態へ復帰する

### 3-4. 新規作成時の提案適用

- `CreateRecipe`（`EntityFactory.NewRecipe` の呼び出し側）で、生成直後に提案値を適用する。レイアウト側の仮採番 `recipe-NNN` は捨て、`recipe-<slug>` と主産物アイテム名を初期値とする（新規作成の仮主産物は Items 先頭の既存規則のまま）
- 初期ペアの `RecipeId` も適用後の Id に合わせる（`PropagateRecipeId` と同じ伝搬）
- これにより `EntityEditLayout` の変更は不要になる

## 4. 暫定解釈と確定した UI 上の判断

実装中に変更する場合は本節を更新する。

1. 既存レシピでも Id・名前が提案値と一致する限り追随対象とする（BU）。同梱マスタでは `recipe-cuprium` など規約どおりの Id・名前が該当し、主産物を変更すると Id も追随して変わる。`recipe-xiranite02` のように提案値と一致しない Id は追随せず固定のままになる
2. 追随判定は「現在値 == 直近提案値」の比較だけで行い、編集履歴の別フラグは持たない。手動で提案値と同じ値を入れると追随へ戻る（値が同じなので区別できず、戻っても実害がない）
3. 提案ボタンは Id・名前の両方を一度に提案値へ戻す単一ボタンとする（フィールド別のボタンは設けない）

## 5. テスト

[test-specification-phase29.md](test-specification-phase29.md) に従う。
スラッグ採番と提案状態遷移は Application の xUnit で検査し、`dotnet test` 全緑を確認する。
UI の追随・固定・提案ボタン・RecipeId 伝搬はブラウザ E2E で確認する。
ブラウザプレビューによるユーザー確認は実装後・手動検査項目の実施前に挟み、フィードバックを検査へ反映する（ui-mock-first ルール、implementation-plan.md §5「UI の確認」に従う）。

## 6. 受け入れ条件

- 新規レシピで出力アイテムを選ぶと Id・名前が主産物へ追随し、手動編集で固定される
- 提案ボタンで手動編集後も Id・名前を提案値へ戻せる
- 同一主産物のレシピ追加で Id が衝突せず 2 桁連番が付く
- Id の自動変更でペアの `RecipeId` が伝搬し、検証違反が出ない
- `dotnet build` と `dotnet test` が全緑である
