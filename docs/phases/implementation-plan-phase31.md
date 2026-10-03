# Phase 31 実装詳細計画

**対象フェーズ**: Phase 31（ID 系値の空白禁止と読み込み時正規化）（仮採番）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 BU〜BW）
**関連ドキュメント**: [test-specification-phase31.md](test-specification-phase31.md)（本 Phase のテスト仕様）、[remaining-issues.md](../remaining-issues.md)（「管理ツール側の登録データの ID 修正」項目）

> 本書は Phase 31 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。
> Phase 番号は仮採番であり、Phase 29・30 に別件が割り当たった場合は採番を振り直す。

## 1. スコープ

### やること

- エンティティの Id と参照 Id 値に「空白文字を含まない」の規則を追加し、`MasterValidator` の検証違反とする（仕様決定 BU）
- `master.schema.json` に空白禁止の `pattern` を記載し、CI の validate_master.py でも検出可能にする（BU）
- JSON 読み込みで ID 系値の前後空白を一貫して除去する正規化を読み込み経路に追加し、除去箇所を管理ツールの読み込み画面へ通知する（BV）
- 管理ツールの共通属性編集で Id 欄を確定時トリムする（BW）
- `remaining-issues.md` の「管理ツール側の登録データの ID 修正」項目を、導入後の状態に合わせて更新する

### やらないこと

- ID の文字種制限（slug 化）は導入しない。空白の禁止のみとする
- Id 変更時の参照一括更新（rename 機能）は含めない。綴り違いは従来どおり手修正とし、残存参照は検証で捕捉する
- `IconKey` は既存の文字種規則を維持し、正規化・トリムの対象外とする（空白入りは従来どおり違反）
- `Name`・`Description`・`VersionAdded` 等の ID 系値でない文字列は対象外とする
- 公開アプリの UI への変更は行わない

## 2. 用語

- **ID 系値**: エンティティの Id と、他エンティティを Id で参照する値の総称。対象は各エンティティの `Id`（Item・Facility・Environment・GameEvent・Recipe・GameMap）、`ProviderFacilityId`、`ConsumeItemId`、`GameEventId`（Item・Environment・Recipe・GameMap の各保持分）、レシピの `Inputs[].ItemId`・`Outputs[].ItemId`・`Facilities[].FacilityId`・`Facilities[].EnvironmentId`・`Facilities[].FixedConsumption.ItemId`、`GameMap.GatherRates[].ItemId` とする。DTO ではいずれも `Id` または `*Id` のプロパティ名を持つ。`Icons[].Key`（IconKey）は別規則があるため含めない
- **空白文字**: `char.IsWhiteSpace` が true を返す文字（半角スペース・タブ・改行・全角スペース等）
- **前後空白**: 値の先頭または末尾に連続する空白文字。それ以外に挟まる空白は **内部空白** と呼ぶ

## 3. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.cs` | 共通ヘルパー `RequireNoWhitespace`（仮称）を追加し、ID 系値の各検査箇所へ適用する |

### Infrastructure

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.Infrastructure/Transfer/MasterJsonReader.cs` | `NormalizeIdValues`（仮称）を追加し、DTO の ID 系値をトリムしつつ除去箇所を記録する |
| `src/EndfieldAicWeb.Infrastructure/Transfer/MasterJsonLoader.cs` | `Parse` と `ValidateStructure` の間で `NormalizeIdValues` を呼び、`MasterJsonLoadResult` に `Normalizations` を追加する |

### Admin

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.Admin/Components/CommonFieldsEditor.razor` | Id 欄の `Apply` で `Trim()` してから代入する |
| `src/EndfieldAicWeb.Admin/Services/AdminDocumentService.cs` | 読み込み通知 `LoadNotes`（仮称）プロパティを追加し、`LoadJson` で `result.Normalizations` を保持する |
| `src/EndfieldAicWeb.Admin/Pages/Home.razor` | 読み込みパネルに `LoadNotes` の一覧を表示する |

### スキーマ

| ファイル | 変更 |
|---|---|
| `data/master.schema.json` | `$defs` に `idValue` を新設し、ID 系値フィールドへ適用する（4-4 参照）。validate_master.py の変更は不要（`pattern` 検証で検出される） |

