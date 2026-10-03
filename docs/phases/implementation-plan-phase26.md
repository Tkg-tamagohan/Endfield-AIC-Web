# Phase 26 実装詳細計画

**対象フェーズ**: Phase 26（散布機のカバー可能台数と台数計算の機械数比例化）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 BP〜BR。改定元の I）
**関連ドキュメント**: [test-specification-phase26.md](test-specification-phase26.md)（本 Phase のテスト仕様）、[test-specification-phase4.md](test-specification-phase4.md)（散布機台数上書きの先行群）、[implementation-plan-phase11.md](implementation-plan-phase11.md)（採取上限の未充足化の先行型）

> 本書は Phase 26 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

- `Environment` に `CoverableMachines`（正の整数、供給設備 1 台が環境を供給できる機械台数）を追加し、同梱マスタの env-stable・env-acrid へ 4 を投入する（仕様決定 BP）
- 散布機の既定台数を「その環境を必要とする稼働中ランの機械数合計 ÷ `CoverableMachines` の切上げ」へ改める（仕様決定 BQ。I の「レシピにつき 1 台」の改定）
- 散布機台数のユーザー上書きを `台数 × CoverableMachines` の機械数上限として需要展開へ反映し、カバーしきれない機械分を未充足とする（仕様決定 BR）
- 環境要件へ必要台数（自動値）を保持させ、計算ページの入力上限と「自動 N 台まで」表示に使う

### やらないこと

- 散布機のカバー範囲（面積・形状）のモデル化（仕様決定 W を維持する）
- カバー不足時の代替レシピへの自動切替・ペア選択の自動変更（ユーザーのペア選択で対応する。BR）
- カバー不足の未充足分の再展開（引き戻しでカバーが空いても展開に戻さない暫定解釈。§4）
- グラフの層割り・表示規則の変更（散布機台数の変化は台数分表示へ既存規則のまま反映される）
- `EnvironmentCountOverride` の入力形式・保持規則（AH）・エラー系の変更

## 2. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `Models/Environment.cs` | `CoverableMachines`（`int`、required）を追加する。正の整数のみ有効 |
| `Validation/MasterValidator.cs` | `ValidateEnvironment` に `CoverableMachines > 0` の検査を追加する |
| `Calculation/CalculationSession.cs` | `ComputeCounts` の既定台数を機械数合計 ÷ `CoverableMachines` の切上げへ改める。`Expand` で上書き台数による機械数上限を需要展開へ適用する。削られた機械数を記録し、`Run` 末尾でカバー不足警告を発行する |
| `Calculation/CalculationOutputs.cs` | `EnvironmentRequirement` に必要台数（自動値）を追加する。`FacilityCounts` 内部型に必要台数マップを持たせる |
| `Calculation/CalculationWarning.cs` | `WarningCode` にカバー不足のコード `EnvironmentCoverageExceeded` を追加する |

### Application

| ファイル | 変更 |
|---|---|
| `PlanViewDefaults.cs` | `DispenserLimit` を環境要件の必要台数から返す形へ改める（確定ペアの distinct レシピ数の走査をやめる） |
| `EntityFactory.cs` | `NewEnvironment` の `CoverableMachines` 既定値を 4 とする（現行ガス散布機の実測値に揃える暫定値） |

### Infrastructure

| ファイル | 変更 |
|---|---|
| `Transfer/MasterJsonDto.cs` | 環境 DTO に `CoverableMachines` を追加する |
| `Transfer/MasterJsonReader.cs` | 構造検証へ `CoverableMachines` の必須・数値検査を追加し、モデルへ変換する |
| `Transfer/MasterExporter.cs` | エクスポートへ `CoverableMachines` を含める |

### Admin

| ファイル | 変更 |
|---|---|
| `Pages/EnvironmentsPage.razor` | 「カバー可能台数」の `NumberInput` を追加する |

### SharedUi

| ファイル | 変更 |
|---|---|
| `Components/CalculatorPanel.razor` | 環境節のヒントをカバー上限の効果（台数を下げると機械数上限が下がり、超過分は未充足になる）へ更新する |

### データ

| ファイル | 変更 |
|---|---|
| `data/master.schema.json` | `$defs/environment` に `CoverableMachines`（integer、exclusiveMinimum 0）を追加し required へ入れる |
| `data/master.json` | env-stable・env-acrid に `"CoverableMachines": 4` を追加し `DataVersion` を更新する |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | BP・BQ・BR（本計画で追加済み） |
| `docs/requirements.md` | §2.1・§4.3・§5.4・§10 への反映（済） |
| `docs/implementation-plan.md` | §2・§3 の Environment 記述の更新（済）。実装 PR で Phase 26 のチェックを `[x]` にする |

