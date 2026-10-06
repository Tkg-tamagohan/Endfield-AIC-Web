# Phase 17 テスト仕様

**対象フェーズ**: Phase 17（グラフの輸送容量チェック絞り込みと設備台数分表示）
**前提ドキュメント**: [implementation-plan-phase17.md](implementation-plan-phase17.md)、[decision-records.md](../decision-records.md)（仕様決定 AN・AO）
**関連ドキュメント**: [test-specification-phase15.md](test-specification-phase15.md)（FG 先行群）、[test-specification-phase2.md](test-specification-phase2.md)（TRN 先行群）

> 本書は Phase 17 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。

## 1. モデル検査（`FlowGraphModelBuilderTests`）

| ID | 対象 | 条件 | 期待 |
|---|---|---|---|
| FG-17 | 設備の台数分展開（AO。Phase 30 の仕様決定 BV で容量超過の検査を除去する改訂。現行は [test-specification-phase30.md](test-specification-phase30.md) §1 を参照） | A-02、i-t 72/分 → 実数 2.4 台 → 切上げ 3 台。expandFacilities | `facunit:f-t#0..#2` の 3 ノード。占有比率の注記（稼働 100%・100%・40%）。入力エッジは占有比で分割（120・120・48/分）、出力も同様（30・30・12/分） |
| FG-18 | 固定消費のユニット分割（AO） | FG-07 と同形（切上げ 2 台・固定消費 12/分）。expandFacilities | ユニットあたり 12/分の FixedConsumption エッジ（合計 24/分で不変） |
| FG-19 | 散布機ユニット（AO） | 環境を要する稼働レシピ 2 つ → 散布機 2 台。expandFacilities | `facunit:f-disp#0`・`facunit:f-disp#1` が「散布機」注記を持ち、環境消費エッジが散布機台数で等量分割される |
| FG-20 | レーン増設で解消できる集計超過は対象外（AN・AO。Phase 30 の仕様決定 BV で改訂。容量超過の検査を除去し、現行は [test-specification-phase30.md](test-specification-phase30.md) §1 を参照） | 入力合計 60/分・切上げ 3 台・ユニット入力 20/分の設備 | 集約表示・台数分表示のどちらでも容量超過フラグなし（Phase 30 で撤去済みの判定） |
| FG-21 | 切上げ 1 の設備は展開しない（AO） | 切上げ 1 台の設備。expandFacilities | `fac:<Id>` 単一ノードのままで、ユニットノードを作らない |
| FG-22 | 設備 ID の `#` とユニットノードの衝突回避（AO） | 設備 `m`（2 台）と設備 `m#1`（1 台）が共存。expandFacilities | `fac:m#1`（実設備）と `facunit:m#0`・`facunit:m#1`（ユニット）が別ノード。集約表示でも `fac:m`・`fac:m#1` が別ノード |
| FG-23 | 未調整ビューのユニット流量（AN・AO。Phase 30 の仕様決定 BV で改訂。容量超過の検査を除去し、現行は [test-specification-phase30.md](test-specification-phase30.md) §1 を参照） | 実数 1.1 台（ユニット当たり実機械 25/分・ベルト容量以下）の設備。unadjusted & expandFacilities | スケーリング後の機械数で再割当され、各ユニット 25/分。容量超過フラグなし（Phase 30 で撤去済みの判定） |
| FG-24 | 同一アイテムの複数ラン入力はユニットで合算（AN。Phase 30 の仕様決定 BV で改訂。容量超過の検査を除去し、現行は [test-specification-phase30.md](test-specification-phase30.md) §1 を参照） | 2 レシピが同一ユニットへ同じアイテムを各 20/分入力 | 合計 40/分で容量超過フラグ（個別エッジ 20/分では発火しないことの確認。Phase 30 でフラグの断言を撤去し、入力合算の検査のみ残る） |
| FG-25 | 未調整ビューで共有ユニットの機械配置に従う（AN・AO。Phase 30 の仕様決定 BV で改訂。容量超過の検査を除去し、現行は [test-specification-phase30.md](test-specification-phase30.md) §1 を参照） | 同じ設備を 0.9 台ずつ使う 2 レシピ（ユニット当たり実機械 24/分）。unadjusted & expandFacilities | スケーリング後の機械数で再割当され、ユニット入力は各 24/分。容量超過フラグなし（等量分配だと 36/分で誤発火する値。Phase 30 で撤去済みの判定） |

