# Phase 30 実装詳細計画

**対象フェーズ**: Phase 30（輸送容量超過の警告とグラフ赤化の撤去）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 BV。関連: AM・AN・AO）
**関連ドキュメント**: [test-specification-phase30.md](test-specification-phase30.md)（本 Phase のテスト仕様）、[implementation-plan-phase17.md](implementation-plan-phase17.md)（判定基準 AN と台数分表示 AO の先行型）、[implementation-plan-phase26.md](implementation-plan-phase26.md)（末尾集約と機械群評価の先行型）

> 本書は Phase 30 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。
> 番号は依頼時の仮採番どおり 30 とする。Phase 29 は本書作成時点で未割当であったが、後に「レシピの Id・名前の自動入力」として確定した（仕様決定 BU）。本書の仕様決定は計画時の仮採番 BU から BV へ移した。

## 1. スコープ

### やること

- 「設備 1 ユニットへの同一アイテム入力流量 > 輸送容量（ベルト 30 個/分・パイプ 60 個/分）」の警告（`TransportCapacityExceeded`）と、同一判定によるフローグラフの赤化（`OverCapacity` のエッジと両端ノード）を撤去する（仕様決定 BV、AN の撤去）
- 判定のためだけに存在する機構を併せて撤去する。対象は発火経路 `AddTransportWarnings`、機械群評価 `FacilityUnitLayout.MaxMachineInputs` とその記録系 `FacilityUnitOverspill`/`OverspillGroups`、容量定数 `BeltCapacityPerMinute`/`PipeCapacityPerMinute`、レコード項目 `OverCapacity`、描画側の赤色分岐
- 容量警告を前提にした既存テストを整理し（TRN 系列の廃止・FG の容量系アサーション除去）、撤去の回帰ケースを追加する
- スキル文書の容量警告の記述を追従させる（`.devin/skills/testing-blazor-apps` と個人プラグインスキル）

### やらないこと

- `Item.TransportKind` とその編集 UI・スキーマ項目は変更しない（仮想アイテム規則と生産リスト候補の絞り込みで使用中）。スキーマの description の表現のみ追従する
- `FacilityUnitLayout.Allocate`（ユニット割当・`RunShares`・`Used`・散布機ユニット・末尾集約・`MaxUnitSlots`）は台数分表示（仕様決定 AO）で使用中のため残す。末尾へ集約する動作そのものも表示用に残る
- 容量の仕様値（ベルト 30 個/分・パイプ 60 個/分）は仕様決定 AM の仕様として残す。復帰時の参照は残課題へ記録済み
- 判定の休眠維持は採らない。復帰は大型化学反応炉の実装時に入出力ポートのモデル化とあわせて再設計する（remaining-issues.md 参照）

## 2. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `Calculation/CalculationWarning.cs` | `WarningCode.TransportCapacityExceeded` を削除する |
| `Calculation/ProductionPlanAggregator.cs` | `AddTransportWarnings` の呼び出しとメソッド本体を削除する |
| `Calculation/ProductionCalculator.cs` | `BeltCapacityPerMinute`・`PipeCapacityPerMinute` を削除し、クラスコメントの「輸送容量」の言及を外す |
| `Calculation/FacilityUnitLayout.cs` | `MaxMachineInputs` と補助の `AddInputs`・`MergeMaxInputs` を削除する。`FacilityUnitOverspill` レコード・`FacilityUnitSlot.OverspillGroups`・`Allocate` 末尾の記録ブロックを削除する。共用の言及（「台数分表示と輸送容量超過判定で共用する」等）をコメントから外し、末尾集約は表示用に残る旨へ書き換える |
| `Models/Item.cs` | `TransportKind` のコメントの「輸送容量対象外」を、残る用途（仮想アイテム・レシピ入力不可）へ書き換える |

### Application / SharedUi / JS

