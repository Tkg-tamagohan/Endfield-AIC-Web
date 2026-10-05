# Phase 35 実装詳細計画

**対象フェーズ**: Phase 35（既定レシピ選択の入力効率キー：実効入力レートの第 2 キー化）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 CL。関連: F・U・BA・BT・CB）
**関連ドキュメント**: [test-specification-phase35.md](test-specification-phase35.md)（本 Phase のテスト仕様）

> 本書は Phase 35 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> Phase 番号は 35 とする。仕様決定は CK の次の採番で CL、自動テスト ID は SEL-26 以降・DSP-26 以降、手動確認 ID は MN-145 以降を使う。

## 1. スコープ

### 背景と問題

既定レシピ選択は `VersionAdded` 降順 → 実効出力レート降順 → `Id` 昇順の順で決まる（仕様決定 F・BA）。同梱マスタの重息壌ガスでは、環境を要する `recipe-heavyXiragen02`（精製機・入力 3 個）と環境なしの `recipe-heavyXiragen01`（精錬炉・入力 4 個）が `VersionAdded` 1.4.0・実効出力レート 30 個/分で完全に並び、`Id` 昇順の決め手で入力の多い 01 が既定になっている。入力の少ない側を既定として優先できるよう、選択規則へ入力効率のキーを追加する（仕様決定 CL）。

### やること

- `PairSelector` のレシピ既定順に第 2 キーとして実効入力レート昇順を挿入する（仕様決定 CL）
- `DisposalSelector` の処理レシピ順位にも同じキーを同型適用する（仕様決定 CB の「同型」を維持）
- ペア代替選択の候補列挙（`PairSelector.ListCandidates`）を新しい順序へ追随させる（`OrderCandidates` 共有のため自動で追随する）
- 仕様決定 CL・要件本文・計画・テスト仕様の文書を同期する（計画 PR 側で実施済み）
- xUnit に SEL-26 以降・DSP-26 以降を追加し、手動確認 MN-145 以降を実施する

### やらないこと

- ペア行の既定選択規則（U・BT）は変えない。`FixedConsumption` を入力合計へ含めない
- `VersionAdded` 第一キー（F）・実効出力レート・`Id` 昇順の各キーの定義は変えない
- ユーザーによるアイテム単位のペア上書き（PairOverride）の仕組みは変えない
- 最適化バリエーション（設備台数最小・消費電力最小・採取素材最小・設備面積最小、仕様決定 F の将来構想）の UI は実装しない

## 2. 変更一覧

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.Domain/Calculation/PairSelector.cs` | `OrderCandidates` に実効入力レート昇順を第 2 キーとして追加。`EffectiveInputRate` を新設し、クラス XML コメントの選択規則記述を更新する |
| `src/EndfieldAicWeb.Domain/Calculation/DisposalSelector.cs` | レシピ順位に同じキーを同型適用（全入力合計 ÷ 適格ペアの最小 `CycleTime` × 60 の昇順を、実効処理レート降順の前へ）。XML コメントを更新 |
| `tests/EndfieldAicWeb.Domain.Tests/SelectionTests.cs` | SEL-26 以降を追加（必要なら `CalculationFixtures` に競合フィクスチャを追加） |
| `tests/EndfieldAicWeb.Domain.Tests/DisposalTests.cs` | DSP-26 以降を追加 |
| `docs/decision-records.md` | 仕様決定 CL を追加（計画 PR で実施済み） |
| `docs/requirements.md` | §4.1-2 と §4.8 の選択規則を更新（計画 PR で実施済み） |
| `docs/implementation-plan.md` | Phase 35 のチェックリスト項目を追加（計画 PR で実施済み。完了時に `[x]` へ更新） |

## 3. 設計の詳細

### 3-1. 実効入力レートの定義（仕様決定 CL）

実効入力レート = `recipe.Inputs` の `Quantity` 合計 ÷ 適格ペアの最小 `CycleTime` × 60（個/分）。

- `FixedConsumption` はペア属性であり入力へ含めない
- 適格ペア 0 件は +∞ とし最下位へ（BA の「実効出力レート 0 で最下位」と対称）
- 最小 `CycleTime` は適格ペアのみから取る（イベント不適格の最速ペアは使わない。実効出力レートと同じ前提）

### 3-2. 新しい順序

- 需要アイテム（`PairSelector`）: `VersionAdded` 降順（パース不能は最古）→ 実効入力レート昇順 → 実効出力レート降順 → `Id` 昇順
- 処理レシピ（`DisposalSelector`）: `VersionAdded` 降順 → 実効入力レート昇順（対象アイテムと補助入力を含む全入力合計で算出）→ 実効処理レート降順 → `Id` 昇順

### 3-3. 影響

- 同梱マスタ: 重息壌ガスの既定が `recipe-heavyXiragen01`（入力レート 120 個/分）から `recipe-heavyXiragen02`（90 個/分）へ変わる。ペア候補列挙も 02 が先頭になり `（既定）` ラベルが 02 へ移る。炭塊は 03・04 とも入力レート 30 個/分で同率のため、実効出力レート差で 04 が既定のまま
- 既存テスト: 競合レシピの入力合計が異なるフィクスチャは既定が変わりうる。実装時に `CalculationFixtures` の競合ケースを洗い出して期待値を確認する

## 4. 協議記録（ユーザー確定済み）

| # | 論点 | 決定 |
|---|---|---|
| A | 「入力素材数」の定義 | 実効入力レート（`Inputs` 全量の 1 サイクル合計個数 ÷ 適格ペアの最小 `CycleTime` × 60、個/分）が小さい方を優先。種類数・サイクルあたり個数は採らない |
| B | 挿入位置 | `VersionAdded` の次（第 2 キー）。実効出力レートは第 3 キーへ降格する |
| C | 処理レシピへの適用 | 適用する。入力合計は対象アイテムと補助入力を含む全入力で数える |

前提として確認済み: `VersionAdded` 第一キー維持、`FixedConsumption` は入力に含めない、ペア行規則（U・BT）は対象外、候補列挙順は選択規則へ追随。

## 5. チェックリスト

- [ ] `PairSelector.OrderCandidates` に実効入力レート昇順を第 2 キーとして追加し、XML コメントを更新する
- [ ] `DisposalSelector` の順位にも同じキーを適用し、XML コメントを更新する
- [ ] SEL-26 以降・DSP-26 以降を追加し、`dotnet build`・`dotnet test` 全緑を確認する
- [ ] `tools/validate_master.py` 通過を確認する
- [ ] 手動確認 MN-145 以降を実施する
