# Phase 16 実装詳細計画

**対象フェーズ**: Phase 16（輸送容量と推奨流量制限の単位改訂）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AM）
**関連ドキュメント**: [test-specification-phase16.md](test-specification-phase16.md)（本 Phase のテスト仕様）

> 本書は Phase 16 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書改訂と実装は 2 つの PR に分け、本書とテスト仕様書を含む文書改訂 PR を先行させる。

## 1. スコープ

### やること

輸送容量と推奨流量制限の単位を個/s から個/分へ改訂する（仕様決定 AM）。
内部の計算保持値はすでに個/分のため、判定・出力時の /60 換算を除去して単位を揃える改訂である。

- 輸送容量をベルト 30 個/分・パイプ 60 個/分へ改訂する。容量定数の改名（`BeltCapacityPerSecond` → `BeltCapacityPerMinute`、`PipeCapacityPerSecond` → `PipeCapacityPerMinute`）、警告判定の /60 除去、警告文の単位表記の追従
- `FlowAdjustment` の要求流量・推奨制限を個/分へ改訂する。フィールド名の改名（`RequiredPerSecond` → `RequiredPerMinute`、`RecommendedLimitPerSecond` → `RecommendedLimitPerMinute`）と UI 表示行の追従
- `FlowGraphModelBuilder` の容量超過判定を新定数・新単位へ追従する
- 改訂で新たに容量警告が発火する既存テストを仕様に沿って見直す

### やらないこと

- 表示単位切替の「毎秒」選択肢（`AmountUnit.PerSecond`、仕様決定 M）の変更。要求量・供給量の表示換算は従来どおりとする
- 警告動作の意味の変更。容量超過自体は従来どおり許容し、超過時は必要レーン数を警告に添える（R で旧 L 継承）
- `TransportKind.None` のアイテムへの容量判定
- 採取上限・環境消費・固定消費の単位変更（すでに個/分、仕様決定 AF）
- 実マスタデータ・Admin 側マスタ項目の変更（輸送容量はコード定数でありマスタ項目ではない）

## 2. 現状と変更の性質

内部の計算は `Demand`・`Produced`・`Raw`・`RunCycles` をすべて個/分で保持している。
現行コードは警告判定と `FlowAdjustment` の出力の 2 箇所だけ /60 で個/s に換算しており、容量値 30・60 はその個/s 値を前提に旧 Planner から継承された。
正しい容量はベルト 30 個/分・パイプ 60 個/分のため、数値はそのままに単位の枠組みを個/分へ揃える（/60 の除去）。
この改訂で警告発火の実効閾値は従来の 1/60 に下がり、通常の計画でも容量超過警告が出やすくなる。

## 3. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `Domain/Calculation/ProductionCalculator.cs` | `BeltCapacityPerSecond` → `BeltCapacityPerMinute`、`PipeCapacityPerSecond` → `PipeCapacityPerMinute`（値は 30・60 のまま、個/分とする）。`AddTransportWarnings` の /60 除去・変数名 `flowPerSecond` → `flowPerMinute`・警告文の「個/s」→「個/分」。`BuildFlowAdjustments` の /60 除去 |
| `Domain/Calculation/CalculationOutputs.cs` | `FlowAdjustment` の `RequiredPerSecond` → `RequiredPerMinute`、`RecommendedLimitPerSecond` → `RecommendedLimitPerMinute` |
| `Domain/Calculation/CalculationWarning.cs` | コメントの容量単位を個/分へ追従 |

### Application

| ファイル | 変更 |
|---|---|
| `Application/FlowGraphModelBuilder.cs` | 容量定数の新名追従、超過判定の /60 除去 |

### UI（App・Admin）

| ファイル | 変更 |
|---|---|
| `App/Pages/Home.razor` | 推奨流量制限行の「/秒」→「/分」、フィールド名追従 |
| `Admin/Pages/PreviewPage.razor` | 同上 |

### テスト

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/TransportCapacityTests.cs` | TRN-01〜04 の流量値を新単位の期待へ更新（45/分・90/分・500/分・30/分）。警告文の単位表記検証を追加 |
| `tests/EndfieldAicWeb.Domain.Tests/FlowAdjustmentTests.cs` | FLW-01/02/04/05 の期待値を個/分へ更新し、フィールド名を追従 |
| `tests/EndfieldAicWeb.Application.Tests/FlowGraphModelBuilderTests.cs` | FG-13 のコメントの単位を追従 |
| `Assert.Empty(plan.Warnings)` を持つ既存テスト | 容量 30/分・60/分 化で新たに発火する分を洗い出し、容量警告のみを許容する形へ見直す。対象は `dotnet test` の全件結果で確定する |

### 文書（先行 PR で実施済み）

| ファイル | 変更 |
|---|---|
| `docs/requirements.md` | §4.2 の容量単位を個/分へ |
| `docs/decision-records.md` | 決定 AM を追加し、R の輸送容量の記述を個/分へ修正 |
| `docs/implementation-plan.md` | §3 の FlowAdjustment・輸送容量の単位、Phase 16 セクション追加 |
| `docs/phases/` 各計画書・テスト仕様書 | phase2（FLW/TRN 表）・phase4（UI 行仕様・換算対象）・phase9（据え置きの改訂注記）・phase15（FG 容量判定）を追従 |

## 4. 確定した判断

- 容量値は 30・60 の数値を保ち単位だけを変える。内部保持値が個/分で統一済みのため、改訂は /60 除去と命名・表記の変更に集約される
- `FlowAdjustment` の単位も輸送容量と揃えて個/分とする（ユーザー指示による。輸送容量と流量制限で単位が混在する状態を避ける）
- 警告文の単位表記は「個/分」とし、推奨流量制限の UI 行は「/分」表記とする
- 文書改訂 PR を先行させ、実装 PR は文書の内容を根拠に進める

## 5. 受け入れ条件

- 容量超過警告がベルト 30 個/分・パイプ 60 個/分で発火し、警告文が個/分表記になる
- `FlowAdjustment` の要求流量・推奨制限が個/分値で出力され、UI の推奨流量制限行が個/分表記になる
- `dotnet test` が全緑（改訂で新たに発火する容量警告を仕様に沿って見直したうえで）
- フローグラフの容量超過フラグが新容量・新単位で警告発火と一致する