### 廃止するケース

Phase 30（仕様決定 BV）で容量超過フラグの付与を撤去したため、成立しなくなった項目。

| ID | 対象 | 条件 | 期待 |
|---|---|---|---|
| FG-13 | 容量超過フラグの対象（AN で改訂後、Phase 30 の仕様決定 BV で撤去） | A-02、i-t 60/分 → i-u のユニット入力 120/分 がベルト 30 個/分を超過 | i-u→f-t の RecipeInput エッジと item:i-u・fac:f-t の両端ノードにフラグ。f-t→i-t の RecipeOutput にはフラグなし |
| FG-15 | 出力・需要だけの超過は対象外（AN。Phase 30 の仕様決定 BV で撤去） | A-02、i-t 60/分（i-t の産出・需要は容量を超えるが、設備への入力ではない） | item:i-t に容量超過フラグなし。i-t への流入エッジ（RecipeOutput）にもフラグなし |
| FG-16 | 環境消費の入力も判定対象（AN。Phase 30 の仕様決定 BV で撤去） | A-01、i-gas の散布機 1 台あたり消費 360/分 がパイプ 60 個/分を超過 | i-gas→f-disp の EnvironmentConsume エッジと両端ノードにフラグ |
| FG-26 | TransportKind=None の消費は容量判定対象外（AN。Phase 30 の仕様決定 BV で撤去） | 固定消費・環境消費が仮想アイテム（None）の計画 | 入力エッジ・ノードとも容量超過フラグなし |

## 2. 計算機の警告検査（`TransportCapacityTests`、TRN 系の改訂）

警告もグラフと同じく「設備 1 ユニットへの入力流量 > 容量」で判定する（仕様決定 AN）。
Phase 30 の仕様決定 BV で輸送容量の警告自体を撤去したため、本節の項目は系列ごと廃止した。

### 廃止するケース

| ID | 対象 | 条件 | 期待 |
|---|---|---|---|
| TRN-01 | ユニット入力のベルト超過は警告 | F-06、i-t 60/分 → f-t のユニット入力 i-u 120/分 | `TransportCapacityExceeded`（i-u について） |
| TRN-02 | 散布機入力のパイプ超過は警告 | F-10、i-std 10/分 → 散布機 1 台の i-gas 360/分 | `TransportCapacityExceeded`（i-gas について） |
| TRN-03 | TransportKind=None は対象外（従来どおり） | F-08、`i-none-item` 500/分 | 容量警告なし |
| TRN-04 | 上限ちょうどは警告なし | F-06、i-t 7.5/分 → ユニット入力 i-u 30/分 ちょうど | 警告なし |
| TRN-05 | 警告文は個/分表記（ユニット基準） | F-06、i-t 60/分 | 警告文が「個/分」・「設備 1 台への入力流量」を含み、レーン数を含まない |
| TRN-06 | レーン増設で解消できる集計超過は警告なし | F-08、`i-belt-item` 45/分（集計 45/分だがユニット入力は 10/分） | 警告なし |

## 3. 手動確認項目

| ID | 内容 | 手順 | 期待 |
|---|---|---|---|
| MN-48 | 台数分表示スイッチ | グラフ表示中に「設備を台数分表示」を切り替える | 設備ノードが切上台数ぶんのユニットへ展開・集約が切り替わる |

### 廃止するケース

Phase 30（仕様決定 BV）で容量超過フラグの付与を撤去したため、成立しなくなった項目。

| ID | 内容 | 手順 | 期待 |
|---|---|---|---|
| MN-49 | 容量超過の見え方 | ユニット入力が容量を超える計画を表示する | 入力エッジと両端ノードが赤化。集約表示と台数分表示で一致する。出力側だけの超過は赤くならない |

## 4. 確認項目の結果記録

実装 PR の本文に、`dotnet test` の結果と §1〜§3 の各項目の合否を表で記録する。