### テストフィクスチャ

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/CalculationFixtures.cs` | `Env()` ヘルパーに `coverableMachines` 引数（既定 4）を追加する |
| `tests/EndfieldAicWeb.Application.Tests` 等 | `EnvironmentRequirement` のフィールド追加に伴う手組み計画フィクスチャを追従させる |

### 検証手順の追従

| ファイル | 変更 |
|---|---|
| `.devin/skills/`・個人プラグインの検証スキル | 散布機台数の既定が機械数比例になった旨と、カバー不足フィクスチャの作り方を追記する（リポジトリ AGENTS.md の規約） |

## 3. 変更詳細

### 3.1 CoverableMachines モデル（BP）

`Environment` に `CoverableMachines`（`int`）を追加する。供給設備（散布機）1 台が環境を供給できる機械台数で、散布機台数と機械数上限を結ぶ係数である。

- `Models/Environment.cs`: `public required int CoverableMachines { get; set; }`
- `MasterValidator.ValidateEnvironment`: `CoverableMachines <= 0` を検証エラーとする（整数性はスキーマと DTO の型で担保する）
- `master.schema.json`: `$defs/environment` の properties に `"CoverableMachines": { "type": "integer", "exclusiveMinimum": 0 }` を追加し required へ入れる
- `MasterJsonDto` の環境 DTO に `int? CoverableMachines` を追加し、`MasterJsonReader` で `RequireNumber` と整数検査を行ってモデルへ写す。`MasterExporter` は出力へ含める
- `EntityFactory.NewEnvironment`: 既定値 4（現行のガス散布機と同値とする暫定値）
- `EnvironmentsPage.razor`: 「カバー可能台数」の `NumberInput` を消費速度の隣へ追加する
- `data/master.json`: env-stable・env-acrid に `"CoverableMachines": 4` を追加し、`DataVersion` を 0.2.6 へ更新する。`tools/validate_master.py` と `BundledMasterDataTests` で検証する

マスタモデル変更の五点セット（モデル・スキーマ・データ・文書・テスト）は `master-model-change-sync` スキルの手順に従う。

### 3.2 既定台数の機械数比例化（BQ）

`ComputeCounts` の環境ごとの台数確定を、レシピ数の数え上げから機械数集計へ改める。

- 同環境を要する全ランの機械数を `RunCycles[run] × run.Pair.CycleTime / 60` の合算で集計する（`envMachines[envId]`、実数）
- 必要台数 `required[envId] = Ceil(envMachines[envId] / env.CoverableMachines)`
- 散布機台数 `dispenser[envId] = override ?? required[envId]`（上書き優先は従来どおり）

環境要件の行は、稼働中ランが要求する環境に加え、カバー不足で稼働が停止した要求を持つ環境（§3.3 の削減機械数が 0 より大きい環境）も出す。全量停止の環境も「自動 N 台まで」が見えて台数を戻せるようにするためである。

`EnvironmentRequirement` に `RequiredDispenserCount` を追加し、計算ページの入力上限（`PlanViewDefaults.DispenserLimit`）と「自動 N 台まで」の表示に使う。必要台数は `Ceil((実績機械数 + 削減機械数) / CoverableMachines)` で見積もる。

### 3.3 カバー上限の需要展開反映（BR）

ユーザーが指定した散布機台数は `override.Count × env.CoverableMachines` をその環境を要する機械数の上限とする。自動台数は必要量を常に満たすため、上限は上書きが存在する環境にのみ発生する。

`Expand` でランの稼働を増やす箇所に上限を挟む。残需要 `remainder` に対応するサイクル増分 `delta` を計算した後、ランのペアが `EnvironmentId` を持ち上限があるとき、次で `delta` を削る。

```
used[env] = Σ（同 env のラン r の RunCycles[r] × r.Pair.CycleTime / 60）  // 使用済み機械数
allowed  = cap[env] − used[env]                                        // 残カバー量（機械数）
delta    = min(delta, allowed × 60 / run.Pair.CycleTime)               // サイクルへ換算して削る
```

削られた分は次の 3 系統へ記録する。

- `Unmet[itemId] += 削減サイクル × 当該アイテムの 1 サイクル出力量`。未充足は残差需要から外れる端末計上の既存規則どおりとする
- `_envBlockedMachines[envId] += 削減サイクル × CycleTime / 60`（必要台数の見積もり用）
- `_envCapEnvs`（環境 ID の集合）へ記録し、`Run` 末尾の警告段で `EnvironmentCoverageExceeded` を発行する

`delta` が 0 まで削られたランは稼働させず、`RunCycles`・`RunOrder` に登録しない。登録してから引き戻しで削ると、展開と引き戻しの間で挿抜が繰り返されるためである。

容量の占有順は展開順（`RunOrder` 先頭から）とする。採取上限・設備ユニット充填と同じく、先に確定した需要を優先する既存規則に揃える。

カバー不足の未充足は再展開しない（暫定解釈）。引き戻しでカバーが空いても、その時点の残差に未充足分は含まれないためである。採取上限超過分の「別レシピへ展開」と異なり、カバー上限では代替の自動切替を行わない（BR）。

上書きが必要台数を超える指定は従来どおり受理する（上限が緩いだけで挙動は変わらない。ENV-03 の既存契約）。0 台指定はその環境を要する生産を全量未充足にする。

警告は `Run` 末尾の既存警告段で発行する（GatherCapExceeded 等と同じ位置、最終の未充足確定後）。警告コードは `EnvironmentCoverageExceeded` とし、文面は実装 PR で確定する（テストはコードで検査する）。

### 3.4 UI 反映

- `PlanViewDefaults.DispenserLimit`: `plan.EnvironmentRequirements` の当該環境の `RequiredDispenserCount` を返す形へ改める。従来の「確定ペア中に同環境を要する distinct レシピ数」の走査は、機械数比例の自動値と一致しないため置き換える
- `CalculatorPanel.razor`: 環境節のヒントを「散布機台数を下げるとカバーできる機械数が減り、超過分は未充足になります（自動値が上限）」系へ更新する（文言は実装 PR で確定）。「（自動 N 台まで）」は `RequiredDispenserCount` を表示する（現行 `EnvAutoCount` の Max 経路に載る）
- 全量停止したランは確定ペア・供給内訳から外れる（稼働 0 のランを出力しない既存規則どおり）。理由は未充足と警告で示す

## 4. 暫定解釈と確定した UI 上の判断

実装中に変更する場合は本節を更新する。

1. カバー不足の未充足は再展開しない暫定解釈を採る。未充足は端末計上で残差需要から外れる既存動作のままとし、引き戻しでカバーが空いても展開に戻さない。代替策はユーザーの台数引き上げとペア選択の切替である
2. 機械数は実数（`RunCycles × CycleTime / 60`）で集計する。切上台数で割る方法も必要台数の結果は同値（`Ceil(Ceil(x)/k) = Ceil(x/k)`、k は正の整数）だが、帳簿は実数基準で統一する
3. `RequiredDispenserCount` は `Ceil((実績 + 削減) / CoverableMachines)` の見積もりとする。停止したランの入力需要は展開されないため、停止側の下流にある他環境の自動値は真の必要台数より小さく出うる。0 台・少台数指定時の表示補助としてこの見積もりでよい
4. 0 台指定などで全量停止した環境も環境要件の行に出す（削減機械数が 0 より大きい環境）。UI が行を失うと自動値が見えず台数を戻せないためである
5. カバー容量の占有順は展開順（`RunOrder` 先頭から）とする

## 5. テスト

[test-specification-phase26.md](test-specification-phase26.md) に従う。
計算・検証は Domain・Application・Infrastructure の xUnit で検査し、ENV 群の改訂と新規を含めて `dotnet test` 全緑を確認する。
UI の入力上限・ヒント・未充足の見え方はブラウザ E2E で確認する。
ブラウザプレビューによるユーザー確認は実装後・手動検査項目の実施前に挟み、フィードバックを検査へ反映する（ui-mock-first ルール、implementation-plan.md §5「UI の確認」に従う）。

## 6. 受け入れ条件

- 同梱マスタ（env-stable・env-acrid、CoverableMachines=4）で、息壌 31/分 は散布機自動 1 台・不活性ガス 6/分 の現状と同じ結果になる。息壌 150/分 では機械数 5.0 から散布機自動 2 台になる
- 散布機台数を自動値未満に下げると、カバー不足の機械分が未充足として出て警告が出る。0 台は環境を要する生産を全量未充足にし、環境の入力行は「自動 N 台まで」のまま残る
- 台数を必要台数へ戻すと未充足が解消される。代替レシピへの自動切替は起きない
- `dotnet build` と `dotnet test` が全緑である
