# Phase 27 実装詳細計画

**対象フェーズ**: Phase 27（散布機台数の入力範囲）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 BS。関連: BQ・BR・AH）
**関連ドキュメント**: [test-specification-phase27.md](test-specification-phase27.md)（本 Phase のテスト仕様）、[implementation-plan-phase26.md](implementation-plan-phase26.md)（カバー上限の先行型）

> 本書は Phase 27 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

- 計算ページの散布機台数の入力範囲を `0〜必要台数` から `必要台数〜ceil(利用機械数)` へ改める（仕様決定 BS）。必要台数未満（0 台を含む）と上限超過は入力エラーとする
- 環境要件にその環境を利用する機械数合計（実数）を出力し、入力上限の算定に使う
- 台数入力の範囲表示（「（自動 N 台まで）」→「（自動 N 台・N〜M 台）」系）とヒント文面の更新

### やらないこと

- `EnvironmentCountOverride`・`CalculationSession` のカバー上限・不足計算（Expand クランプ・`EnvironmentCoverageExceeded`・未充足配分・未調整ビューの環境倍率）の変更。計算エンジンは必要台数未満の上書きを従来どおり受理し、カバー不足を計算する防御的経路として残す（仕様決定 BS）
- 自動台数（必要台数）の算出式・`CoverableMachines` の変更（BQ・BP を維持する）
- 環境行の出し分け（計算に登場する環境のみ表示する規則）の変更

## 2. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `Calculation/CalculationOutputs.cs` | `EnvironmentRequirement` に利用機械数合計（実数）を追加する |

### Application

| ファイル | 変更 |
|---|---|
| `PlanViewDefaults.cs` | `DispenserLimit` を「下限=必要台数・上限=利用機械数の切上げ」の範囲を返す形へ改める |
| `CalculationInputBuilder.cs` | `EnvCountInput`・`EnvCountState` に下限を持たせ、`TryParseEnvironmentCounts`・`ReconcileEnvCountText` を範囲検証へ改める |

### SharedUi

| ファイル | 変更 |
|---|---|
| `Components/CalculatorPanel.razor` | 台数入力を新しい範囲で検証し、範囲表示（「（自動 N 台・N〜M 台）」系）とヒントへ更新する |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | BS（本計画で追加済み） |
| `docs/requirements.md` | §2.1・§4.3 への反映（済） |
| `docs/implementation-plan.md` | Phase 27 行（済）。実装 PR でチェックを `[x]` にする |

### テストフィクスチャ

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Application.Tests` 等 | `EnvironmentRequirement` のフィールド追加に伴う手組み計画フィクスチャを追従させる |

## 3. 変更詳細

### 3.1 利用機械数の出力

`EnvironmentRequirement` に `UsedMachineCount`（`double`）を追加する。その環境を要するランの機械数合計で、`RequiredDispenserCount` の見積もり分子（実績機械数＋有効削減機械数）と同じ量とする。計算エンジンが防御的経路でカバー不足の計画を返した場合も、下限（必要台数）と上限（利用機械数の切上げ）が同じ分母で組まれる。

### 3.2 入力範囲（BS）

- `PlanViewDefaults`: `DispenserLimit(plan, envId)` を `DispenserRange(plan, envId) -> (int Min, int Max)` へ置き換える。`Min = RequiredDispenserCount`、`Max = Ceil(UsedMachineCount)`。計画に登場しない環境は `(0, 0)` とする。`Min <= Max` は `CoverableMachines >= 1` から常に成り立つ
- `EnvCountInput`・`EnvCountState`: `Max` に `Min` を添える
- `TryParseEnvironmentCounts`: `count < Min || count > Max` をエラーとし、メッセージは「`<環境名>` の散布機台数は `<Min>`〜`<Max>` の整数で入力してください。」とする。0 を含む下限未満は同じエラーで拒否する
- `ReconcileEnvCountText(countText, min, max)`: 整数かつ `min <= count <= max` の保持値のみ残し、範囲外・非整数は空欄（自動値）へ戻す（AH の範囲読み替え。クランプはしない既存規則どおり）

### 3.3 UI 反映

- 環境行の表示を「（自動 N 台まで）」から「（自動 N 台・N〜M 台）」系へ改める。自動値（空欄）は従来どおり必要台数 N を指す
- ヒントを新仕様の説明へ更新する。「台数を下げると未充足」の説明は範囲変更で到達不能になるため外し、必要台数以上の台数がガス消費・消費電力へ比例して載る旨を説明する（文言は実装 PR で確定）
- 必要台数を超える台数はカバー上限を緩めるだけで生産結果は変わらないが、散布機の設備要件・ガス消費（`ConsumeRatePerMinute × 台数`）・消費電力は台数比例で増える。実機で多めに設置した台数をそのまま見積もりに反映する用途になる

## 4. 暫定解釈と確定した UI 上の判断

実装中に変更する場合は本節を更新する。

1. 上限値は `Ceil(UsedMachineCount)` とする。利用機械数が実数のため整数台数へ切上げ、散布機が利用機械を 1 対 1 でカバーする最大の有意台数と一致する
2. 「利用機械数」は `RequiredDispenserCount` の分子と同じ量（実績機械数＋有効削減機械数）とする。必要台数と上限が同じ分母で組まれ、Domain の防御的経路が返す不足計画でも範囲が成立する
3. 空欄・範囲外の保持値は従来どおり自動（必要台数）へ戻す。範囲の読み替えで、従来有効だった `1〜N−1` の保持値も自動へ戻る（AH と同じ規則）
4. カバー不足警告・未充足配分・未調整ビューの環境倍率は UI 経路では到達不能になるが、計算エンジン側の挙動は変更しない防御的経路として残す。Domain テストは直接 `EnvironmentCountOverride` を渡して維持する

## 5. テスト

[test-specification-phase27.md](test-specification-phase27.md) に従う。
入力パース・保持値整合・上限算出は Application の xUnit で、利用機械数の出力は Domain の xUnit で検査し、`dotnet test` 全緑を確認する。
UI の範囲表示・入力エラー・台数超過時のガス消費はブラウザ E2E で確認する。
ブラウザプレビューによるユーザー確認は実装後・手動検査項目の実施前に挟み、フィードバックを検査へ反映する（ui-mock-first ルール、implementation-plan.md §5「UI の確認」に従う）。

## 6. 受け入れ条件

- 息壌 150/分（機械数 5.0・必要台数 2）で環境行の入力範囲が 2〜5 台になり、2 未満（0 台を含む）と 5 超は入力エラーになる
- 台数を 3〜5 にすると散布機台数・ガス消費・消費電力が台数比例で増え、生産・未充足は変わらない
- 保持された台数が再計算後の範囲から外れた場合は自動値へ戻る
- `dotnet build` と `dotnet test` が全緑である
