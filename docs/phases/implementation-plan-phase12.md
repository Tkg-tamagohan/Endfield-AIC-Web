# Phase 12 実装詳細計画

**対象フェーズ**: Phase 12（公開アプリ UI: マップ選択＋採取レート入力）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)（§4.7）、[decision-records.md](../decision-records.md)（仕様決定 AC、AD、AE）、[implementation-plan-phase11.md](implementation-plan-phase11.md)
**関連ドキュメント**: [test-specification-phase12.md](test-specification-phase12.md)（本 Phase のテスト仕様）

> 本書は Phase 12 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

Phase 11 で計算側（`ContextFilter.MapId`、`GatherRateOverride`、`CalculationInputBuilder.TryParseGatherRates`）は実装済みである。
本 Phase は、公開アプリの計算 UI からそれらを指定できるようにする。

- 入力パネルへのマップ選択の追加（未選択は採取無制限）
- 結果パネルへの採取素材ごとの利用可能レート入力行の追加（散布機台数入力と同型、仕様決定 AE）
- 供給内訳の表示名の整理（「採取素材」から「採取」へ）と、採取上限への到達の可視化
- マップ候補と既定上限を導く Application 層の補助（Phase 13 の管理ツールでも使う）
- 本書と [test-specification-phase12.md](test-specification-phase12.md)

### やらないこと

- 管理ツールの計算プレビューへの追従（Phase 13）。`PreviewPage.razor` の供給内訳の表示名も Phase 13 で揃える
- `data/master.json` の変更。同梱のサンプルマップ（`map-sample`）をそのまま使う
- Domain の計算と警告メッセージの変更。警告文中のアイテム・マップが Id 表記のままである点は既存の他警告と同じ扱いとする

## 2. Application 層の補助

`src/EndfieldAicWeb.Application/MapSelection.cs` に静的クラス `MapSelection` を追加する。
候補の絞り込みと既定上限の導出を UI から切り離し、App と管理ツールで同じ規則を使うためである。

```csharp
public static class MapSelection
{
    /// 選択候補。常設（GameEventId=null）と、有効イベント所属のマップ。マスタの並び順を保つ。
    public static IReadOnlyList<GameMap> ListCandidates(MasterDataSnapshot snapshot, IReadOnlyCollection<string> activeGameEventIds);

    /// マップが計算で使えるか。存在しない Id、または所属イベントが非有効なら false。
    public static bool IsAvailable(MasterDataSnapshot snapshot, string mapId, IReadOnlyCollection<string> activeGameEventIds);

    /// 上書きなしの既定上限（個/分）。null は上限なし。
    /// MapId=null は null。マップが使えない場合は 0。行なしは 0、無限行は null、上限行は RatePerMinute。
    public static double? DefaultGatherCap(MasterDataSnapshot snapshot, ContextFilter context, string itemId);

    /// 採取レート入力行を出す採取素材。計画の ItemRequirements の順で、IsGatherable=true のものだけを返す。
    public static IReadOnlyList<string> GatherableItemIds(ProductionPlan plan, MasterDataSnapshot snapshot);
}
```

`DefaultGatherCap` は Domain の `ProductionCalculator.ResolveGatherCaps` から上書きを除いた規則と一致させる（仕様決定 AC、AD）。

## 3. 画面の変更（`App/Pages/Home.razor`）

### 3.1 マップ選択（入力パネル）

「有効イベント」の下に見出し「採取マップ」とネイティブ `<select>` を置く。

| 項目 | 内容 |
|---|---|
| 先頭の選択肢 | 「未選択（採取無制限）」。値は空文字で、`MapId=null` として計算する |
| 候補 | `MapSelection.ListCandidates` の結果をマップ名で並べる |
| 候補外の選択状態 | 選択中のマップが候補から外れた場合（所属イベントのチェックを外した場合）も選択は保持し、「〈マップ名〉（イベント無効）」の選択肢として残す。計算は Domain の規則どおり全採取素材を採取不可とし、警告 `GatherMapUnavailable` が出る（仕様決定 AD） |
| 変更時 | イベントのチェック変更と同じく即時再計算する |
| ヒント | 「マップを選ぶと採取素材の採取量に上限がかかり、超えた分はレシピで生産します。」 |

