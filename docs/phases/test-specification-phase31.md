# Phase 31 テスト仕様

**対象フェーズ**: Phase 31（ID 系値の空白禁止と読み込み時正規化）（仮採番）
**前提ドキュメント**: [implementation-plan-phase31.md](implementation-plan-phase31.md)、[decision-records.md](../decision-records.md)（仕様決定 BV〜BX）
**関連ドキュメント**: [test-specification-phase26.md](test-specification-phase26.md)・[test-specification-phase28.md](test-specification-phase28.md)（VAL・MJS・MN 採番の先行群）

> 本書は Phase 31 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 検証系は VAL-22〜（Domain 側は ValidationTests.cs、ロード経路は MasterJsonLoaderTests.cs）、スキーマ検査は MJS-12、手動確認は MN-127〜。着手時に各群の現行最大を再確認すること。

## 1. モデル検査（xUnit）

### ID 系値の空白禁止（BV）: ValidationTests.cs

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| VAL-22 | 空白を含むエンティティ Id は違反 | `Item.Id` に前後空白入り（`" item-a "`）・内部空白入り（`"item a"`）の値をそれぞれ設定 | いずれも `Id` の空白違反。空白なしは従来どおり通過 |
| VAL-23 | 参照 Id 値の空白は違反 | `Environment.ProviderFacilityId`・`ConsumeItemId`・レシピ `Inputs[].ItemId`・`Outputs[].ItemId`・ペア `FacilityId`・ペア `EnvironmentId`・`FixedConsumption.ItemId`・`GatherRates[].ItemId`・`GameEventId` に空白入りの値 | 各フィールドで空白違反になる |
| VAL-24 | 空白のみの値は必須違反（境界） | `Item.Id`・`RecipeInput.ItemId` に空白のみの値 | 従来どおり必須違反が出る（空白違反との同時発火は許容） |

### 読み込み時の正規化（BW）: MasterJsonLoaderTests.cs

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| VAL-25 | 前後空白入りの Id・参照値は正規化される | `TestJson.Mutate` 等で、設備 `" fac-x "` とその参照 `" fac-x "` を含む妥当な JSON | Load 成功。実体化後の Id・参照値が揃って `fac-x` になり、`Normalizations` に箇所が記録される |
| VAL-26 | トリム後の Id 重複は一意性違反 | `fac-a` と `fac-a ` の 2 設備を含む JSON | Load 失敗。`Facilities` の重複違反が出る |
| VAL-27 | null 許容参照値の空白のみ値は未指定へ正規化 | `GameEventId` に `" "`・ペア `EnvironmentId` に `" "` | null 扱いとなり違反は出ず Load 成功 |
| VAL-28 | 内部空白は正規化されず違反として残る | 設備 Id に `"fac 1"` を含む JSON | Load 失敗。`Id` の空白違反が出る |

### スキーマ検査（BV）: MasterJsonScaffoldTests.cs

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| MJS-12 | Id 系値フィールドが空白禁止 pattern を持つ | `data/master.schema.json` の `commonAttributes.Id` と各参照 Id フィールド | `idValue`（または同等の `pattern`）が適用されている |
| MJS-13 | 空白禁止 pattern が実際に空白入り値を拒否する | スキーマから pattern を抽出して ` fac-x `・`fac 1`・`fac-x\n`・`fac-x` へ適用 | 空白入りの 3 値を拒否し `fac-x` を受理する（負の検証経路が CI でも機能することの確認） |

## 2. 手動確認項目（MN）

| ID | 内容 | 手順 | 期待 |
|---|---|---|---|
| MN-127 | Id 欄の確定時トリム（BX） | 管理ツールで設備の Id 欄に ` fac-x ` を入力して確定 | 保存値が `fac-x` になり、検証に空白違反が出ない |
| MN-128 | 空白入り JSON の読み込み通知（BW） | 前後空白入りの Id を含む JSON をローカルファイルから読み込み | 読み込みに成功し、読み込みパネルに除去箇所の通知が一覧表示される |

手動確認は管理ツール（`dotnet run --project src/EndfieldAicWeb.Admin`）で行う。公開アプリ側に UI 差分はない。