### テスト

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/ValidationTests.cs` | VAL-22〜24 を追加する |
| `tests/EndfieldAicWeb.Infrastructure/Transfer`（`MasterJsonLoaderTests.cs`） | VAL-25〜28 を追加する |
| `tests/EndfieldAicWeb.Infrastructure.Tests/MasterJsonScaffoldTests.cs` | MJS-12 を追加する |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | 仕様決定 BU・BV・BW を追加する（計画 PR で先行して追加済み） |
| `docs/requirements.md` | §5.2 共通属性・§5.10 マスタ文書へ規則を反映する（計画 PR で先行して反映済み） |
| `docs/implementation-plan.md` | Phase 31 の節を追加する（計画 PR で追加済み） |
| `docs/remaining-issues.md` | 「管理ツール側の登録データの ID 修正」項目を実施後の状態に合わせて更新する |

## 4. 変更詳細

### 4-1. 検証規則（BU）

`MasterValidator` に `RequireNoWhitespace`（仮称）を追加する。値が非空かつ空白文字を 1 文字でも含むとき違反とする。メッセージは `"{field} には空白を含めないでください: {value}"` とする（IconKey 違反の形式に揃える）。

適用箇所は次のとおり。

- `CheckCommonFields` の `entity.Id`（`RequireNonEmpty` の直後）
- `ValidateEnvironment` の `ProviderFacilityId`・`ConsumeItemId`
- `ValidateRecipeItems` の `ItemId`
- `ValidatePairs` の `FacilityId`・`EnvironmentId`（null でないとき）・`FixedConsumption.ItemId`
- `ValidateGameMap`・`ValidateGameMapInContext` の `GatherRates[].ItemId`
- `CheckGameEventRef` の `GameEventId`（非空のとき）

空白のみの値は従来どおり必須違反（`IsNullOrWhiteSpace`）とし、新規則は空白を含む非空の値に適用する。

### 4-2. 読み込み時の正規化（BV）

`MasterJsonReader` に `NormalizeIdValues`（仮称）を追加し、`MasterJsonLoader.Load` で `Parse` と `ValidateStructure` の間に呼ぶ。`MasterJsonDocument` は型付き DTO のため、各エンティティリストを走査して対象プロパティへトリム後の値を代入するだけでよい。

規則は次のとおり。

- 必須の ID 系値（各 `Id`・`ProviderFacilityId`・`ConsumeItemId`・`FacilityId`・各 `ItemId`）は `Trim()` するのみとし、空になっても null にしない（後段の必須違反に委ねる）
- null 許容の参照値（`GameEventId`・ペアの `EnvironmentId`）は `Trim()` して空なら null とする
- 内部空白は `Trim()` の対象外のため残り、BU の違反として後段の検証で捕捉される
- `IconKey`・`Name`・`Description`・`VersionAdded` は対象外

除去が発生した値は `{ロケーション}: 「{正規化後の値}」` 形式の文字列として `normalizations` に記録する。ロケーションは `RequireField` が使う形式に揃え、`Facilities[2].Id` 等とする。`MasterJsonLoadResult` に `Normalizations`（`IReadOnlyList<string>`、既定空）を追加して返す。

Id と参照値の双方へ同じ規則で適用するため、`fac-moulding ` のエンティティと参照が揃って `fac-moulding` へ矯正され、参照整合は保たれる。トリムで重複した Id（`fac-a` と `fac-a ` の併存）は `EnsureUniqueIds` が違反として捕捉する。

### 4-3. 読み込み通知（BV）

`AdminDocumentService` に `LoadNotes`（`IReadOnlyList<string>`、既定空）を追加し、`LoadJson` で `result.Normalizations` を保持する（読み込み失敗時・再読み込み時は空へ戻す）。`Home.razor` の読み込みパネルに、件数が 0 でないときだけ「前後の空白を除去した ID 系値が N 件あります。」と各エントリの一覧を表示する。表示は注記系スタイルとし、先頭 20 件＋残件数とする。

### 4-4. スキーマへの反映（BU）

`$defs` に `idValue` を新設する。

```json
"idValue": { "type": "string", "minLength": 1, "pattern": "^\\S+$(?![\\s\\S])" }
```

`commonAttributes.Id` と必須参照フィールド（`ProviderFacilityId`・`ConsumeItemId`・`recipeIo.ItemId`・`recipeOutput.ItemId`・`recipeFacility.FacilityId`・`fixedConsumption.ItemId`・`gatherRate.ItemId`）は `"$ref": "#/$defs/idValue"` に置き換える。null 許容参照フィールド（Item・Environment・Recipe・GameMap の `GameEventId`、ペアの `EnvironmentId`）は `{"anyOf": [{"$ref": "#/$defs/idValue"}, {"type": "null"}]}` とする。

`$(?![\\s\\S])` は Python の `$` が末尾改行前にも一致する落とし穴を避ける IconKey と同じイディオムである。null 許容フィールドへの適用で空文字も弾かれるようになるが、空文字の参照値は null に統一する意図的な強化とする。

### 4-5. Id 欄のトリム（BW）

`CommonFieldsEditor` の Id 行を `Apply(e, v => Entity.Id = v.Trim())` に変える。`IconEditor` の `value.Trim()` と同型である。`Name`・`Description`・`VersionAdded` は対象外とする（`VersionAdded` は空白入りであれば semver 違反として検出済み）。

### 4-6. remaining-issues.md の更新

「管理ツール側の登録データの ID 修正（0.2.8 エクスポート由来）」を、本 Phase 導入後の状態に合わせて書き換える。空白入り ID は読み込み時に自動矯正されるため、残る課題は綴り違い（`cupriumCanitster`・`item-swage`）の手修正のみとなる旨を記す。rename 機能は引き続き未対応のまま残す。

## 5. 影響の確認

- 公開アプリは同じ `MasterJsonLoader` 経路で読み込むため正規化は自動適用されるが、同梱マスタに空白入り・空文字の ID 系値はなく（81 ID 実測済み）実データ上の差分は出ない
- `MasterExporter` のエクスポートも `ValidateAll` を通るため、空白入り ID はエクスポートでも拒否される
- 正規化は読み込み経路のみに入れ、編集中の文書への新規混入は BW の入力トリムで予防する
- null 許容参照フィールドのスキーマ強化で空文字が弾かれるようになるが、空文字の参照値は同梱マスタに存在しない（実測済み）

## 6. 受け入れ条件

- 空白入りのエンティティ Id・参照 Id 値が検証違反になる（BU）
- 前後空白入りの ID 系値を含む JSON が読み込め、参照整合が保たれ、除去箇所が通知される（BV）
- 内部空白を含む ID 系値は読み込みでも違反として残る（BU・BV の境界）
- 管理ツールの Id 欄で空白を含めて確定しても保存値に空白が残らない（BW）
- `validate_master.py` 経由でも空白入り ID を検出できる
- `dotnet test` 全緑
