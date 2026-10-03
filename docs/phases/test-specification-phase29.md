# Phase 29 テスト仕様

**対象フェーズ**: Phase 29（レシピの Id・名前の自動入力）
**前提ドキュメント**: [implementation-plan-phase29.md](implementation-plan-phase29.md)、[decision-records.md](../decision-records.md)（仕様決定 BU。関連: AV・AW・BN）
**関連ドキュメント**: [test-specification-phase6.md](test-specification-phase6.md)（IDF・ENT の採番・新規既定の先行群）、[test-specification-phase20.md](test-specification-phase20.md)（Admin 新規既定値の先行群）

> 本書は Phase 29 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 自動テストは IDF（`EntityFactory` の採番系）に連番で追加し、提案状態遷移は新接頭辞 RCP とする。手動項目は MN-122〜を継続使用する。

## 1. モデル検査（xUnit）

### スラッグ採番（IDF）: AdminToolsTests

| ID | 内容 |
|---|---|
| IDF-06 | `SuggestRecipeId` は ItemId 先頭の `item-` を除いた slug で `recipe-<slug>` を返す（`item-xiranite` → `recipe-xiranite`） |
| IDF-07 | `recipe-<slug>` が衝突するとき `<slug>01` から 2 桁連番で最初の空きを返す（`recipe-carbon` 占有時 → `recipe-carbon01`） |
| IDF-08 | 連番の途中に欠番があれば最初の空きを埋める（`recipe-carbon`・`recipe-carbon01`・`recipe-carbon03` 占有時 → `recipe-carbon02`） |
| IDF-09 | `item-` で始まらない ItemId は全体を slug に使う（`x-foo` → `recipe-x-foo`） |

### 提案状態遷移（RCP）: AdminToolsTests または新規ファイル

| ID | 内容 |
|---|---|
| RCP-01 | 提案値は名前=主産物アイテム名・Id=`recipe-<slug>`。`Outputs` の `SortOrder` 最小行を主産物とし、同率は先頭行を取る |
| RCP-02 | 直近提案値と一致する状態で主産物が変わると、Id・名前とも新しい提案へ追随する |
| RCP-03 | 名前を手動編集したあとは名前だけ追随せず、Id は追随を続ける（フィールドごとに独立して固定される） |
| RCP-04 | 手動で提案値と同じ値を入れ直すと、そのフィールドは追随へ復帰する |
| RCP-05 | 提案適用（ボタン相当の操作）で、手動編集後の Id・名前が両方とも現在の提案値へ戻る |
| RCP-06 | `Outputs` が空、または主産物の `ItemId` が文書にないときは提案を計算せず、Id・名前は現在値を維持する |
| RCP-07 | Id 採番の衝突判定は編集中レシピ自身の Id を除外する（`recipe-cuprium` が `recipe-cuprium` のまま追随対象になる） |
| RCP-08 | `SortOrder` の変更や行の追加削除で主産物が入れ替わった場合も追随判定の対象になる |
| RCP-09 | 選択中レシピの切替で直近提案値が現在の提案値へ初期化される（別レシピの提案値を引きずらない） |
| RCP-10 | 既存レシピの主産物を、別の既存レシピと同じアイテムへ変更すると、追随した Id が `<slug>NN` の連番になる（編集経路の衝突採番） |

### 回帰（据置）

| ID | 内容 |
|---|---|
| IDF-01〜05 | 据置。`prefix-NNN` 採番は他種別で継続使用のため変更しない |
| ENT-01〜10 | 据置。`EntityFactory` の新規既定値（VersionAdded・CycleTime=2）は変更しない |

## 2. 手動確認項目（ブラウザプレビュー）

Admin でマスタを読み込み、レシピ編集ページで確認する。実装のブラウザプレビューをユーザーに操作してもらい、確認後に実施する。

| ID | 確認内容 |
|---|---|
| MN-122 | 新規レシピ追加で Id が `recipe-<slug>`・名前が先頭アイテム名になり、出力アイテムを変更すると Id・名前が追随する |
| MN-123 | Id または名前を手動編集するとその欄は追随を止め、提案ボタンで両欄が提案値へ戻る |
| MN-124 | 追随による Id 変更で設備ペアの `RecipeId` も更新され、検証で `RecipeId` 不一致の違反が出ない |
| MN-125 | 規約どおりの Id・名前を持つ既存レシピ（例: `recipe-cuprium`）で主産物を変えると追随し、提案値と一致しない Id のレシピ（例: `recipe-xiranite02`）は Id が固定のままになる |
| MN-126 | 既存レシピの主産物を、別の既存レシピと同じアイテムへ変更すると Id が `<slug>NN` に連番化され、設備ペアの `RecipeId` も新 Id へ伝搬する |

## 3. 受け入れ条件との対応

- MN-122〜126 で Phase 29 計画 §6 の受け入れ条件をすべてカバーする
- `dotnet build`・`dotnet test` 全緑と、同梱マスタの `tools/validate_master.py` 通過（データ未変更の回帰確認）を併せて確認する
