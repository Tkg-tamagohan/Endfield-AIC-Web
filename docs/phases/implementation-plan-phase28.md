# Phase 28 実装詳細計画

**対象フェーズ**: Phase 28（ペア既定選択の同率キー順）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 BT。関連: U・P）
**関連ドキュメント**: [test-specification-phase28.md](test-specification-phase28.md)（本 Phase のテスト仕様）、[implementation-plan-phase2.md](implementation-plan-phase2.md)（ペア既定選択 U の先行型）、[implementation-plan-phase21.md](implementation-plan-phase21.md)（レシピ順位付け BA の先行型）

> 本書は Phase 28 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

- 既定ペア選択の同率キーを `EnvironmentId=null` → `FixedConsumption` なし/小 から、`FixedConsumption` なし/小 → `EnvironmentId=null` へ入れ替える（仕様決定 BT、U の改定）
- 入れ替えで選択が変わる組合せをカバーするテストを追加し、旧順序を前提にした既存テストの記述を追従させる

### やらないこと

- `CycleTime` 最小を第一キーとする既定、`FacilityId` 昇順の最終タイブレーク、ペア上書きの一意キーと不適格時のフォールバック（P）は変更しない
- レシピ側の順位付け（`VersionAdded` → 実効出力レート → `Id` 昇順。F・BA）は変更しない
- 実効出力レートの算出式（適格ペアの最小 `CycleTime` のみを見る）は変更しない
- 採取素材の採取優先（AD）、同一レシピの別ペア衝突時の先勝ち（仕様決定 R）は変更しない

## 2. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `Calculation/PairSelector.cs` | `OrderPairs` で `FixedConsumption` のキーを `EnvironmentId` のキーより先に評価するよう入れ替え、クラスとメソッドの XML コメントの順序記述を新規則へ追従させる |

### テストフィクスチャ

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/CalculationFixtures.cs` | F-03 に、同 `CycleTime` で固定消費の有無が食い違う需要アイテムとレシピ（`i-w2`/`r-w2`、`i-w3`/`r-w3`）を追加する |

### テスト

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/SelectionTests.cs` | SEL-06 の表示名を新規則へ改訂し、SEL-23〜25 を追加する |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | BT（本計画で追加済み） |
| `docs/requirements.md` | §4.1 への反映（済） |
| `docs/implementation-plan.md` | §4 の計算手順の記述と Phase 28 行（済）。実装 PR でチェックを `[x]` にする |
| `docs/phases/test-specification-phase2.md` | SEL-06 の行は新規則の改訂注記を test-specification-phase28.md 側に持つため、本文書は改訂しない |

## 3. 変更詳細

### 3-1. 既定ペアの順序規則

`OrderPairs` の並びを次へ改める。

```
CycleTime 昇順
→ FixedConsumption なし優先
→ FixedConsumption.RatePerMinute 昇順
→ EnvironmentId=null 優先
→ FacilityId 昇順（序数比較）
```

旧規則との差異は「固定消費の有無と量が異なるペア同士が同 `CycleTime` で競合し、かつ `EnvironmentId` 優先側が固定消費で劣る」組合せでのみ現れる。
典型は「環境あり・固定消費なし」対「環境なし・固定消費あり」で、旧規則は環境なし側を、新規則は環境あり側を既定にする。
`Select` の既定ペアと `ListCandidates` の候補順・`IsDefault` は同じ `OrderPairs` を共有するため、代替選択ドロップダウンの表示順と「（既定）」併記は変更に自動で追従する。

### 3-2. F-03 の追加分

F-03 に次のアイテムとレシピを追加する。
`r-w`（SEL-06）の 3 ペア構成では新旧とも f-c が既定になり規則差が現れないため、差が出る組合せを別レシピで用意する。

- `i-w2` / `r-w2`: ペアは (f-a, 6秒, env-w, FC なし)・(f-b, 6秒, env=null, FC i-fuel-w 30/分)・(f-c, 6秒, env=null, FC i-fuel-w 60/分)。旧規則は f-b、新規則は f-a を既定とする
- `i-w3` / `r-w3`: ペアは (f-a, 6秒, env-w, FC i-fuel-w 10/分)・(f-b, 6秒, env=null, FC i-fuel-w 30/分)。旧規則は f-b、新規則は f-a を既定とする。`RatePerMinute` の小さい方が `EnvironmentId` より先に評価されることの検査

### 3-3. 既存テストへの影響

- SEL-06（`r-w`: (f-a, 6秒, env-w, FC なし)・(f-b, 6秒, env=null, FC 30/分)・(f-c, 6秒, env=null, FC なし)）は新旧どちらの規則でも f-c が既定で、検査値は変わらない。「固定消費の有無と量が同じ同率では従来どおり `EnvironmentId=null` が残る」ことを示す改訂ケースとして扱い、DisplayName を新規則へ改める
- `ListCandidates` の候補順も変わるため、`i-w2` で候補順と `IsDefault` を SEL-25 で検査する
- 同梱マスタ（`data/master.json`）の全レシピはペア 1 行のみで、実データ上の既定選択は変わらない

## 4. 影響の確認

- `OrderPairs` を通るのは `Select`（既定ペア）と `ListCandidates`（候補列挙）の 2 系統で、共通規則の入れ替えで両方が揃う
- ペア上書きの照合（`Matches`。一意キー P）と適格判定（`IsPairEligible`）は順序規則と無関係で変わらない
- `EffectiveRate`（レシピ順位付け）は適格ペアの最小 `CycleTime` だけを見るため無関係
- 候補ラベル（`ResultViewText.OptionLabel`）は順序規則を持たず、「（既定）」は `IsDefault` 由来で自動追従する
- 同梱マスタに複数ペア行を持つレシピはなく、公開アプリと管理ツールの既定動作に実データ上の差分は出ない

## 5. 受け入れ条件

- 同 `CycleTime` で「環境あり・固定消費なし」のペアが「環境なし・固定消費あり」のペアに勝って既定になる
- 固定消費の有無と量が同じなら従来どおり `EnvironmentId=null` が優先される
- `dotnet test` 全緑
