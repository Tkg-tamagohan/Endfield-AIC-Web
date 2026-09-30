# Phase 4 実装詳細計画

**対象フェーズ**: Phase 4（計算アプリ UI: 公開 Blazor WASM）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)
**関連ドキュメント**: [test-specification-phase4.md](test-specification-phase4.md)（本 Phase のテスト仕様）

> 本書は Phase 4 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### 作るもの

- `EndfieldAicWeb.App` の計算 UI（Blazor WASM 単一ページ `/`）
  - 入力部：生産リスト（アイテム＋個/分の行の追加・削除）、有効イベントの切替、環境ごとの散布機台数入力
  - 結果部：素材（供給内訳つき・ペア代替選択つき）・設備台数・環境・消費電力合計・余剰・警告の表示、および「未調整／調整済」切替と表示単位（毎分・毎秒・期間）の切替
- `EndfieldAicWeb.Application` のユースケース群（計算実行の入口・表示用データの導出・期間換算）
- `EndfieldAicWeb.Domain` の小変更
  - `ProductionPlan` に確定ペアの出力（`PairSelections`）を追加し、UI が現在選択中のペア行を特定できるようにする
  - `PairSelector` に代替候補の列挙 API（`ListCandidates`）を公開し、ペア行単位の選択肢を提供する
- `tests/EndfieldAicWeb.Application.Tests`（xunit、ソリューションへ追加）

### 作らないもの

- 管理ツール UI（Phase 6）
- Cloudflare Pages へのデプロイと `_redirects`・自動デプロイ構成（Phase 5）
- アイコン実画像の表示と取り込みパイプライン（Phase 7）。本 Phase では `IconKey` ツールチップつきのプレースホルダ表示までとする
- 実データ投入（Phase 7）。`data/master.json` の開発用サンプルをそのまま使う
- E2E 自動化・bUnit。ブラウザ動作は要件 §9 の方針どおり手動確認とし、ロジックは Application 層の単体テストで担保する

## 2. レイヤー構成と参照

- `App → Application`、`App → Infrastructure`（`MasterJsonLoader` を利用する合成根の参照）、`Application → Domain`、`Infrastructure → Domain`。
- `data/master.json` は App の csproj で `wwwroot/data/` へコピーし、開発サーバー・発行成果物の双方へ同梱する（Phase 5 の配信条件の前倒し分）。アイコン実画像と `data/icons/` の同梱は Phase 7 で扱う。
- マスタ読み込みは App 側サービス（`Services/MasterDataService`）が担い、HTTP 取得 → `MasterJsonLoader.Load` → `MasterDataSnapshot` 生成の経路を持つ。読み込み失敗時はエラー一覧を表示して計算 UI を出さない。

## 3. 画面構成（単一ページ）

単一ページ `/` に入力部と結果部を縦に並べる。旧 WPF 版の「入力タブ／結果タブ」構成は、モバイルで往復しやすい縦積みへ変更する（仕様決定 K のスマートフォン対応のため）。

### 入力部

| 領域 | 内容 |
|---|---|
| 生産リスト | 行 = アイテム選択＋個/分入力＋削除ボタン。「行を追加」で行を増やす。初期 1 行。アイテム選択は約 200 件を想定した検索式コンボボックス（名前・Id の部分一致、`TransportKind.None` の仮想アイテムは候補外）とする |
| 有効イベント | マスタの GameEvent を自動有効化＋手動選択で扱う。開催期間±1 日以内のイベント（と常設）は既定 ON で上段にチェックボックスとして列挙。期間外（終了・未開催）は「期間外のイベントを選択」の折りたたみ内に置く。名称と期間（ActiveFrom/ActiveTo がある場合）を併記。チェック変更でも即時再計算する |
| 計算ボタン | 入力検証（アイテム選択・正の有限レート・行 0 件）に通ったとき実行。不正行はエラーメッセージ表示 |

### 結果部

計算済みのときのみ表示。上部に 2 つの切替を置く。

| 切替 | 内容 |
|---|---|
| 未調整／調整済 | 仕様決定 O。既定は「調整済」（`FlowAdjustments` が 1 件以上あるとき。0 件なら両状態が一致するため既定でも差は出ない） |
| 表示単位 | 毎分（既定）・毎秒・期間。期間選択時のみ日・時・分の入力欄を出す（仕様決定 M） |

| セクション | 内容 |
|---|---|
| 素材 | アイテム名・要求量・供給内訳（レシピ／副産物／採取素材＋流量）・未充足量（赤字）。レシピ生産されるアイテムにはペア選択ドロップダウンを置く（§4） |
| 設備 | 設備名・実数台数 → 切上げ台数。調整済モードでは配下に推奨流量制限の行（「〈レシピ名〉の〈アイテム名〉を X 個/s に制限」、旧版踏襲）を並べる |
| 環境 | 環境名・散布機設備名・確定台数・消費アイテムと流量（個/分）。期間モードでは個数換算も併記。行内に散布機台数の変更入力を置き、自動計算値（その環境を要する稼働ペア数）を上限に 0〜上限で下げられる（仕様決定 I）。空欄は自動。変更で即時再計算する |
| 電力 | 消費電力合計（期間換算しない、仕様決定 M） |
| 余剰 | アイテム名・余剰量。未調整モードでは切上げ最大稼働による過剰を含む（§5） |
| 警告 | `CalculationWarning` のメッセージ一覧 |

非公式ファンツールの明記はヘッダー直下とフッターに置く（要件 §3・§8）。

