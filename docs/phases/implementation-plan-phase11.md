# Phase 11 実装詳細計画

**対象フェーズ**: Phase 11（採取上限の計算: 上限・代替レシピ展開・警告・ユーザー上書き）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)（§4.7）、[decision-records.md](../decision-records.md)（仕様決定 AC・AD・AE）、[implementation-plan-phase10.md](implementation-plan-phase10.md)

> 本書は Phase 11 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

仕様決定 AC・AD・AE は requirements.md（§4.7）へ先行反映済みである。
本 Phase は、計算入力へのマップ選択と採取レート上書きの追加、および採取上限に従う需要展開の実装を行う。

- `ContextFilter.MapId`（計算ごとに 1 マップ選択、null は未選択）と `GatherRateOverride[]` の追加（Domain）
- `ProductionCalculator` の採取素材終端処理を採取優先へ変更（従来のレシピ優先からの仕様変更、仕様決定 AD）
- 警告コードの追加（`GatherCapExceeded`、マップが使えない場合の警告、暫定解釈の入力エラー警告）
- `CalculationService` の引数への採取上書き追加、`CalculationInputBuilder` への採取レート入力行パース追加（Application）
- Domain テストへの ID 採番の新規ケース追加（GAT 系）と、`FIX-03`・`CNV-01` の期待値更新
- Application テストへのパースケース追加（GRI 系）と、`ApplicationFixtures` の `IsGatherable` 明示化
- 本書と、[test-specification-phase11.md](test-specification-phase11.md)

### やらないこと

- 公開アプリ・管理ツールの計算 UI へのマップ選択・採取レート入力欄の追加。Phase 12・Phase 13 で扱う。本 Phase では両 UI からの計算呼び出しは上書きなし・マップ未選択のままとする
- `data/master.json` の変更。Phase 10 で追加したサンプルマップをそのまま使う
- `SchemaVersion`・`DataVersion` の変更。計算入力の追加はマスタスキーマを変えない

## 2. 計算入力と警告コード

### 2.1 Domain

```csharp
// CalculationInputs.cs
public sealed record GatherRateOverride(string ItemId, double RatePerMinute);   // 個/分（仕様決定 AE）

public sealed record ContextFilter
{
    public IReadOnlyCollection<string> ActiveGameEventIds { get; init; } = [];
    /// <summary>選択マップ。null は未選択（全採取素材を上限なし）。</summary>
    public string? MapId { get; init; }
}
```

`ProductionCalculator.Calculate` の引数末尾に `IReadOnlyList<GatherRateOverride> gatherOverrides` を追加する。

### 2.2 有効採取レートの解決（仕様決定 AC・AD・AE）

採取素材 `i` の有効採取レート（個/分、∞ は上限なし）は次の順で決める。
解決は計算開始時に一度だけ行い、全採取素材の上限表を Session の状態として保持する。

1. `ContextFilter.MapId` が null（未選択）なら全採取素材 ∞。上書きは有効なマップ選択に対してのみ適用されるため、未選択時の上書きは無視する（AE）
2. `MapId` がマスタに存在しない、または所属イベントが非有効なら、全採取素材 0（採取不可）＋警告。上書きは適用しない（AD、§4 暫定解釈）
3. マップが有効なら `GatherRateOverride` → マップの採取レート行（`IsUnlimited` なら ∞、それ以外は `RatePerMinute`）→ 行なしは 0

### 2.3 需要展開の変更（採取優先、仕様決定 AD）

`Expand` で採取素材（`IsGatherable=true`、かつイベント上有効）の残差需要を次の順で処理する。

1. 残差のうち `min(残差, 有効レート − 採取済み)` を `Raw`（採取）へ計上する
2. 採取しきれない残差は通常どおりペア選択し、当該アイテムを産出するレシピで展開する
3. 展開できるレシピがなければ残差を `Unmet` とし、警告 `GatherCapExceeded` を出す

- 採取は終端処理のため、スタック積み（循環検出）より先に行い、超過分のみが既存の循環検出・ペア競合・引き戻しの経路へ進む
- `Raw` の引き戻し（`TrimTerminal`）は従来どおり残差へ揃える。レシピの `Retract` は `Raw` を他経路供給として差し引くため、副産物が後から供給された場合はレシピ稼働から先に縮小し、採取優先が保たれる
- 採取上限は需要の発生源を問わず適用する。環境消費・固定消費による追加需要も同じ `Demand` → `Expand` の経路を通るため、追加の分岐は不要（AD）

### 2.4 警告コードの追加