イベント切替時の扱い（候補の絞り込みと、候補外になった選択の保持）は Phase 13 の管理ツールでも同じにする。

### 3.2 採取レート入力（結果パネル）

「素材」の直後に見出し「採取」の節を置く。
節は、マップを選択していて、かつ `MapSelection.IsAvailable` が true のときだけ表示する。
マップ未選択では上書きが適用されず（仕様決定 AE）、使えないマップでは上書きが無効になる（仕様決定 AD）ためである。

- 行は `MapSelection.GatherableItemIds` の採取素材ごとに 1 行とする
- 行の内容：アイコン、アイテム名、利用可能レートの入力欄（`input tiny`、`@onchange`）、「個/分」、既定上限の表示「（マップ既定 240 個/分）」または「（マップ既定 無制限）」、現在の採取量「採取 60 個/分」
- 入力欄のプレースホルダは既定上限の数値、無限なら「無制限」とする
- 採取量が有効上限（入力値、空欄なら既定上限）に達している行には、タグ「上限到達」を付ける（上限の可視化）
- ヒント：「空欄はマップの既定値を使います。マップ値を超える値も指定できます。変更すると再計算します。」
- 入力値は再計算をまたいで保持する（散布機台数と同じく、前回の行から Id で引き継ぐ）
- 入力エラーは節の先頭に表示し、再計算を行わない（`TryParseGatherRates` のエラーメッセージを使う）
- マップを使えない状態では入力行を計算へ渡さない（上書きは Domain でも無効になるが、隠れた行の入力エラーで計算が止まらないようにする）

### 3.3 供給内訳の表示名

`SupplyText` の `SupplyKind.Gathered` の表記を「採取素材 X」から「採取 X」へ変える。

## 4. 暫定解釈

- **レートの表示単位**：採取節の入力と採取量は常に個/分で表示し、表示単位の切替（毎分、毎秒、期間）の対象外とする。散布機台数の環境行と同じ扱いで、入力値の単位（個/分、仕様決定 AF）と揃えるためである
- **マップ変更時の入力値**：マップを変えても採取レートの入力値は保持する。仕様決定 AE の「再計算後も入力を保持」をマップ変更にも適用した解釈であり、プレビュー確認で妥当性を確かめる
- **節の表示条件**：採取節は有効なマップ選択時だけ表示する。上書きが計算に効かない状態で入力欄を出すと、入力が反映されない理由が利用者に伝わらないためである
- **候補外の選択の保持**：仕様決定 AD は「選択状態が残った場合」を想定しているため、UI はイベントのチェックを外しても選択を自動解除しない。選択肢の表記で候補外であることを示す

## 5. プレビュー確認用のデータ

同梱のサンプルデータには、イベント所属のマップと、レシピで生産できる採取素材がない。
プレビューでは、稼働中の開発サーバーの `wwwroot/data/master.json` だけを差し替え、次を追加したマスタで確認する（リポジトリの `data/` はコミットしない）。

- マップ `map-event`（`ev-first` 所属、原鉱石 無限）
- レシピ `recipe-ore`（固形燃料 1 → 原鉱石 1、加工機 4 秒）。原鉱石が上限を超えたときのレシピ展開を見るため

## 6. 作業順序

1. 本書と [test-specification-phase12.md](test-specification-phase12.md) を作成する。
2. §2 の `MapSelection` を実装する。
3. §3 の画面変更を実装し、`dotnet build` を通す。
4. App をローカル起動し、§5 のデータでプレビューを用意して主要フローを自分で確認する。
5. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する（テスト作成はこの後）。
6. テスト仕様書の §3 の項目どおりに Application 層テストを作成し、`dotnet test` を全緑にする。
7. `implementation-plan.md` の Phase 12 を `[x]` へ更新して PR を作成する。

## 7. 受け入れ条件

- `dotnet test` が全緑。`~/.venvs/validate/bin/python tools/validate_master.py` がスキーマ適合を報告する
- プレビューでのユーザー確認を経て、マップ選択と採取レート指定を含む主要フローが動作する
