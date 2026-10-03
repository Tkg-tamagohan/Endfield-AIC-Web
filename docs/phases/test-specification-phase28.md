# Phase 28 テスト仕様

**対象フェーズ**: Phase 28（ペア既定選択の同率キー順）
**前提ドキュメント**: [implementation-plan-phase28.md](implementation-plan-phase28.md)、[decision-records.md](../decision-records.md)（仕様決定 BT。関連: U・P）
**関連ドキュメント**: [test-specification-phase2.md](test-specification-phase2.md)（ペア既定選択 U の先行群）、[test-specification-phase21.md](test-specification-phase21.md)（レシピ順位付け BA の先行群）

> 本書は Phase 28 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 自動テストは `SelectionTests.cs` の SEL- 連番を継続（SEL-23〜。着手時に現行最大を再確認）。仕様の読み替えが起きる既存 ID は「改訂」と記す。

## 1. モデル検査（xUnit）

### 既定ペアの同率キー順（SEL）: SelectionTests

| ID | 内容 | 条件 | 期待 |
|---|---|---|---|
| SEL-06 | 改訂。同 `CycleTime` で固定消費の有無・量が同じなら従来どおり `EnvironmentId=null` を優先（U の BT 改定後も維持） | F-03、`i-w` 60/分 | ペア (f-c, 6秒, env=null, FC なし)。環境・燃料需要は発生しない（検査値は旧規則と同じ） |
| SEL-23 | 同 `CycleTime` で「環境あり・FC なし」は「環境なし・FC あり」に勝つ（BT） | F-03、`i-w2` 60/分 | ペア (f-a, 6秒, env-w) で稼働。env-w の散布機と i-gas-w 需要が出て、i-fuel-w 需要は出ない |
| SEL-24 | 両方 FC ありでは `RatePerMinute` 小さい方が `EnvironmentId` に先立つ（BT） | F-03、`i-w3` 60/分 | ペア (f-a, 6秒, env-w, FC 10/分) で稼働。f-b (FC 30/分) は使われない |
| SEL-25 | `ListCandidates` の候補順と `IsDefault` が新規則と一致（BT） | F-03、`i-w2` の候補列挙 | 先頭が (f-a, env-w) で `IsDefault` が立ち、続きが f-b → f-c の順 |

### 回帰（据置）

| ID | 内容 |
|---|---|
| SEL-01〜05・07〜22 | 据置。レシピ順位付け（F・BA）とペア上書き（P）とイベント適格は本改定の対象外で、検査値は変わらない |

## 2. 手動確認項目

同梱マスタに複数ペア行を持つレシピはなく、UI 経路では新規則の差分を再現できない。手動 E2E は設けず、xUnit で完了とする。
