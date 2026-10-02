# Phase 15 実装詳細計画

**対象フェーズ**: Phase 15（生産フローグラフの WebGPU 表示）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)
**関連ドキュメント**: [test-specification-phase15.md](test-specification-phase15.md)（本 Phase のテスト仕様）

> 本書は Phase 15 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。
> 作業ブランチはユーザー指定の `dev/webgpu` とする。

## 1. スコープ

### やること

公開アプリの結果パネルに、計算結果の供給関係を表す有向グラフ（生産フローグラフ）を追加する（仕様決定 AI）。

- 採取供給・設備・アイテムをノード、供給・消費関係をエッジとして、採取から目標へ左から右に流れる層状グラフを描く
- エッジ上を流量に比例した速度・密度の粒子が流れる
- 未充足・輸送容量超過のアイテムを赤系で強調し、目標アイテムをアクセント色で強調する
- ノードクリックで対応するリスト行へスクロールして強調する
- WebGPU 非対応環境ではグラフ領域と切替ボタン自体を出さず、従来のリスト表示のみとする（仕様決定 AK）

### やらないこと

- 管理ツールへの展開（別 Phase とする）
- 背景演出、ゲージ・チャート類（フローグラフの基盤への相乗りとして後続で検討）
- 立体の工場プレビュー（仕様決定 K の対象外を維持）
- WebGPU の compute 利用。計算量は CPU で十分であり描画のみが対象
- マスタ構成・計算ロジックの変更

## 2. 現状と構成

現状の結果画面は素材・採取・設備・環境・消費電力・余剰のフラットなリスト群であり、供給経路の形は画面上に現れない。
`ProductionPlan` はすでに DAG 相当の情報（`ItemRequirements[].Supplies`、`RecipeRuns`、`PairSelections`、`EnvironmentRequirements`）を持つため、グラフ表示は既存結果の再表現として実現できる。

Blazor WebAssembly から WebGPU へは直接届かないため、`IJSRuntime` 経由の JS モジュールで描画する。
Admin 側に `wwwroot/js/` の vendored JS の先例があり、同型の運用とする。
外部依存を持たない vanilla JS＋WGSL で実装し、ノードは DOM カード、canvas はエッジと粒子のみを描くハイブリッド構成とする（仕様決定 AJ）。
この分割により WGSL でのテキスト描画やアイコンテクスチャのアトラス化を回避でき、ノード内は既存の `EntityIcon` と同じ `<img>` 表示をそのまま使える。

構成は次のとおり。

```
Home.razor ── FlowGraphModelBuilder（Application・純粋関数）
                │  FlowGraphModel（ノード・エッジ・層割り）
                ▼
        Components/FlowGraph.razor ── canvas + DOM ノード
                │  IJSRuntime（動的 import）
                ▼
        wwwroot/js/flow-graph.js ── WebGPU 描画・パン/ズーム・DOM 配置
```

## 3. グラフモデル（Application 層）

`FlowGraphModelBuilder.Build(plan, snapshot, targets, unadjusted)` が `FlowGraphModel` を返す純粋関数とし、xUnit でカバーする。

ノードは 3 種類とし、ノード Id は `item:<ItemId>`・`fac:<FacilityId>`・`gather` の形式で一意にする。

- **アイテムノード**：`ItemRequirements` の全アイテムと、余剰のみに登場するアイテム。フラグは目標（`targets` 照合）・未充足量・余剰量・輸送容量超過
- **設備ノード**：`RecipeRuns` と `EnvironmentRequirements` の供給設備を FacilityId で集約。散布機台数の合計を注記として持つ
- **採取ノード**：採取供給がある計画にだけ現れる共通の供給源（rank 0）

エッジは始点と終点のノード対で一意に集約し（同一対は流量を合算）、次の種別を持つ。
いずれも `RatePerMinute` を流量として持つ。

- **RecipeInput**：レシピ入力（アイテム→設備）。`CyclesPerMinute × 入力数量`
- **RecipeOutput**：レシピ出力（設備→アイテム）。`CyclesPerMinute × 出力数量`。`SortOrder > 0` の出力は副産物フラグを立てる
- **FixedConsumption**：ペアの固定消費（アイテム→設備）。`RatePerMinute × 設備の切上げ台数`（J・V の規則どおり）
- **EnvironmentConsume**：環境消費（消費アイテム→供給設備）。`EnvironmentRequirement.ConsumeRatePerMinuteTotal`
- **Gathered**：採取供給（採取ノード→アイテム）。`Supplies` の Gathered 合算

`unadjusted=true` のときは RecipeInput・RecipeOutput の流量に `ResultViewBuilder` と同じ設備倍率 `s(F)` を掛ける（表示中のビューと値を一致させる）。
このため `ResultViewBuilder` に `FacilityScales(plan, snapshot)` の internal 入口を追加し、倍率計算を共有する。

層割りは最長パス法とする。
採取ノードと、供給元を持たないアイテム（未充足のみ）を rank 0 に置き、`rank(n) = 1 + max(rank of predecessors)` を反復して確定する。
循環依存（`CycleDetected` で計算が打ち切られた残存経路を含む）はランク付けの際に後退エッジとして無視し、無限ループにしない。
ランク内の順序は先行ノードのバリセンター（順序値の中央値）で並べ、同率はノード Id の昇順とする。