| コード | 発生条件 | メッセージ（日本語） |
|---|---|---|
| `GatherCapExceeded` | 採取上限を超える需要があり代替レシピもない（仕様決定 AD） | アイテム {id} の需要が採取上限を超え、代替レシピもないため未充足 |
| `GatherMapUnavailable` | 選択マップの所属イベントが非有効（AD、X と同型） | 選択したマップはイベントが有効でないため採取不可 |
| `InvalidGatherMap` | `MapId` が存在しないマップを指す（暫定解釈） | 選択したマップが存在しないため採取不可 |
| `InvalidGatherRateOverride` | 上書きが存在しない・採取素材でないアイテムを指す、または値が負・非有限（暫定解釈） | 上書きを無視する旨 |

## 3. Application

- `CalculationService.Calculate` の引数末尾に `IReadOnlyList<GatherRateOverride> gatherOverrides` を追加し、`ProductionCalculator` へそのまま渡す
- `CalculationInputBuilder` に `GatherRateInput(string ItemId, string ItemName, string? RateText)` と `TryParseGatherRates` を追加する。空欄行はマップ既定値扱いで無視する。非数値・負・非有限はエラー（`{ItemName} の利用可能レートは 0 以上の数値で入力してください。`）。0 以上ならマップ値を超える入力も受理する（AE）。行が指すアイテムが存在しないか採取素材でない場合はエラーとする
- 呼び出し側の追従: `Home.razor`・`PreviewPage.razor` は `[]` を渡す（UI 自体は Phase 12・13）。テストの呼び出し側も同様に追従する

## 4. 暫定解釈

- **不明な `MapId`**: 仕様は「非有効イベント所属のマップが選択状態で残った場合」の扱いのみ定める。`MapId` がマスタに存在しない場合も同じ事態（候補外の選択が残った）と解釈し、全採取素材を採取不可（上限 0）＋警告 `InvalidGatherMap` とする。上書きは適用しない（有効なマップ選択でないため。AE）
- **上書きの検証**: `GatherRateOverride` が存在しない・`IsGatherable=false` のアイテムを指す、または `RatePerMinute` が負・非有限のときは、その上書きを無視して警告 `InvalidGatherRateOverride` を出す。`EnvironmentCountOverride` の不適格扱い（警告して既定へ戻る）と同型とする
- **上書きとマップ行なしアイテム**: 上書きは有効レートの置き換えなので、マップに行のない採取素材（既定 0）にも適用できる（AE「指定は有効レートの置き換え」）
- **上書きの重複**: 同一アイテムへの上書きが複数ある場合は先頭を採用する（`EnvironmentCountOverride` の `FirstOrDefault` と同型）
- **採取優先と副産物**: 副産物や他経路の生産で需要が賄われた分は残差から先に差し引き、残差だけを上限まで採取する。採取上限は「採取量の上限」であり、供給総量の上限ではない
- **イベント不可アイテムの優先**: 採取素材でも所属イベントが非有効なら採取不可・未充足（仕様決定 X）の既存処理を維持し、マップ判定より先に評価する

## 5. 既存ケースの期待値変更

採取優先への変更により、レシピを持つ採取素材の扱いが変わる。

- `FIX-03`（F-11 変形・i-fuel は採取素材かつ r-fuel で生産可）: マップ未選択では全量採取となり、`r-fuel` は稼働しない
- `CNV-01`（F-13・i-gasp は採取素材かつ r-gasp で生産可）: 同様に全量採取、`r-gasp` は稼働しない
- `ApplicationFixtures.Item` は `IsGatherable` を TransportKind から推定していたが、採取優先では生産対象アイテムまで採取扱いになるため、明示引数へ変更し原材料のみ `true` とする

## 6. 作業順序

1. 本書と [test-specification-phase11.md](test-specification-phase11.md) を作成する。
2. §2 の Domain（入力・警告コード・採取上限ロジック）を実装する。
3. §3 の Application（サービス引数・入力パース・呼び出し側追従）を実装する。
4. テスト仕様書の各ケースを実装し、§5 の期待値変更を反映する。
5. `dotnet test` 全緑と `tools/validate_master.py` の通過を確認する。
6. `implementation-plan.md` の Phase 11 を `[x]` へ更新して PR を作成する。

## 7. 受け入れ条件

- `dotnet test` が全緑。`~/.venvs/validate/bin/python tools/validate_master.py` がスキーマ適合を報告する
- 採取上限・代替展開・警告が仕様どおりに動く（GAT 系ケース）
- マップ未選択・上書きなしの既存利用では、採取素材を産出するレシピがあった計算のみ挙動が変わり（採取優先）、それ以外は変わらない
