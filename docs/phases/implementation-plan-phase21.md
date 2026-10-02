# Phase 21 実装詳細計画

**対象フェーズ**: Phase 21（既定レシピ選択への必要設備数条件の追加）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 F・U・L、追加予定の仕様決定 BA）
**関連ドキュメント**: [test-specification-phase21.md](test-specification-phase21.md)（本 Phase のテスト仕様）、[implementation-plan-phase2.md](implementation-plan-phase2.md) §3（選択規則の現行構成）

> 本書は Phase 21 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。
> マスタ構成（モデル、スキーマ、JSON）は変更しない。計算ドメインの選択規則のみを対象とする。

## 1. スコープ

### やること

適格レシピの既定選択順へ「必要設備数が少ない」を第二キーとして追加する（仕様決定 BA）。
炭塊のように `VersionAdded` が同率の複数レシピが存在する場合、Id 昇順ではなく出力効率の高いレシピが既定になる。

### やらないこと

- ペア行の既定選択（仕様決定 U）の変更
- ペア上書き・`InvalidPairOverride` フォールバック・計画内レシピ→設備一意（旧 BN）の変更
- 採取優先（仕様決定 AD）や循環処理（AQ・AR）の変更
- マスタデータの変更。`recipe-carbon04` は高レートにより自動的に既定へ変わる

## 2. 確定した仕様（2026-10-02 確定）

1. 既定選択順は `VersionAdded` 降順 → **実効出力レート降順** → `Id` 昇順とする
   - 実効出力レート = 需要対象 `itemId` の 1 サイクル出力量合計 ÷ 適格ペアの最小 `CycleTime` × 60（個/分）
   - 「必要設備数が少ない」の比較は実数機械数（残需要 ÷ 出力レート）で行う。残需要は候補間で共通なため、実装は出力レート降順の比較と同値。需要量に依存しない決定的な既定とする
2. レート算出に使うペアは、適格ペアのうち `CycleTime` 最小のもの（仕様決定 U の先頭と一致）
3. 散布機台数は台数比較に含めない（生産設備のみ）
4. 同率は `Id` 昇順（従来どおり）
5. 出力レートは需要対象 `itemId` のみの出力量で計算する（主産物・副産物を問わない）
6. 適格ペア 0 件のレシピは実効レート 0 として最下位に並べる（従来の次点フォールバックと整合）

## 3. 実装構成

### Domain 層（`PairSelector.cs`）

- `OrderCandidates` のシグネチャへ `master`・`context` を追加し、候補ごとに実効出力レートを計算する
- 順序は `VersionAdded` 降順 → 実効出力レート降順 → `Id` 昇順
- `Select` と `ListCandidates` は引き続き共通の `OrderCandidates` を使うため、UI の候補順と `IsDefault` 印は同一規則に自動追従する
- `Select` の引数に残需要は追加しない（レート比較は需要量に依存しないため不要）

### 呼び出し側（`ProductionCalculator.cs`）

- 変更なし。`Selection` キャッシュ・`PairConflict`・採取優先・循環処理は維持する

### 文書

- 本書と `test-specification-phase21.md` を作成する
- `decision-records.md` に仕様決定 BA を追加する
- `requirements.md` §4.1-2 の既定選択順の記述を改訂する
- `implementation-plan.md` に Phase 21 節を追加する
- `docs/phases/test-specification-phase2.md` の SEL 系記述へ、第二キー改定の経緯を脚注等で残す

## 4. 既存テストへの影響予測

- `F-03` フィクスチャ: `r-y-a`/`r-y-b` は同一出力・同一 `CycleTime` で同レートのため SEL-03（Id 昇順）は不変。`r-x-*` 系はバージョン差があるため不変
- 同バージョン・レート差のある同一アイテム複数レシピを持つ既存フィクスチャがあれば期待値変更の可能性がある。実施時に全フィクスチャを走査して確認する
- 手動確認 MN-50（炭塊計算で芽針経路が並ぶ）の期待値は本改定後の既定と一致する。現行の既定（サンドリーフ経路）とは一致しない点に注意する

## 5. テスト

[test-specification-phase21.md](test-specification-phase21.md) に従う。
自動テストは `SelectionTests.cs` に SEL-14 以降の連番で追加し、新規フィクスチャ F-18 を `CalculationFixtures.cs` に作る。
手動確認はブラウザプレビューで行う（ui-mock-first ルールに従いユーザー確認を先に取る）。

## 6. 受け入れ条件

- `VersionAdded` 同率のレシピ間で、実効出力レートの高いレシピが既定になる
- 同梱マスタで炭塊の既定が芽針系（`recipe-carbon04`）になる
- `dotnet build`・`dotnet test` が全緑である

## 7. 文書反映の文案

### `decision-records.md` への追加行

```
| BA | 既定レシピ選択の第二キー | 同一 `VersionAdded` の複数レシピが候補のとき、需要対象アイテムの実効出力レート（1 サイクル出力量 ÷ 適格ペアの最小 `CycleTime` × 60 個/分）が高いレシピを優先する。同一需要に対する必要機械数の実数比較と同値であり、切上台数では比較しない。同率は `Id` 昇順、適格ペア 0 件はレート 0 で最下位。散布機などのペア外設備は台数に含めず、副産物を含む他アイテムの出力量も計算に入れない。既定ペア規則（U）とユーザー上書きは変更しない（F の改定） |
```

### `requirements.md` §4.1-2 の改訂文

```
2. **レシピ選択**: 需要アイテムごとに既定で `VersionAdded` 最新のレシピを選び、同率は実効出力レート（対象アイテムの 1 サイクル出力量 ÷ 適格ペアの最小 `CycleTime`）の高いレシピを優先し、さらなる同率は `Id` 昇順とする（仕様決定 F・BA）。設備は `CycleTime` 最小のペアを選ぶ。複数レシピ・複数設備がある場合はユーザーがアイテム単位で切り替えられる。同一レシピ×同一設備で属性の異なるペア行が複数ある場合は `CycleTime` 最小を既定とし、同率は `EnvironmentId=null` → `FixedConsumption` なし/小の順で優先する。代替選択はペア行単位で提示する（仕様決定 U）。
```

### `implementation-plan.md` への追加節

```
### Phase 21: 既定レシピ選択への必要設備数条件の追加（PR: 同バージョンレシピの効率順位付け）

- [ ] 適格レシピの既定順を `VersionAdded` 降順 → 実効出力レート降順 → `Id` 昇順へ改める（仕様決定 BA）
- [ ] `PairSelector` の候補順序付けに実効レート比較を追加し、選択と UI 候補順を同一規則に保つ
- [ ] 選択規則のテスト（SEL-14〜）を追加する
- [ ] requirements.md・decision-records.md を同期する
```

## 8. 留意点

- Phase 番号 21 はユーザー指定（Phase 20 は別スレッド）。仕様決定 ID（BA）とテスト ID（SEL-14〜・MN-70〜）は着手時に main で再確認する
- 旧リポジトリの決定との対応: 本改定は「需要アイテムへの既定選択規則（旧 I/BC、仕様決定 R で継承）」の第二キーを差し替えるものであり、継承の趣旨（ユーザーが代替選択できる既定の提示）と矛盾しない
