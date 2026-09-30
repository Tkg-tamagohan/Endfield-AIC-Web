# Phase 12 テスト仕様書

**対象**: Phase 12 成果物（公開アプリ UI: マップ選択＋採取レート入力）
**前提ドキュメント**: [requirements.md](../requirements.md)（§4.7）、[decision-records.md](../decision-records.md)（仕様決定 AC、AD、AE）、[implementation-plan-phase12.md](implementation-plan-phase12.md)

> 本書は Phase 12 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> 従来のテスト項目は Phase 11 以前の仕様書を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。`tests/EndfieldAicWeb.Application.Tests` に配置する |
| 実行コマンド | `dotnet test`、および `~/.venvs/validate/bin/python tools/validate_master.py` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | App を `dotnet run` で起動し、ブラウザプレビューで §4 の項目を確認する（ui-mock-first ルールに従い、ユーザー確認を先に取る） |

## 2. フィクスチャ

### A-06: マップ候補

`ApplicationFixtures` に追加する。
A-01 のアイテム（`i-ore`、`i-gas` は採取素材、`i-part` は非採取）とレシピに、次のマップとイベントを加える。

| 要素 | 内容 |
|---|---|
| イベント | `ev-on`、`ev-off` |
| マップ（マスタ順） | `m-cap`（常設: `i-ore` 上限 60、`i-gas` 無限）、`m-on`（`ev-on` 所属: `i-ore` 上限 120）、`m-off`（`ev-off` 所属: `i-ore` 上限 999）、`m-empty`（常設: 行なし） |

## 3. テスト項目一覧（Application 層）

### MPS: マップ候補と既定上限

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| MPS-01 | 候補は常設と有効イベントのマップ | A-06、有効イベント `ev-on` | `m-cap`、`m-on`、`m-empty` の順。`m-off` は含まない |
| MPS-02 | 有効イベントなしの候補 | A-06、有効イベントなし | `m-cap`、`m-empty` の順 |
| MPS-03 | 使えるマップの判定 | A-06、有効イベント `ev-on` | `m-cap`、`m-on` は true。`m-off` と存在しない `m-ghost` は false |
| MPS-04 | マップ未選択の既定上限 | A-06、MapId=null、`i-ore` | null（上限なし） |
| MPS-05 | 上限行、無限行、行なしの既定上限 | A-06、MapId=`m-cap` | `i-ore` は 60、`i-gas` は null。MapId=`m-empty` の `i-ore` は 0 |
| MPS-06 | 使えないマップの既定上限 | A-06、MapId=`m-off`（`ev-off` 非有効）、および MapId=`m-ghost` | どちらも `i-ore` は 0 |
| MPS-07 | 採取レート入力行の対象 | A-06、目標 `i-part` 60/分、MapId=`m-cap` の計算結果 | `ItemRequirements` の順で `i-ore`、`i-gas` を返し、`i-part` を含まない |
| MPS-08 | 既定上限と計算の一致 | A-06、MapId=`m-cap`、目標 `i-part` 60/分（`i-ore` 需要 120） | `i-ore` の採取量が `DefaultGatherCap` の 60 に等しい |

## 4. 手動確認項目（ブラウザプレビュー）

実装のブラウザプレビューをユーザーに操作してもらい、次を確認する。
ユーザーのフィードバックを反映してから §3 のテスト作成に進む。
データは実装計画 §5 のプレビュー用マスタを使う。

| ID | 確認内容 |
|---|---|
| MN-11 | 採取マップの選択肢に「未選択（採取無制限）」とサンプル採取地が出る。`map-event` は `ev-first` を有効にしたときだけ候補に出る |
| MN-12 | 汎用部品を計算し、マップ未選択では採取素材がすべて採取で賄われ、採取節が出ない |
| MN-13 | サンプル採取地を選ぶと即時再計算され、活性ガス（行なし、上限 0）が未充足と `GatherCapExceeded` の警告になる。採取節に原鉱石、固形燃料、活性ガスの行が出て、既定上限（240、無制限、0）が読める |
| MN-14 | 原鉱石の利用可能レートを既定より小さく入力して確定すると、超えた分が `recipe-ore` で生産され、原鉱石の行に「上限到達」が付く。空欄に戻すと既定値へ戻る |
| MN-15 | 利用可能レートに非数値や負の値を入れるとエラーが出て、計算結果は前回のまま残る |
| MN-16 | 目標数量の変更やペア選択の変更で再計算しても、採取レートの入力値が保持される |
| MN-17 | `map-event` を選んだあと `ev-first` のチェックを外すと、選択が「（イベント無効）」表記で残り、`GatherMapUnavailable` の警告が出て採取節が消える |
| MN-18 | 供給内訳のチップが「採取 X」と表示される。毎秒表示に切り替えても採取節は個/分のまま |
| MN-19 | スマートフォン幅で、マップ選択と採取節の入力が潰れない |
