# Phase 34 テスト仕様

**対象フェーズ**: Phase 34（フローグラフ描画の堅牢化：実行時退避・時刻精度・ピクセル比上限）
**前提ドキュメント**: [implementation-plan-phase34.md](implementation-plan-phase34.md)、[decision-records.md](../decision-records.md)（仕様決定 CJ・CK。関連: AK・BJ〜BL）
**関連ドキュメント**: [test-specification-phase33.md](test-specification-phase33.md)（MN 採番の先行）

> 本書は Phase 34 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 変更は JS モジュールとコンポーネントの相互運用に閉じ、Domain・Application の契約は変わらないため xUnit の新規 ID は設けない。手動確認は MN- 連番を継続（MN-138〜。Phase 33 が MN-135〜137 を使用済み）。着手時に現行最大を再確認すること。

## 1. 自動テスト（xUnit）

新規ケースなし。変更対象は `flow-graph.js` と `FlowGraph.razor` の相互運用であり、Domain・Application の入出力契約は不変である。既存テスト全緑（`dotnet test`）を回帰として確認する。

## 2. 手動確認項目（ブラウザプレビュー）

WebGPU 対応 Chrome で公開アプリの計算ページを開き、計算を実行してグラフを出す。失敗の注入と時刻リセットの通過は、DevTools コンソールからモジュールを import して検証用エクスポートを呼ぶ方法で行う。

```js
const m = await import('./_content/EndfieldAicWeb.SharedUi/js/flow-graph.js');
const cv = document.querySelector('.flow-canvas');
m.debugFail(cv);            // 失敗経路の注入
m.debugAdvance(cv, 1100);   // 時刻リセット（1024 秒）の通過
```

失敗を注入した canvas のハンドルはレジストリから外れるため、`debugAdvance` は `debugFail` と同じ表示では通せない。時刻リセットの確認（MN-143）は失敗系の確認（MN-139〜141）とは別の表示、または注入前に行う。

| ID | 確認内容 | 手順 | 期待 |
|---|---|---|---|
| MN-138 | グラフ描画の回帰 | WebGPU 対応環境で計算を実行しグラフを開く | エッジ・粒子・パン・ピンチ・ズームボタン・最大化・縦横切替が従来どおり動く |
| MN-139 | 描画失敗時の退避 | グラフ表示中に `debugFail` を呼ぶ | グラフ節（切替ボタン・canvas・ズーム一式）が消えてリスト表示へ戻る。コンソールに `console.warn` が 1 回出る |
| MN-140 | 失敗注入の多重呼び出し | MN-139 のあと再度 `debugFail` を呼ぶ | no-op で動作し、追加の通知・例外が出ない |
| MN-141 | 失敗後の操作 | MN-139 のあと、リスト行のクリック・再計算を行う | 例外なく動作し、グラフ節は畳まれたままになる（当該マウントでの復帰はない） |
| MN-142 | 非対応環境の回帰 | WebGPU を無効化したブラウザ（`--disable-webgpu` 等）で計算を実行 | グラフ節が出ず、従来どおりリスト表示のみになる |
| MN-143 | 時刻リセットの無害性 | グラフを出して `debugAdvance(cv, 1100)` を呼び、粒子を観察する | リセット前後で粒子の位置がジャンプせず、アニメーションが続く |

上記に加え、ピクセル比上限を検証する。DevTools のデバイスエミュレーションで `devicePixelRatio` を 3 に設定してグラフを表示し、コンソールで canvas の実バッファサイズを確認する。

| ID | 確認内容 | 手順 | 期待 |
|---|---|---|---|
| MN-144 | DPR 上限の適用 | dpr=3 エミュレーションで `cv.width / cv.clientWidth` を確認 | 2 を超えない（≈ 2）。パン・ズーム・クリック座標の狂いがない |

## 3. 受け入れ条件との対応

- MN-138 が既存機能の回帰を、MN-139〜141 が仕様決定 CJ の退避経路を、MN-142 が初期化時退避の回帰を、MN-143 が時刻リセットの無害性を、MN-144 が仕様決定 CK をカバーする
- `dotnet build`・`dotnet test` 全緑と `tools/validate_master.py` 通過を併せて確認する
