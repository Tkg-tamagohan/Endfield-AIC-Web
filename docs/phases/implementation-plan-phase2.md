# Phase 2 実装詳細計画

**対象フェーズ**: Phase 2（Domain: モデル＋計算＋検証の移植適合＋単体テスト）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)
**関連ドキュメント**: [test-specification-phase2.md](test-specification-phase2.md)（本 Phase のテスト仕様）

> 本書は Phase 2 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### 作るもの

- `EndfieldAicWeb.Domain` の 3 サブフォルダ
  - `Models/`：requirements §5 のエンティティ（共通属性・Item・Environment・Recipe・RecipeInput/Output・Facility・RecipeFacility・FixedConsumption・GameEvent・TransportKind・IconEntry・MasterDocument）
  - `Calculation/`：計算の入出力型・`PairSelector`・`ProductionCalculator`（環境計上・固定消費・収束反復を含む）
  - `Validation/`：`MasterValidator`・`MasterValidationError`・`MasterValidationException`・`IconKeyRules`
- `EndfieldAicWeb.Domain.Tests` の単体テスト（[test-specification-phase2.md](test-specification-phase2.md) の項目 ID どおり）

### 作らないもの

- `master.json` の読み書き・スキーマ検証・Icons マニフェストのファイル解決（Phase 3 の Infrastructure）
- Application・App・Admin プロジェクトの実装（Phase 4・6）
- 期間換算の表示（M）は UI 層の責務であり Domain では `個/分` のみを扱う
- 発電モデル・保持枠/ポート・衝突クラス・Origin・レイアウト系（移植しない対象、implementation-plan §1）

## 2. モデルの対応表

| 新モデル | 旧モデルからの変更 |
|---|---|
| `MasterEntity`（抽象基底） | 共通属性 `Id`・`Name`・`Description`・`IconKey`・`VersionAdded`・`VersionRemoved`（N）を集約。旧版はエンティティごとに散在していた |
| `Item` | 旧 Item に共通属性・`GameEventId`・`IsGatherable` を追加。`Origin` 廃止（S） |
| `Environment` | 新設（H）。`ProviderFacilityId`・`ConsumeItemId`・`ConsumeRatePerMinute`・`GameEventId`。カバー範囲は持たない（W） |
| `Recipe` | `CycleTime`・`FacilityId` をペアへ移動し `Facilities: RecipeFacility[]` を持つ。共通属性・`GameEventId` |
| `RecipeFacility` | 新設。`RecipeId`・`FacilityId`・`CycleTime`・`EnvironmentId`・`FixedConsumption`。JSON ではレシピ内にネストし、`RecipeId` は所属レシピから与える |
| `FixedConsumption` | 新設（J）。`ItemId`・`RatePerMinute` |
| `Facility` | `Width`・`Height`・`PowerConsumption` のみ（G/W）。`Category`・`CollisionClass`・`PowerSupplyRange`・`InternalSlots`・ポート・`Origin` は廃止 |
| `GameEvent` | 共通属性を追加し `ActiveFrom`/`ActiveTo`（両 null=常設、T）。旧の `Recipes` ナビは持たない |
| `MasterDocument` | `SchemaVersion`・`DataVersion`・5 エンティティ・`Icons`（マニフェスト `Key/File/Sha256/Bytes`）。旧 `MasterMeta`（DB 管理用）は廃止 |

採取素材の判定は `IsGatherable`（bool）とし、`Category` は表示用タグとして残す（計算の判定には使わない）。`Category` の enum 化は実データが揃ってから検討する保留事項とする。

## 3. 計算の適合方針

### 移植する骨格

旧 `ProductionCalculator` の Session（net demand 構築・展開・引き戻し・固定点反復・循環検出・副産物充当・端末計上の残差調整）をそのまま骨格とし、稼働単位をレシピからペア（`RecipeFacility`）へ置き換える。
`RunCycles`・`RunOrder`・`Selection` はペアをキーとし、`Retract` のレシピ同一性比較はペア参照の一致に読み替える。

### 二段選択（F/U）

`PairSelector` が旧 `RecipeSelector` を置き換える。

1. アイテムを出力するレシピを `GameEventId` で適格判定し、`VersionAdded` 最新（同率は Id 昇順、パース不能は最古＋警告）の順に並べる。
2. 先頭から順に、そのレシピ内で適格なペア（`EnvironmentId` が null、または指す環境の `GameEventId` が有効）を持つ最初のレシピを採用する。
3. 採用レシピ内で `CycleTime` 最小を既定とし、同率は `EnvironmentId=null` → `FixedConsumption` なし/小（`RatePerMinute` 昇順）の順（U）。なお同率は `FacilityId` 昇順で決定論的にする。
4. `PairOverride` はペア行の全要素（`RecipeId`・`FacilityId`・`CycleTime`・`EnvironmentId`・`FixedConsumption`、P の一意キー）で照合し、不適格なら `InvalidPairOverride` 警告のうえ既定へフォールバックする。

### 環境計上（I）

稼働確定ペアの `EnvironmentId` ごとに散布機台数を確定する。

- 既定台数：その環境を必要とする稼働中ペア数（ペアは計画内でレシピに一意のため「レシピにつき 1 台」と同値）。
- 上書き：`EnvironmentCountOverride { EnvironmentId, Count }` があればそちらを優先する。
- 散布機（`ProviderFacilityId` の設備）は `FacilityRequirement`（実数=切上げ=指定台数）と `TotalPowerConsumption` に計上する。
- 環境の消費アイテムには `ConsumeRatePerMinute × 台数` を毎分の需要として追加する。
- `EnvironmentRequirement` として環境ごとの台数・消費アイテム・消費流量（個/分 合計）を出力する。

