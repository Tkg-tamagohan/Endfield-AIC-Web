# Phase 13 実装詳細計画

**対象フェーズ**: Phase 13（Admin 計算プレビューの公開版追従）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AD、AE、AG、I、M、O、U）、[implementation-plan-phase12.md](implementation-plan-phase12.md)
**関連ドキュメント**: [test-specification-phase13.md](test-specification-phase13.md)（本 Phase のテスト仕様）

> 本書は Phase 13 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

管理ツールの計算プレビュー（`src/EndfieldAicWeb.Admin/Pages/PreviewPage.razor`）を、公開版（`src/EndfieldAicWeb.App/Pages/Home.razor`）と同等の機能へ揃える（仕様決定 AG）。
移植は公開版からのコピー追従で行い、共有ロジックは既に Application 層にあるものをそのまま使う。

- ペア代替選択ドロップダウン（素材行、仕様決定 U）
- 散布機台数入力（環境節、仕様決定 I）
- 単位切替（毎分/毎秒/期間）と期間入力（仕様決定 M）
- 採取マップ選択（入力パネル、仕様決定 AD）
- 採取素材ごとの利用可能レート入力（結果パネル、仕様決定 AE）
- 供給内訳の表示名の追従（「採取素材 X」→「採取 X」、Phase 12 §3.3 の未追従分）
- Admin の `wwwroot/css/app.css` への不足スタイル追加（`.env-line`、`.input.pair`、`.period-inputs`）

### やらないこと

- razor ページの共有化（RCL 化）。仕様決定 AG で見送り済みであり、[remaining-issues.md](../remaining-issues.md) に残課題として記録済み
- アイテム選択を `RefSelect` から公開版の検索コンボへ変えること。Admin 側の既存 UI を維持する
- Application・Domain 層の変更。必要なロジック（`MapSelection`、`CalculationInputBuilder`、`PairOption`、`PlanViewDefaults`、`AmountConverter`/`PeriodAmount`）は Phase 11・12 で実装済み
- `data/master.json` の変更

## 2. Admin 側で維持する差異

公開版とのコピー追従の対象外とする、Admin 固有の構成。

| 項目 | Admin 側の扱い |
|---|---|
| データソース | `AdminDocumentService`（編集中の文書）。`Store.Snapshot` を使い、計算ごとに `EnsureSnapshot()` で検証→スナップショット再構築する。公開版の `MasterDataService`/`IconCatalog` は使わない |
| アイテム選択 | `RefSelect`（ネイティブ select）。公開版のフィルタコンボにはしない |
| 計算呼び出し | `Calculator.Calculate` を try/catch で囲み、失敗は `_inputError` に表示する（編集中データで例外になりうるため。既存のまま） |
| レシピアイコン | `Store.EffectiveIconKey(recipe)` によるフォールバック（既存のまま） |
| 文面 | 入力パネルの「編集中データで計算します。」ヒントと、フッターの「データ版 … ・ SourceLabel」を維持する |

## 3. 画面の変更（`Admin/Pages/PreviewPage.razor`）

公開版 `Home.razor` から次のブロックを移植する。

### 3.1 入力パネル

- 「有効イベント」ブロックの下に、見出し「採取マップ」とネイティブ `<select>` を置く。候補は `MapSelection.ListCandidates(_snapshot, ActiveGameEventIds)`、先頭は「未選択（採取無制限）」、候補外になった選択は「〈マップ名〉（イベント無効）」で保持する（公開版 §3.1 と同じ）
- イベント・マップの切替は即時再計算する（既存の `OnEventToggled` と同型の `OnMapChanged`）

### 3.2 ツールバー

- 調整済/未調整の segmented の右に、単位切替の segmented（毎分/毎秒/期間）を追加する
- 期間選択時は日・時・分の入力欄（`.period-inputs`）を出し、非数値・負数は「期間は 0 以上の数値で入力してください。」のエラーを表示する

### 3.3 素材節

- 表示量は `Fmt`（`AmountConverter.Convert` 経由）＋`Suffix()` に置き換える
- 供給チップの `SupplyText` は `SupplyKind.Gathered` を「採取 X」とし、各単位の `Suffix()` を付ける
- `_outcome.PairOptionsByItemId` に候補がある素材行にペア代替選択ドロップダウン（`select.input.pair`）を出す。選択は `_pairOverrides` に保持し、変更で即時再計算する

### 3.4 採取節

