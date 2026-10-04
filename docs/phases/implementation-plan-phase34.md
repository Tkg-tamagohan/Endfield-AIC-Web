# Phase 34 実装詳細計画

**対象フェーズ**: Phase 34（フローグラフ描画の堅牢化：実行時退避・時刻精度・ピクセル比上限）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 CJ・CK。関連: AI・AK・BJ〜BL）
**関連ドキュメント**: [test-specification-phase34.md](test-specification-phase34.md)（本 Phase のテスト仕様）

> 本書は Phase 34 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。
> Phase 番号は 34 とする。仕様決定は CI の次の採番で CJ・CK、手動確認 ID は MN-138 以降を使う。
> 本 Phase は外部リポジトリ [noxellab/nagi-ocean-sim](https://github.com/noxellab/nagi-ocean-sim)（Three.js/WebGL2 の海シェーダーデモ、MIT）の作法を、WebGPU で描く生産フローグラフの堅牢化へ転用するものである。転用するのは考え方（ランタイム降格・位相積算・ピクセル比上限）であり、コードの転記はない。

## 1. スコープ

### 背景と問題

`flow-graph.js` は WebGPU 非対応時に `create` が `null` を返し、`FlowGraph` が `OnUnavailable` 経由でグラフ節を畳む設計（仕様決定 AK）を持つが、対象は初期化時だけである。描画中のデバイスロストや描画例外が起きると、`frame()` 内の `getCurrentTexture()` 等が例外を投げて rAF が再登録されず、canvas が最終フレームで凍結したまま残る。リスト表示への退避も起きない。

もう一つの潜在問題は時刻の精度である。`applyView` は `(performance.now() - startTime) / 1000` を f32 uniform `u.time` へ毎フレーム書き、シェーダーで `fract(u.time * speed + phase)` を計算する。u.time が大きくなると f32 の量子化誤差が粒子位相へ出始める。長時間開き続けた画面で粒子が微細に震える可能性がある（推測。§4 に根拠を書く）。

### やること

- 描画の実行時失敗（デバイスロスト・`uncapturederror`・`frame()` の例外）を検知し、一度だけ .NET 側へ通知してグラフ節を畳む退避経路を追加する（仕様決定 CJ）
- 失敗後のハンドル操作（`update`・`refit`・`zoomStep`・`dispose`）を無害な no-op にし、`fail` を冪等にする
- `u.time` が閾値を超えた時点で、各粒子の位相へ時間積算分の小数部を畳み込み、時刻基準をリセットする（見た目は不変の内部処理）
- canvas バッファのピクセル比を `min(devicePixelRatio, 2)` に上限付けする（仕様決定 CK）
- E2E から失敗経路と時刻リセットを発火できる検証用エクスポート `debugFail(canvas)`・`debugAdvance(canvas, seconds)` を追加する
- 手動確認項目 MN-138〜143 を実施する

### やらないこと

- npm / Node.js のテスト基盤（jsdom＋モック GPU で `node --test` を回す nagi 式ハーネス）は導入しない。純粋関数の資産が少なく、package.json の維持コストに見合わないため（後述の協議記録参照）
- グラフの見た目・レイアウト・粒子密度・色は変えない。仕様決定 AP・BF〜BH・BJ〜BL・BO・CD の表示契約は据え置き
- `webgpuSupported`・`create` の非対応判定ロジックは変えない
- 失敗時の自動復帰（デバイス再取得・canvas 再構成）は実装しない。復帰は次回マウント（計算の再実行）に委ねる（仕様決定 CJ）
- `CalculatorPanel.razor` は変更しない（既存の `OnUnavailable` 経路を再利用する）

## 2. 変更一覧

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.SharedUi/wwwroot/js/flow-graph.js` | `create(canvas, layer, dotnetRef)` の第 3 引数（任意）を追加。`makeHandle` 内に失敗通知 `fail(err)` を実装し、`device.lost` ・`uncapturederror`・`frame()` 全体の try/catch から呼ぶ。`update` の先頭に `destroyed` ガードを追加。`applyView` に時刻リセットを実装。DPR 取得を共通関数化して上限 2 を適用。`debugFail`・`debugAdvance` をエクスポートし、canvas → 内部ハンドルの WeakMap レジストリを設ける |
| `src/EndfieldAicWeb.SharedUi/Components/FlowGraph.razor` | `create` の呼び出しに `_selfRef` を渡す。`[JSInvokable] OnGraphFailed` を追加し、ハンドルの best-effort dispose 後に `OnUnavailable` を発火する |
| `docs/decision-records.md` | 仕様決定 CJ・CK を追加 |
| `docs/requirements.md` | フローグラフ項（§35 相当箇所・§323 相当箇所）に実行時退避とピクセル比上限を追記 |
| `docs/implementation-plan.md` | Phase 34 のチェックリスト項目を追加 |

## 3. 設計の詳細

### 3-1. 実行時失敗の検知と通知（仕様決定 CJ）

`makeHandle` 内に冪等の `fail(err)` を置く。処理は次の通り。

1. `destroyed = true`・`running = false` とし、保留中の rAF をキャンセルする
2. GPU リソース（edgeVertexBuffer・particleInstanceBuffer・edgeParamBuffer・quadBuffer・uniformBuffer）を try/catch 内で best-effort 破棄する。デバイスロスト後の `destroy()` が投げても通知処理を止めないためである
3. `dotnetRef.invokeMethodAsync('OnGraphFailed')` を一度だけ呼ぶ（`failed` フラグで多重通知を防ぐ。呼び出し側切断に備え `.catch(() => {})` を付ける）
4. `console.warn` に理由を残す。ページ上の警告文面は出さない（仕様決定 CF の「ページに表示する警告」に該当しない内部通知である）

失敗の検知は 3 経路とする。

- `device.lost.then(info => ...)`：devise 喪失の正式経路。`dispose` 内の `device.destroy()` が発火する `reason === 'destroyed'` は `destroyed` フラグで除外する
- `device.addEventListener('uncapturederror', ...)`：パイプライン作成以降の検証エラーを捕捉する。ここで拾うエラーは描画が破綻している兆候であり、グラフを畳んでリストへ戻すほうが利用者に優しい
- `frame()` の try/catch：`getCurrentTexture()` や `queue.submit` の実行時例外を捕捉する。例外後は rAF を再登録せず `fail` へ渡す

`FlowGraph.razor` 側の `OnGraphFailed` は、JS ハンドルの dispose を試みてから `_handle = null` とし、`_unavailableNotified` を再利用して `OnUnavailable` を一度だけ発火する。結果として `CalculatorPanel` の `_graphSupported` が false になり、初期化失敗時と同じくグラフ節（切替ボタン・最大化・canvas 一式）が DOM から外れてリスト表示へ戻る。当該マウント内での復帰は行わない。

### 3-2. 時刻リセット（内部処理、仕様決定の対象外）

`u.time` を無制限に伸ばさず、閾値（1024 秒）を超えた時点で次の処理を行う。

1. 各粒子インスタンスの位相 `p` を `fract(T * s_e + p)` へ書き換える（T はリセット時点の経過秒、s_e は粒子が属するエッジの速度）。`fract` は整数シフトに対して不変なので表示は連続する
2. `startTime` を現在時刻へ再設定し、以後の `u.time` を小さい値に戻す
3. 粒子インスタンスバッファを `writeBuffer` で更新する（バッファの再確保・バインド組み替えは不要）

インスタンスの位相を直接畳み込むのではなく初期位相 `i / count` を保持して毎回再計算してもよいが、実装が等価に単純なため、現在値への畳み込みとする。時刻と位相の整合が取れる限りリセット間隔は任意であり、1024 秒は f32 の誤差が視認に遠く及ばない余裕を持つ値として選ぶ。

`applyView` が `u.time` を書く唯一の場所なので、リセット判定はそこに置く。`reducedMotion` の静止画モードでは `u.time` を使わないが、処理は無害なので分岐しない。

### 3-3. ピクセル比の上限（仕様決定 CK）

`resize()` と `applyView()` で使う `devicePixelRatio` を共通関数 `effDpr()` に集約し、`Math.min(devicePixelRatio || 1, 2)` とする。両箇所で同じ値を使わないと変換行列とバッファサイズがずれるため、集約は必須である。devicePixelRatio はページズームやモニタ間移動で変わるため、呼び出しごとに評価する。

ノードの文字とアイコンは DOM 側の描画であり、上限で劣化するのはエッジ帯と粒子だけである。実線と点の 2D 描画なので dpr2 と dpr3 の視認差は小さいと推測する。

### 3-4. 検証用エクスポート

E2E 検証が失敗経路と時刻リセットを外部から発火できるよう、モジュールに WeakMap の `canvas → ハンドル` レジストリと次のエクスポートを追加する。ハンドルは `makeHandle` で登録し、`dispose`・`fail` で削除する。

```js
export function debugFail(canvas)        // 登録済みハンドルの fail を合成エラーで起動する
export function debugAdvance(canvas, s)  // startTime を s 秒だけ過去へずらし、次フレームでリセットを通す
```

いずれも未登録・破棄済みなら no-op とする。実装コストはレジストリ込みで十数行であり、テスト目的以外の呼び出しは UI から到達不能である。

## 4. 現状とのパフォーマンス比較予測

測定ではなくコード読みに基づく推定である。実測が必要なら WebGPU 対応環境での E2E（MN-138〜143）の際に併せて見る。

| 項目 | 現状 | 変更後（予測） |
|---|---|---|
| 定常フレームの CPU 処理 | `applyView` の uniform 書換・エンコーダ構築・1 パス投入 | 同一。追加は `applyView` の閾値比較 1 回と `frame()` の try/catch で、現代の JIT では測定不能な差と推測する |
| 定常フレームの GPU 処理 | エッジ帯（1 エッジあたり 120 頂点）＋粒子（エッジあたり最大 10 インスタンス） | 同一。dpr ≤ 2 の環境ではピクセル数も変わらない |
| 時刻リセット | なし | 1024 秒ごとに一度、粒子数分の Float32 書換と `writeBuffer` 1 回。数十エッジの計画でも 0.1ms 未満と推測する |
| 高 DPR 端末の充填コスト | バッファは `dpr²` 比例。dpr3 で 9 倍相当 | `min(dpr, 2)` で上限。dpr3 端末では充填ピクセル数が約 44% 減（(2/3)²）。canvas バッファのメモリも同率で減る |
| 描画失敗時の挙動 | rAF が再登録されず canvas が凍結し、次のマウントまでグラフが残る | 一度だけ .NET へ通知してグラフ節を畳み、リスト表示へ戻る |
| 長時間表示の安定性 | u.time の f32 化により、連続表示が 36 時間級に及ぶと粒子位相に視認しうる量子化誤差が出る可能性がある（推測） | 時刻が常に 1024 秒未満へ保たれ、誤差は累積しない |

速度の改善を目的とする変更ではない。ユーザーが体感する差は、デバイスロスト時にグラフが残り続けず畳まれる点と、dpr3 超の端末でわずかに軽くなる点に限られる。

## 5. 受け入れ条件

- WebGPU 対応環境でグラフが従来どおり描画され、粒子・パン・ズーム・最大化・縦横切替に回帰がない
- `debugFail` による失敗注入でグラフ節が畳まれ、リスト表示へ戻る。以後のハンドル操作は例外を投げない
- WebGPU 非対応環境では従来どおり初期化時点でリスト表示のみになる（AK の回帰）
- `debugAdvance` で時刻リセットを通しても粒子がジャンプせず動き続ける
- dpr > 2 のエミュレーションで `canvas.width <= clientWidth * 2` が成立する
- `dotnet build`・`dotnet test` 全緑、`tools/validate_master.py` 通過（データ未変更の回帰確認）