### 固定消費（J/V）

稼働確定ペアの `FixedConsumption` について `RatePerMinute × 切上台数` を毎分の需要として追加する。
切上台数はペアが属する設備の `FacilityRequirement.CeilCount` とし、文面どおりペア単位で計上する（同一設備に複数ペアが稼働する場合、各ペアが設備全体の切上台数を基準にする）。調整済モードでも基準は変えない（V）。

### 収束反復

環境消費と固定消費の追加需要は台数確定後にしか求まらず、その需要が新たなレシピ稼働（→台数変化）を生みうる。
`展開（固定点）→ 台数・散布機確定 → 追加需要の差分適用` を 1 巡とし、追加分が 0 になるまで反復する（上限 10 回）。
収束しない場合は `ConvergenceNotReached` 警告を返し、最後の状態を結果として返す。
追加需要は累積値との差分で適用し、減少した場合は需要を引き下げたうえで引き戻しを再実行する。

### イベント不可扱い（T/X）

- レシピ・環境の `GameEventId` は上記の適格判定で処理する。
- アイテム自体の `GameEventId` が非有効なら生産・外部調達とも不可とし、展開せず需要を未充足とする（`EventItemUnavailable` 警告）。

### 一意性（旧 BN 相当）

計画内ではレシピ→ペアを一意とする。
異なるアイテムの需要が同一レシピの別ペアを選んだ場合、先に確定したペアを採用して `PairConflict` 警告を出す。

### 削除するもの

- `ReactorUnitPacker`・発電反復（`PowerCalculator`）・`ReactorUnit`/`PowerSummary` 出力・`MasterDataSnapshot` の発電/合成設備識別。
- `ContextFilter.GameVersion`（§3 の入力にない。`VersionAdded` は既定選択の順序付けにのみ使う）。
- `IsRecipeFeasibleOnFacility`（保持枠/ポート制約自体が廃止）。

## 4. 出力型（requirements §4 / implementation-plan §3 準拠）

`ProductionPlan`：以下を返す。未調整・調整済の 2 状態切替（O）は表示層の責務であり、計算結果は共用する。

| メンバー | 型 | 内容 |
|---|---|---|
| `ItemRequirements` | `ItemRequirement[]` | 需要・供給内訳（Recipe/Byproduct/Gathered）・未充足量 |
| `FacilityRequirements` | `FacilityRequirement[]` | 実数台数・切上げ台数。散布機を含む |
| `RecipeRuns` | `RecipeRun[]` | `RecipeId`・`FacilityId`・`CyclesPerMinute`。ペア単位 |
| `EnvironmentRequirements` | `EnvironmentRequirement[]` | `EnvironmentId`・散布機台数・消費アイテム・消費流量（個/分） |
| `TotalPowerConsumption` | `double` | Σ(設備消費電力 × 切上台数)。散布機分を含む |
| `Surpluses` | `SurplusProduction[]` | 充当しきれなかった余剰 |
| `FlowAdjustments` | `FlowAdjustment[]` | `RecipeId`・`InputItemId`・要求流量・推奨制限（個/s） |
| `Warnings` | `CalculationWarning[]` | 下表のコード |

警告コード：`CycleDetected`・`NoRecipeAvailable`・`TransportCapacityExceeded`・`InvalidPairOverride`・`PairConflict`・`EventItemUnavailable`・`InvalidVersionString`・`InvalidEnvironmentOverride`・`ConvergenceNotReached`。
旧コードから `NoGeneratorRecipe`・`PowerNotConverged`・`ReactorRecipeInfeasible`・`InvalidRecipeOverride`（ペア化により改名）を整理する。

## 5. 検証の適合方針

`MasterValidator` を新モデルへ適合する。
違反は例外ではなく `MasterValidationError` の一覧として集約して返す方針は変えない。

| 規則 | 扱い |
|---|---|
| 必須項目（Id・Name・各必須属性）・enum 定義値（`TransportKind`） | 継承 |
| 値域（`CycleTime`>0、`Quantity`>0、`PowerConsumption`>=0、`Width`/`Height`>0、`ConsumeRatePerMinute`>0、`RatePerMinute`>0、`ActiveFrom`<`ActiveTo`） | 継承・適合 |
| `VersionAdded` の semver 形式・`VersionRemoved`>`VersionAdded` | 旧版は Recipe のみだったが、共通属性（N）の一貫した値域として全エンティティへ適用する |
| 参照整合性（Recipe の入出力 ItemId・ペアの FacilityId/EnvironmentId/FixedConsumption.ItemId・Environment の ProviderFacilityId/ConsumeItemId・Item/Environment/Recipe の GameEventId） | 新モデルに合わせて再構成 |
| ペア一意性（P） | 同一レシピ内で全要素の組が重複するペアを検出する |
| ペアの `RecipeId` | 所属レシピの Id と一致すること |
| 仮想アイテム規則 | 「レシピ入力に `TransportKind.None` を含めない」のみ継承。発電設備由来の出力側規則は廃止 |
| 保持枠/ポート制約・発電設備規則・`Origin` の enum 検査・`MaxFacilitySize` 上限 | 廃止 |
| `IconKey` の文字種制約（`IconKeyRules`） | 継承 |

## 6. 作業順序

1. `Models/` のエンティティを実装する。
2. `Calculation/` の入出力型・警告コード・`PairSelector` を実装する。
3. `ProductionCalculator` を移植し、環境計上・固定消費・収束反復を実装する。
4. `Validation/` を適合する。
5. [test-specification-phase2.md](test-specification-phase2.md) の項目どおりに単体テストを作成する。
6. `dotnet test` を全緑にし、`implementation-plan.md` の Phase 2 チェックリストを更新する。
