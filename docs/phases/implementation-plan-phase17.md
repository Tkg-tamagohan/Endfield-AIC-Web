# Phase 17 実装詳細計画

**対象フェーズ**: Phase 17（グラフの輸送容量チェック絞り込みと設備台数分表示）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AN・AO）
**関連ドキュメント**: [test-specification-phase17.md](test-specification-phase17.md)（本 Phase のテスト仕様）

> 本書は Phase 17 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

公開アプリの生産フローグラフ（Phase 15）の表示ルールを 2 点改訂する。

- 輸送容量超過の判定を「設備 1 ユニットへの入力流量 > 容量」に限定する（仕様決定 AN）。台数・レーンを増やして解消できる集計超過は対象外で、生産出力・採取供給・目標需要だけの超過も対象外とする。容量は従来どおり `TransportKind` の定数（ベルト 30 個/分・パイプ 60 個/分、仕様決定 AM）を使う
- 設備ノードを実設置台数（`FacilityRequirement.CeilCount`）ぶん個別に描く切替をグラフに追加する（仕様決定 AO）。操作子はグラフ表示中にだけ出し、既定は従来の集約表示とする

### やらないこと

- 計算本体の警告（`TransportCapacityExceeded`）の発火条件の見直し自体は行う（AN で「設備 1 ユニットへの入力流量 > 容量」へ改訂）。警告文の文言変更や警告の廃止は対象外
- 設備台数・流量の計算ロジック変更。台数分表示とユニット割当は表示・判定の再構成のみで、需要展開や台数の計算結果には触れない
- リスト表示側の変更
- 管理ツールへの展開（Phase 15 と同じく公開アプリのみ）

## 2. 変更一覧

### Domain / Application

| ファイル | 変更 |
|---|---|
| `Domain/Calculation/FacilityUnitLayout.cs`（新設） | 設備を切上台数ぶんのユニットへ割り当てる共有実装。ランの機械数をユニット容量 1.0 へ逐次充填し、散布機を環境ごとの専用ユニットへ振る。計算機の容量警告とグラフで同じ割当結果を使う |
| `Domain/Calculation/ProductionCalculator.cs` | `AddTransportWarnings` をユニット単位の入力流量判定へ改める（仕様決定 AN）。警告文を「設備 1 台への入力流量」表記へ改め、必要レーン数の記述を落とす |
| `Domain/Calculation/CalculationWarning.cs` | `TransportCapacityExceeded` のコメントを新基準へ追従 |
| `Application/FlowGraphModelBuilder.cs` | `Build` に `expandFacilities` 引数を追加。展開時はユニットノード `facunit:<FacilityId>#<n>` へエッジを流量比で分割する。容量超過判定をユニットエッジ単位で行い、集約表示では構成エッジのいずれかが超過していれば集約エッジも赤化する |

### App

| ファイル | 変更 |
|---|---|
| `App/Pages/Home.razor` | グラフ表示中のツールバーに「設備を台数分表示」チェックを追加し、`_graphExpandFacilities` を `FlowGraphModelBuilder.Build` へ渡す |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | AN・AO を追加 |
| `docs/implementation-plan.md` | Phase 17 節を追加 |
| `docs/phases/implementation-plan-phase15.md` | §3・§6 の容量超過ルールに AN による改定注記 |
| `docs/phases/test-specification-phase15.md` | FG-13 の旧基準に AN による改定注記 |

## 3. モデルの変更詳細

### 容量超過判定（AN）

判定は設備ユニットへの入力エッジ単位とする。アイテムノードから設備ユニットへのエッジ（RecipeInput・FixedConsumption・EnvironmentConsume）の流量が、そのアイテムの `TransportKind` 容量を超えるものを `OverCapacity` とする。
集約表示では、集約エッジを構成するユニットエッジのいずれかが超過していればその集約エッジを超過とみなす。台数分表示と集約表示で判定結果が一致する。
超過したエッジの両端ノード（アイテムノードと設備ノード）にも `OverCapacity` を立て、従来どおり赤縁と赤いエッジで表す。
アイテム単位の集計判定（`max(RequiredPerMinute, 生産量, 採取量)`）と、設備からアイテムへ向かうエッジの赤化は廃止する。
計算本体の警告も同じ基準とし、設備 1 ユニットへの入力流量が容量を超えるアイテムにだけ発火する。

### 設備台数分表示（AO）

`expandFacilities=true` のとき、切上台数が 2 以上の設備を台数ぶんのユニットノード（`facunit:<FacilityId>#<0..N-1>`）へ展開する。ユニットノードは設備ノード（`fac:<FacilityId>`）と別のプレフィックスとし、FacilityId に `#` が含まれても衝突しない構造とする。
ユニットへのラン占有の割当は、ランごとの機械数（`CyclesPerMinute × CycleTime / 60`）を `RecipeRuns` の順にユニット容量 1.0 へ逐次充填する。
レシピ入力・出力エッジは、ユニットが受け持つ占有分をラン機械数で割った比率で各ユニットノードへ分割する。
固定消費エッジは全ユニットへ等量（合計 / 台数）に分ける。
環境消費エッジは、その環境の散布機台数ぶんを占める散布機ユニット（ラン占有ユニットの後ろに並ぶ）へ等量に分ける。
ユニットノードの注記は、ラン占有ユニットを「稼働 NN%」（占有比率の百分率）、散布機ユニットを「散布機」とする。
切上台数が 1 以下の設備は展開せず、従来の単一ノードのままとする。

## 4. 確定した UI 上の判断

実装中に変更する場合は本節を更新する。

1. 切替操作子はツールバーのチェックボックス「設備を台数分表示」とし、グラフが開いているときだけ出す（既定はオフ＝従来の集約表示）
2. ユニットノードのクリックは集約ノードと同じく設備行へのスクロールとする（`RefId` は FacilityId）
3. 台数分表示中の容量判定はユニットへの入力エッジごとに行い、集約表示でも同じ判定結果が出る（構成ユニットのいずれかが超過していれば集約エッジも超過）。「台数分表示で正常 ⟺ 集約表示でも正常」が正しい状態（ユーザー確認済み）

## 5. テスト

[test-specification-phase17.md](test-specification-phase17.md) に従う。
`FlowGraphModelBuilder` の新規則を xUnit で採番してカバーする。
ブラウザプレビューによるユーザー確認は実装後に別途挟む（ui-mock-first ルール）。

## 6. 受け入れ条件

- 設備への入力流量が容量を超えるエッジと両端ノードだけが赤化し、出力・採取・目標需要だけの超過は赤化しない
- 「設備を台数分表示」の切替でユニットノードが台数ぶん現れ、流量がユニット比で分割され、散布機ユニットが「散布機」注記を持つ
- `dotnet build` と `dotnet test` が全緑である