| ファイル | 変更 |
|---|---|
| `Application/FlowGraphModelBuilder.cs` | `inputTotals`・`overInputs` の積み上げ経路（`MaxMachineInputs` 呼出と入力エッジの合算）・`overCapacityItems`/`overCapacityFacilities`・`TransportCapacityOf` を削除し、レコード引数から `OverCapacity` を外す（ノード・エッジとも）。コメントの容量判定記述を外す |
| `SharedUi/Components/FlowGraph.razor` | `fnode-alert` 条件から `n.OverCapacity` を外し、未充足のみを条件に残す |
| `SharedUi/wwwroot/js/flow-graph.js` | `OVER_COLOR` とエッジ色の `overCapacity` 分岐を削除し、エッジは種別色のみに戻す |

### データ・スキーマ

| ファイル | 変更 |
|---|---|
| `data/master.schema.json` | `TransportKind` の description の「輸送容量対象外」を残る用途へ書き換える |

### テスト

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/TransportCapacityTests.cs` | ファイルごと削除する（TRN-01〜11 の廃止） |
| `tests/EndfieldAicWeb.Domain.Tests/WarningTests.cs` | WRN-07・WRN-08 を追加する（撤去の回帰） |
| `tests/EndfieldAicWeb.Domain.Tests/CalculationFixtures.cs` | TRN-03・TRN-06 でのみ使われていた F-08 を削除する |
| `tests/EndfieldAicWeb.Domain.Tests/PlanAssert.cs` | `NoWarningsExcept` は本件の 4 呼出が唯一の利用であり、撤去で未使用になるため削除する |
| `tests/EndfieldAicWeb.Domain.Tests/GatherCapTests.cs` | `NoWarningsExcept(plan, WarningCode.TransportCapacityExceeded)` の 3 呼出を `Assert.Empty(plan.Warnings)` へ置き換える |
| `tests/EndfieldAicWeb.Domain.Tests/ExpansionTests.cs` | 同上の 1 呼出を置き換える |
| `tests/EndfieldAicWeb.Application.Tests/FlowGraphModelBuilderTests.cs` | FG-13・FG-15・FG-16・FG-26 を削除する。FG-17・FG-20・FG-23・FG-24・FG-25 は `OverCapacity` アサーションのみ除去して残す |

### 文書・スキル

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | BV（本計画で追加済み） |
| `docs/requirements.md` | §4.2 の容量警告の箇条を削除、§5.3 `TransportKind` の説明と §10 仮想アイテムの説明を残る用途へ同期（いずれも計画 PR で反映済み） |
| `docs/implementation-plan.md` | §3 の出力一覧と手順 9 から容量判定の記述を除外、Phase 30 行を追加（いずれも計画 PR で反映済み）。実装 PR でチェックを `[x]` にする |
| `docs/remaining-issues.md` | 大型化学反応炉の搬入・搬出レート判定の復帰項目（計画 PR で追加済み） |
| `.devin/skills/testing-blazor-apps/SKILL.md` | 輸送容量の発火例の箇条と個/分表記の言及を撤去後の手順へ追従させる |
| 個人プラグインスキル（`fixture-injection-blazor-testing`・`testing-blazor-apps-flow-graph`） | 容量警告の発火レシピ設計と容量系フィクスチャの記述を撤去後へ追従させる（`manage_plugin` で更新） |

## 3. 変更詳細

### 3-1. 撤去する判定とその経路

仕様決定 AN の判定は「設備 1 ユニットへの同一アイテム入力合計（レシピ入力＋固定消費＋環境消費）> `TransportKind` 容量」を警告し、グラフでは同判定で入力エッジとその両端ノードを赤化していた。
仕様決定 BV でこの判定ごと撤去する。
警告側は `Aggregate` からの呼び出しと `AddTransportWarnings` 全体を削除する。メソッド内でユニットごとの機械定数（固定消費・環境消費の等量加算）を組み立てる `flatInputs` 経路と、末尾集約の機械群を仮想機械へ充填して評価する `MaxMachineInputs` も呼び出し元がなくなるため同時に削除する。
グラフ側は `inputTotals` の積み上げ（構成エッジの合算＋`MaxMachineInputs`）・`overInputs`・`overCapacityItems`/`overCapacityFacilities`・`OverCapacity` フラグ・`fnode-alert` の `OverCapacity` 条件・`OVER_COLOR` 分岐を削除する。

### 3-2. 残す機構

- `FacilityUnitLayout.Allocate` は台数分表示（AO）のユニット割当である。`RunShares`・`Used`・散布機ユニット・末尾への集約（`RunShares` への按分マージ）は残る。`OverspillGroups` の記録のみ容量評価専用のため削除する
- `Item.TransportKind` は仮想アイテム規則（`MasterValidator` でレシピ入力に None を含めない）・計算ページの目標候補の絞り込み（`TransportKind != None` の除外）・管理ツールの編集欄・スキーマ項目で使用中のため残る
- `FacilityUnitLayout.MaxUnitSlots` はユニット実体化の防御上限であり、表示側でも発散計画のスロット爆発を防ぐため残る
- `fnode-alert` は未充足表示の条件として残る

### 3-3. テストの整理

- TRN-01〜11 は警告の発火・非発火・警告文・末尾集約の機械群判定を検査する系列であり、判定の撤去で全件が成立しなくなるため系列ごと廃止する（ファイル削除）。TRN-09〜11 が前提として利用していた末尾集約は表示用に残るが、集約機械群を仮想機械へ充填する評価は本 Phase で消えるため、系列内の前提検査ごと不要になる
- FG の容量専用ケース（FG-13・FG-15・FG-16・FG-26）は削除する
- FG-17・FG-20・FG-23・FG-25 は容量フラグのアサーションのみ除去し、ユニット展開と流量分割の検査は残す
- FG-24 もフラグの除去で改訂に留め、同一設備への同一アイテムの複数ラン入力が集約表示で 1 本のエッジに合算される検査は残す
- WRN-07・WRN-08 を撤去の回帰として `WarningTests.cs` へ追加する

## 4. 影響の確認

- 同梱マスタ 0.2.8 では `recipe-cupriumCanister`（成形機へ赤銅塊 60 個/分、ベルト超過）と `recipe-heavyXiragen01`（精錬炉へ分離コア 60 個/分、ベルト超過）の 2 件で警告が発火しうる構成が含まれている。撤去後は両レシピを含む計画で警告欄にもグラフにも容量超過の表示が出ない
- `OverCapacity` のフィールド削除はレコードの位置引数の変更である。レコードを構築するのは `FlowGraphModelBuilder` 内部のみで、JS 側はプロパティの不在で種別色へ戻る
- `WarningBag` と他の `WarningCode` は影響を受けない
- 管理ツールの「輸送種別」欄・`TransportKind` の編集・スキーマ項目は従来どおり使える

## 5. 受け入れ条件

- ベルト 30 個/分・パイプ 60 個/分を超える入力を持つ計画で、警告一覧にもフローグラフにも容量超過の表示が出ない
- 台数分表示（AO）のユニット展開・稼働率・散布機ユニットの表示は従来どおり
- `dotnet test` 全緑
- ブラウザで `recipe-cupriumCanister` を含む計画を確認し、警告欄に輸送容量の警告がなくグラフに赤いエッジと警告色ノード（未充足由来を除く）が出ない

## 6. 残課題

- 大型化学反応炉（化学反応炉のレシピを複数同時実行できる設備、現在未登録）の実装時に搬入・搬出のレート判定を復帰する。入出力ポートのモデル化（W で廃止済みのため再定義から要る）とあわせた再設計とする。[remaining-issues.md](../remaining-issues.md) の「大型化学反応炉の搬入・搬出レートと輸送容量判定の復帰」へ記録済み
