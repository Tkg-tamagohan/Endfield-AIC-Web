# Phase 9 実装詳細計画

**対象フェーズ**: Phase 9（用語・単位の整理: 基礎素材→採取素材の改称＋レート単位の毎分統一）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AB・AF）

> 本書は Phase 9 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

仕様決定 AB・AF は requirements.md と implementation-plan.md 側へ先行反映済みである。
本 Phase はコード・データ・テスト・過去 Phase 文書をその確定仕様へ揃える追従作業であり、挙動変更を伴わない。

- 「基礎素材」→「採取素材」の改称（仕様決定 AB）
  - `Item.IsBaseMaterial` → `IsGatherable`、`SupplyKind.RawMaterial` → `Gathered`
  - JSON フィールド名・`Category` 値・UI ラベル・テスト・フィクスチャ・文書の用語
- レート単位の毎分統一（仕様決定 AF）
  - `Environment.ConsumeRatePerSecond` → `ConsumeRatePerMinute`、`FixedConsumption.RatePerSecond` → `RatePerMinute`
  - `data/master.json` の値を換算（6 → 360、0.5 → 30）、計算内の ×60 換算を除去
  - 出力側の `EnvironmentRequirement.ConsumeRatePerSecondTotal` → `ConsumeRatePerMinuteTotal`、Admin 入力ラベルと App の表示を個/分へ
- `SchemaVersion` は 1 のままとする（仕様決定 AF。実データ投入前で移行需要がない）
- 本書と、[test-specification-phase9.md](test-specification-phase9.md)（実装時に作成）

### やらないこと

- `FlowAdjustment` の `RequiredPerSecond`・`RecommendedLimitPerSecond` や輸送容量（ベルト 30 個/s・パイプ 60 個/s）は個/s のまま据え置く。表示単位切替の「毎秒」選択肢（`AmountUnit.PerSecond`）も同様で、単位統一の対象はモデル保持とユーザー入力のレートのみである（仕様決定 M・O）。輸送容量と推奨流量制限の値は Phase 16 の仕様決定 AM で個/分へ改訂する（「毎秒」選択肢は対象外のまま）
- `ItemId`・`Name`・`Category="部品"` など改称対象外の値の変更
- UI の操作構造の変更。ラベル文字列の追従のみで、ブラウザプレビューでのユーザー確認は挟まない（新規 UI を伴わないため）

## 2. 変更対象の一覧

### 2.1 モデル・計算（Domain/Application）

| ファイル | 変更 |
|---|---|
| `Domain/Models/Item.cs` | `IsBaseMaterial` → `IsGatherable` |
| `Domain/Models/Environment.cs` | `ConsumeRatePerSecond` → `ConsumeRatePerMinute`（個/分） |
| `Domain/Models/FixedConsumption.cs` | `RatePerSecond` → `RatePerMinute`（個/分） |
| `Domain/Calculation/CalculationOutputs.cs` | `SupplyKind.RawMaterial` → `Gathered`、`EnvironmentRequirement.ConsumeRatePerSecondTotal` → `ConsumeRatePerMinuteTotal` |
| `Domain/Calculation/ProductionCalculator.cs` | `IsRawMaterial` → `IsGatherable`。`ConsumeRatePerSecond × 60 × 台数` → `ConsumeRatePerMinute × 台数`、`RatePerSecond × 60 × 切上台数` → `RatePerMinute × 切上台数`、`ConsumeRatePerSecond × dispenserCount` → `ConsumeRatePerMinute × dispenserCount` の ×60 除去 |
| `Domain/Calculation/PairSelector.cs` | ペア既定の同率比較とキー等価の `RatePerSecond` を `RatePerMinute` へ |
| `Domain/Calculation/CalculationWarning.cs` | コメントの「基礎素材」を「採取素材」へ |
| `Domain/Validation/MasterValidator.cs` | 値域検証とエラー `Field` 名を `ConsumeRatePerMinute`・`FixedConsumption.RatePerMinute` へ |
| `Application/EntityFactory.cs` | `IsGatherable = false`、`ConsumeRatePerMinute = 60`（旧既定 1 個/s の等値換算） |
| `Application/PairOption.cs` | ペアキー内の `RatePerSecond` 参照を `RatePerMinute` へ |

### 2.2 JSON I/O・データ・スキーマ（Infrastructure/data）

| ファイル | 変更 |
|---|---|
| `Infrastructure/Transfer/MasterJsonDto.cs` | `IsBaseMaterial`/`ConsumeRatePerSecond`/`RatePerSecond` → `IsGatherable`/`ConsumeRatePerMinute`/`RatePerMinute` |
| `Infrastructure/Transfer/MasterJsonReader.cs` | 実体化・必須検証・エラー `Field` 名を新名へ |
| `Infrastructure/Transfer/MasterExporter.cs` | 書き出しフィールド名を新名へ |
| `data/master.schema.json` | 同名フィールドを新名へ追従（説明文の「基礎素材」も「採取素材」へ） |
| `data/master.json` | 同上。値は `ConsumeRatePerSecond: 6` → `ConsumeRatePerMinute: 360`、`RatePerSecond: 0.5` → `RatePerMinute: 30` に換算。`Category: "基礎素材"` → `"採取素材"` |

