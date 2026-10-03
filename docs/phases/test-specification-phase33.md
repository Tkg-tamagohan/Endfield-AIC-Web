# Phase 33 テスト仕様

**対象フェーズ**: Phase 33（警告・エラー表示の識別子を「名前（Id）」表記へ統一）
**前提ドキュメント**: [implementation-plan-phase33.md](implementation-plan-phase33.md)、[decision-records.md](../decision-records.md)（仕様決定 CF・CG）
**関連ドキュメント**: [test-specification-phase2.md](test-specification-phase2.md)（WRN 系列の先行群）、[test-specification-phase31.md](test-specification-phase31.md)（VAL 採番の先行群）

> 本書は Phase 33 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 警告系は `WarningTests.cs` の WRN- 連番を継続（WRN-09〜。Phase 30 が WRN-07・08 を予約済み。着手時に現行最大を再確認）。検証系は `ValidationTests.cs` の VAL- 連番を継続（VAL-29〜。Phase 31 が VAL-22〜28 を使用済み）。`CalculationInputBuilderTests.cs` は ID を持たないため新接頭辞 CIB- を設ける。手動確認は MN- 連番を継続（MN-131〜。Phase 31 が MN-128・129 を使用済み、MN-130 は Phase 32 候補として空ける）。廃止・改訂する既存 ID は「廃止」「改訂」と記す。

## 1. モデル検査（xUnit）

### 追加する回帰ケース（WRN）: WarningTests

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| WRN-09 | 警告文面のアイテム参照が `名前（Id）` で表示される | フィクスチャで `Name` を Id と別値にしたアイテムをレシピなし需要にする | `NoRecipeAvailable` の Message が `名前（Id）` を含み、種別語「アイテム」が残る |
| WRN-10 | 存在しない参照は Id のみで表示される | `EnvironmentCountOverride` が存在しない環境 Id を指す（WRN-05 の派生） | `InvalidEnvironmentOverride` の Message がその Id をそのまま含み、`（` を含まない |
| WRN-11 | 循環パスの各要素が名前表記になる | F-04 相当の循環フィクスチャで `Name` を Id と別値にする | `CycleDetected` の Message のパスが `名前（Id） → 名前（Id）` 形になる |
| WRN-12 | イベント名が `名前（Id）` で解決される | `GameEventsById` を持つスナップショットで非有効イベント限定アイテムを需要にする | `EventItemUnavailable` の Message がイベントの `名前（Id）` を含む |

### 追加する回帰ケース（VAL）: ValidationTests

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| VAL-29 | 参照不存在エラーの参照値は入力 Id のまま（存在しないためフォールバック） | `ValidateAll` で `Inputs` が存在しないアイテムを指すレシピ | Message がその Id を含む（従来アサーションと不変） |
| VAL-30 | 存在するエンティティの参照は `名前（Id）` で表示される | `ValidateAll` で「採取素材でないアイテムを GatherRates が参照」のように実在エンティティを指す違反 | Message が `名前（Id）` を含む |
| VAL-31 | 検証エラー行の EntityId 表示解決 | エラー一覧の表示用解決（EntityKind+EntityId → 名前）をヘルパー経由で行う単体検査 | 存在する EntityId は `名前（Id）`、存在しない Id はそのまま返る |

### 追加する回帰ケース（CIB、新接頭辞）: CalculationInputBuilderTests

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| CIB-01 | 数量エラーが `名前（Id）` を含む | アイテム行の数量に非数値を入力 | エラー文が `{item.Name}（{item.Id}） の数量を…` 形 |
| CIB-02 | 採取レートエラーが `名前（Id）` を含む | 採取素材でないアイテムへ採取レート上書き | エラー文が `{name}（{id}） は採取素材ではありません` 形 |

### 改訂するケース

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| （実装時に走査） | 「Id そのまま」を仮定する文面アサーション（完全一致・前方一致など）は新表記へ追従させる。`Contains("i-xxx")` 型は `名前（Id）` が Id を含むため大半は据え置き | — | — |

## 2. 手動確認項目

| ID | 内容 | 手順 | 期待 |
|---|---|---|---|
| MN-131 | 公開アプリの警告欄が `名前（Id）` で表示される | 公開アプリでペア上書き等の警告が出る構成を計算する（または開発者ツールで警告を発火させる） | 警告文に生の Id が出ず、`名前（Id）` 表記になる |
| MN-132 | 存在しない参照は Id のみ表示される | 管理ツールで GameEvent の Id を改名（または削除）して保存し、改名前の Id を `GameEventId` に持つアイテムを目標に計算する（未登録参照は通常の入力欄から指定できないため、改名による注入経路とする） | `EventItemUnavailable` の警告が旧イベント Id をそのまま表示し、`（` を含まない |
| MN-133 | 管理ツールの検証一覧が `名前（Id）` で表示される | 管理ツールで参照不存在等の違反を作って検証を実行し、一覧と文面を確認する | `EntityKind` の後が `名前（Id）`、存在しない参照は Id のみ。一覧からの遷移は従来どおり動く |