## 4. ペア代替選択（仕様決定 U）

- 候補は「そのアイテムを出力し、コンテキスト上適格なレシピ」の適格ペア行をすべて平坦化し、レシピ名・設備名・サイクル秒・環境の有無・固定消費を併記した行単位で提示する。表記例：`汎用部品 / 加工機 3秒（ガス環境・固定消費 固形燃料 30/分）`。
- 選択肢の先頭に「既定」を置き、選ぶと上書きを解除して計算側の既定選択へ戻る。
- 現在の選択は `ProductionPlan.PairSelections`（計算が確定したペア）で表示する。ペア上書きは `PairOverride` の一意キー（RecipeId・FacilityId・CycleTime・EnvironmentId・FixedConsumption、決定記録 P）を保持し、選択変更で即時再計算する。
- 代替候補は Domain の `PairSelector.ListCandidates` が返す。選択規則（VersionAdded 順・CycleTime 最小・同率規則）を UI 側で再実装しないため、順序と既定フラグは Domain 側で付ける。

## 5. 「未調整」の表示導出

「未調整」は切上げ台数の最大稼働を表す表示状態であり、同じ `ProductionPlan` から表示層が導出する（計算結果の共用、implementation-plan §3-10）。導出は Application 層の `ResultViewBuilder`（仮称）に集約し、単体テストの対象とする。

- 設備 `F` の最大稼働倍率 `s(F)` を `(CeilCount(F) − 散布機台数(F)) / レシピ実数台数(F)` とする。`レシピ実数台数(F)` は `F` 上の全ランの `CyclesPerMinute × CycleTime / 60` の合計、`散布機台数(F)` は `EnvironmentRequirements` を `ProviderFacilityId` で集計した台数。`レシピ実数台数(F) = 0` の設備（純粋な散布機）は倍率対象外。
- ラン `r`（設備 `F` 上）の未調整サイクル数は `CyclesPerMinute(r) × s(F)`。散布機の消費・台数・電力は切上げ台数の指定値から変わらないため、倍率はレシピ稼働側だけに掛ける。
- 素材の供給内訳（レシピ・副産物）は属するランの倍率でスケールし、採取素材は変えない。
- 余剰は再計算する：アイテムごとに「スケール済み生産量 − 需要」。イベント不可アイテムはスケール済み生産量の全量を余剰とする（Aggregate の規則と同型）。
- 「調整済」ではスケールを掛けず、計算結果どおりの供給内訳・余剰と `FlowAdjustments` の推奨制限を表示する。
- 注意：設備を複数ランで共有する場合の最大稼働配分は仕様に明示がなく、ここでは各ランを同一倍率でスケールする解釈を採る（推測）。ユーザーのプレビュー確認で裏付けを取る。

## 6. 期間換算（仕様決定 M）

- 表示単位は毎分・毎秒・期間の 3 状態。期間は日・時・分の 3 入力で `合計分数` を組み立てる。
- 換算対象は素材の要求量・供給内訳・未充足・余剰と環境消費（個/分値をそのまま換算する）。消費電力合計と推奨流量制限（個/s の設定値）は換算しない。
- 単位は表示層の責務とし、Domain・Application の値は `個/分` 基準で保持する。

## 7. スタイル

- 旧版踏襲のダーク基調（背景 `#0E1116`、強調 `#F2A33C`、警告 `#E5484D`、補助 `#9AA0A8`）。Bootstrap はレイアウト補助として残し、アプリ固有の色・部品は `app.css` で定義する。
- レスポンシブ：入力部と結果部は幅に応じて 1 列へ落とす。ドロップダウン・入力はモバイル幅でも潰れない最小幅を確保する。
- UI 文字列は日本語のみ。アイコンは `IconKey` をツールチップに持つ「?」プレースホルダ（旧版と同形状）。

## 8. Application 層の構成

| 型 | 責務 |
|---|---|
| `MasterSnapshotFactory` | `MasterDocument` → `MasterDataSnapshot` |
| `PairOption` / `PairOptionKey` | ペア行の選択肢。一意キーの文字列化と `PairOverride` への変換 |
| `CalculationService` | `ProductionCalculator` の実行と、需要アイテムごとの候補列挙（`ListCandidates`）をまとめた結果の返却 |
| `ResultViewBuilder` | `ProductionPlan` ＋スナップショット → 表示用行（素材・設備・環境・余剰・警告）の導出。調整済／未調整の両状態を生成（§5） |
| `PeriodAmount` | 日・時・分 → 合計分数と換算係数 |
| `EventAutoActivation` | 開催期間±1 日以内か常設かでイベントを既定 ON とする判定（仕様決定 T の UI 既定） |
| `ItemSearch` | 生産リストのアイテム検索（名前・Id の部分一致） |

## 9. 作業順序

1. 本書と [test-specification-phase4.md](test-specification-phase4.md) を作成する。
2. Domain：`PairSelections` 出力と `ListCandidates` を追加し、既存テストが全緑のままであることを確認する。
3. Application のユースケース群を実装する。
4. App：マスタ JSON 同梱・読み込み・計算 UI・スタイルを実装する。
5. `dotnet build`・`dotnet test` を通し、App をローカル起動して主要フローを自分で動作確認する。
6. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する（テスト作成はこの後）。
7. [test-specification-phase4.md](test-specification-phase4.md) の項目どおりに Application 層テストを作成し、`dotnet test` 全緑にする。
8. `implementation-plan.md` の Phase 4 チェックリストを更新して PR を作成する。