- 素材節の直後に見出し「採取」の節を置く。表示条件は公開版と同じ（マップ選択中かつ `MapSelection.IsAvailable` が true）
- 行は `MapSelection.GatherableItemIds` の採取素材ごとに、アイコン・名称・レート入力（`input tiny`、`@onchange`）・「個/分」・マップ既定ラベル・現在の採取量・「上限到達」タグを出す
- 入力値は `_gatherRateTexts` で再計算をまたいで保持し、計算不能なキーは公開版と同じ規則で除去する
- 採取節の数値は常に個/分とし、単位切替の対象外とする（Phase 12 §4 の暫定解釈を Admin にも適用）

### 3.5 環境節

- 環境行を散布機台数の入力行に変える（`input tiny`＋「台」＋「（自動 N 台まで）」、仕様決定 I）。自動値は `PlanViewDefaults.DispenserLimit`
- 消費量表示に `PeriodConsumption`（期間換算）を追従させる
- 入力エラーは `_envError` として節の先頭に出し、再計算を行わない

### 3.6 `Recalculate` の組み立て

- `TryParseTargets` に加え、`TryParseEnvironmentCounts`・`TryParseGatherRates` で入力を検証し、失敗時は対応する `_envError`/`_gatherError` に出して計算しない
- `ContextFilter.MapId` に `_mapId` を入れ、`Calculator.Calculate` へ `_pairOverrides`・`environmentOverrides`・`gatherOverrides` を渡す
- 成功後に `_envInputs`・`_gatherInputs` を `_outcome` から再構築する（公開版 §3.2・本書 §3.5 と同じ手順）
- `EnsureSnapshot()` で検証・スナップショット再構築する既存の先頭処理と、`Calculate` の try/catch は維持する

### 3.7 スタイル

`src/EndfieldAicWeb.Admin/wwwroot/css/app.css` に公開版から次をコピーする。

- `.env-line`（環境・採取の入力行の折り返しレイアウト）
- `.input.pair`（素材行のドロップダウン）
- `.period-inputs` と `.period-inputs .input`（期間入力の 3 欄）

## 4. 暫定解釈

- **採取節の表示単位**: 常に個/分とし、単位切替の対象外とする。Phase 12 §4 の暫定解釈を Admin 側にもそのまま適用する（入力単位の仕様決定 AF と揃えるため）
- **マップ変更時の採取レート入力**: 保持する。Phase 12 §4 と同じ解釈
- **環境行の「（自動 N 台まで）」**: 公開版は「自動 @EnvAutoCount 台まで」の表記。同じ表記を使う
- **期間換算の対象**: 素材・チップ・未充足・余剰は `Suffix()` に追従する。環境行の消費量は `個/分` 表示のまま `PeriodConsumption` で期間換算を併記する（公開版と同じ）
- **アイテム選択 UI**: Admin の `RefSelect`（全件ドロップダウン）のままとする。公開版のコンボ移植は仕様決定 AG の「同等の機能」の範囲外と判断する（アイテム選択自体は既に可能）

## 5. プレビュー確認用のデータ

Admin は「同梱マスタを読み込む」で `wwwroot/data/master.json` を読み込める。
Phase 12 §5 と同じく、採取節の全要素を見るには稼働中の dev server の `src/EndfieldAicWeb.Admin/wwwroot/data/master.json` だけを差し替え、次を追加したマスタで確認する（リポジトリの `data/` はコミットしない）。

- マップ `map-event`（`ev-first` 所属、原鉱石 無限）
- レシピ `recipe-ore`（固形燃料 1 → 原鉱石 1、加工機 4 秒）

## 6. 作業順序

1. 本書と [test-specification-phase13.md](test-specification-phase13.md) を作成する。
2. §3.7 のスタイルを Admin の `app.css` に追加する。
3. §3 の画面変更を実装し、`dotnet build` を通す。
4. Admin をローカル起動し、§5 のデータでプレビューを用意して主要フローを自分で確認する。
5. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する（テスト作成はこの後）。
6. `dotnet test`・`tools/validate_master.py` が全緑であることを確認し、`implementation-plan.md` の Phase 13 を `[x]` へ更新して PR を作成する。

## 7. 受け入れ条件

- `dotnet test` が全緑。`~/.venvs/validate/bin/python tools/validate_master.py` がスキーマ適合を報告する
- プレビューでのユーザー確認を経て、管理ツールの計算プレビューで公開版と同等の機能（ペア代替選択・散布機台数入力・単位切替と期間入力・マップ選択・採取レート入力）が動作する