輸送容量超過フラグはアイテムごとに `max(RequiredPerMinute, 生産量, 採取量) / 60 > 容量`（ベルト 30・パイプ 60 個/秒）で判定する。
計算本体の警告判定と同じ 3 系統の最大流量を見るため、警告発火とグラフの赤化は一致する。
容量定数は `ProductionCalculator` の `BeltCapacityPerSecond`・`PipeCapacityPerSecond` を `internal` から `public` へ変更して共用する。

## 4. 描画（flow-graph.js）

`wwwroot/js/flow-graph.js` は ES module で、`create(canvas, layer)` が `null` または描画ハンドル `{ update, refit, dispose }` を返す。

- `create(canvas, layer)`：`navigator.gpu` の有無と `requestAdapter`/`requestDevice` の成否を判定し、失敗時は `null` を返すだけで何もしない
- `update(model)`：ノードのランクと順序からピクセル座標を決め（rank→横、rank 内順序→縦）、各 DOM ノードへ CSS `translate` を設定し、エッジのベジェ曲線を三角形帯へ展開して頂点バッファを作り直す
- 粒子はエッジごとにインスタンスを持ち、頂点シェーダで `time` ユニフォームからベジェ上の位置を評価する。個数と速度はエッジ流量の最大値に対する比率で決める
- 描画パイプラインはエッジ帯と粒子ビルボードの 2 本のみ（ノード・ラベル・アイコンは DOM のため WGSL では扱わない）
- パンは canvas 上のドラッグ、ズームはホイール（カーソル中心、0.4〜1.5 倍にクランプ）。canvas のユニフォーム行列と DOM ノード層の CSS transform に同じ変換を適用する
- 描画ループは `requestAnimationFrame` で回し、画面外（IntersectionObserver）とタブ非表示（visibilitychange）で停止する。`prefers-reduced-motion` のときは粒子を流さず静止描画する
- 失敗時・未対応時に例外を Blazor 側へ投げ返さない（`null` 返却で静かにフォールバック）

## 5. UI 組み込み（App）

- `Components/FlowGraph.razor` を新設する。`Model`（`FlowGraphModel`）をパラメータに取り、`OnAfterRenderAsync` でモジュールを初期化し、モデル変更ごとに `update` を呼ぶ。`DisposeAsync` でモジュールを開放する。`create` が `null` を返したときはコンポーネント自体を非表示にし、親へ `OnUnavailable` で通知する
- `Home.razor` の結果パネル先頭にグラフ節を追加する。ツールバーに「グラフ」切替（`.seg` ボタン）を加え、WebGPU 利用可能時のみ表示する。既定はビューポート 781px 以上で展開、未満は折りたたみ（仕様決定 AK）
- 素材行（`.mat-row`）と設備行（`.result-line`）に `data-flow-ref="<ItemId|FacilityId>"` を付ける。ノードクリックで `querySelector` して `scrollIntoView` し、短時間ハイライトする
- グラフの更新は `RebuildView` で `_flowModel = FlowGraphModelBuilder.Build(...)` を作り直してコンポーネントへ渡す
- CSS：`.flow-graph-wrap` は高さ 420px 固定・内部パン/ズーム。ノードカード `.fnode` は幅 148px にアイコン 36px を置く。ズーム上限 1.5 まででアイコンの見た目上の大きさは 54px 未満となり、原寸（128px 以下、仕様決定 AA）を超える拡大表示にならない（仕様決定 AL）

## 6. 確定した UI 上の判断

実装中に変更する場合は本節を更新する。

1. canvas はエッジと粒子のみを描き、ノード・ラベル・アイコンは DOM カードで構成するハイブリッドとする（仕様決定 AJ。テキスト品質・アクセシビリティ・実装量のいずれでも有利）
2. グラフの既定はビューポート 781px 以上で展開、非対応環境はグラフ領域と切替ボタンを出さない（仕様決定 AK）
3. アイコンはノード内 36px 表示とし、ズーム上限 1.5 を掛けても原寸を超えない（仕様決定 AL、ユーザー指針どおり大きく表示しない）
4. 採取供給は共通の採取ノード 1 つに集約する。ラベルは「採取」とし、マップ選択は採取可否を決める条件として残すのでグラフにはマップ名を出さない
5. 環境は供給設備ノードの注記（散布機台数）＋消費エッジ（EnvironmentConsume）で表し、環境自体のノードは作らない
6. 輸送容量超過はアイテムノードの赤縁とその流入エッジの赤化で表す（§3 の判定は計算本体の警告と同じ基準）
7. グラフは「調整済／未調整」切替に追従して流量・台数を変える（§3 の倍率共有）
8. エッジ色は種別で分ける：RecipeOutput はアクセント、Gathered は緑系、RecipeInput・FixedConsumption・EnvironmentConsume はミュート色、容量超過は赤

## 7. テスト

[test-specification-phase15.md](test-specification-phase15.md) に従う。
`FlowGraphModelBuilder` は xUnit で採番してカバーし、razor の UI 状態遷移と canvas の描画内容は従来どおり対象外とする。
ブラウザプレビューによるユーザー確認は実装後に別途挟む（ui-mock-first ルール）。

## 8. 受け入れ条件

- WebGPU 対応ブラウザで計算実行後、採取→設備→アイテムの層状グラフが描画され、粒子が流量に応じて流れる
- ノードクリックで対応行へスクロール・強調され、リストとグラフの数値が一致する
- WebGPU 非対応環境ではグラフ領域・切替ボタンが出ず、リスト表示だけになる
- スマートフォン幅ではグラフが既定で折りたたまれる
- `dotnet build` と `dotnet test` が全緑である