旧フィールド名を持つ JSON は、必須フィールド欠落＋未知プロパティ拒否の既存規則でそのまま拒否される（移行需要がないため後方互換は持たない）。

### 2.3 UI ラベル（Admin/App）

| ファイル | 変更 |
|---|---|
| `Admin/Pages/ItemsPage.razor` | ラベル「基礎素材」→「採取素材」、バインド先を `IsGatherable` へ |
| `Admin/Pages/EnvironmentsPage.razor` | ラベル「消費速度（個/s）」→「消費速度（個/分）」、バインド先を `ConsumeRatePerMinute` へ |
| `Admin/Pages/RecipesPage.razor` | 固定消費の単位表示「個/s」→「個/分」、新規行の既定値 `RatePerSecond = 1` → `RatePerMinute = 60`（等値換算） |
| `Admin/Pages/PreviewPage.razor` | 環境行の「個/s」→「個/分」、供給内訳の「基礎素材」→「採取素材」、`ConsumeRatePerSecondTotal` → `ConsumeRatePerMinuteTotal` |
| `App/Pages/Home.razor` | 環境行の「個/s」→「個/分」、供給内訳の「基礎素材」→「採取素材」、ペア候補ラベルの固定消費「/秒」→「/分」。`PeriodConsumption` は引数が個/分値になるため ×60 を除去 |

### 2.4 テスト・フィクスチャ

- `tests/EndfieldAicWeb.Domain.Tests/CalculationFixtures.cs`: `isBaseMaterial` → `isGatherable`、`Env` の引数 `ratePerSecond` → `ratePerMinute`、`FixedConsumption.RatePerSecond` → `RatePerMinute`。フィクスチャのレート値は個/分の等値へ換算する（`1` → `60`、`6` → `360`、`2` → `120`、`4` → `240`、固定消費 `0.1` → `6`、`0.5` → `30`、`2` → `120`）
- `tests/EndfieldAicWeb.Application.Tests/ApplicationFixtures.cs`: 同上
- 各テストファイル: `SupplyKind.RawMaterial` → `Gathered`、`ConsumeRatePerSecondTotal` → `ConsumeRatePerMinuteTotal`（期待値は等値換算で 6 → 360）、必須・値域の `Field` 名検証を新名へ、`Category` 文字列の「基礎素材」→「採取素材」、テスト名・DisplayName の用語追従
- `tests/EndfieldAicWeb.Infrastructure.Tests/TestJson.cs`・`MasterJsonLoaderTests.cs`・`MasterExporterTests.cs`: JSON フィールド名と値の換算
- `tests/EndfieldAicWeb.Admin.Tests/AdminIconTests.cs`: `ConsumeRatePerSecond` → `ConsumeRatePerMinute`

### 2.5 過去 Phase 文書の用語追従

文書は現在の仕様を指す参照元であるため、旧名のまま残さない。

- `docs/phases/test-specification-phase2.md`: フィクスチャ定義のフィールド名・レート値（個/分換算）・`RawMaterial`・「基礎素材」表記を追従。期待値は等値のため変わらない
- `docs/phases/test-specification-phase3.md`: `IsBaseMaterial`/`ConsumeRatePerSecond`/`RatePerSecond` の記述を新名へ
- `docs/phases/test-specification-phase4.md`: 「基礎素材」「個/s」の環境消費記述を追従（流量調整の個/s は対象外。Phase 16 の仕様決定 AM で個/分へ改訂）
- `docs/phases/test-specification-phase6.md`・`implementation-plan-phase2.md`・`phase3.md`・`phase4.md`・`phase6.md`: 同名の追従
- `docs/implementation-plan.md`: Phase 9 のチェックを `[x]` へ

## 3. 暫定解釈

- **新規行の既定値**: Admin の新規環境・新規固定消費行の既定値は、旧値の等値換算（1 個/s → 60 個/分）とする。ユーザー入力の初期値であり仕様の意図を変えない
- **「採取素材」の表示文言**: 供給内訳・チェックボックス・`Category` 値は「採取素材」で統一し、「採取」「採取素材」で表記が揺れないようにする
- **`IsGatherable` の命名**: 仕様決定 AB のとおりとし、`Item` の XML コメントも「需要展開の終端となる採取素材か。true なら採取（外部調達）扱い」に揃える

## 4. 作業順序

1. 本書を作成する。
2. [test-specification-phase9.md](test-specification-phase9.md) を作成する。
3. §2.1 のモデル・計算を改称し、×60 換算を除去する。
4. §2.2 の JSON I/O・スキーマ・`data/master.json` を追従する。
5. §2.3 の UI ラベルを追従する。
6. §2.4 のテスト・フィクスチャを追従する。
7. §2.5 の文書を追従し、`implementation-plan.md` の Phase 9 を `[x]` へ更新する。
8. `dotnet test` 全緑・`tools/validate_master.py` 通過を確認して PR を作成する。

## 5. 受け入れ条件

- `dotnet test` が全緑。`~/.venvs/validate/bin/python tools/validate_master.py` がスキーマ適合を報告する
- 改称・単位変換のみで挙動は変わらない（既存テストの期待値は等値換算で据え置き）
