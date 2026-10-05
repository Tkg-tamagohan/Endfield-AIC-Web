# Phase 38 実装詳細計画

**対象フェーズ**: Phase 38（計算ページの描画・再レンダー軽量化）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 CQ。関連: BC・BK・CK・CM）
**関連ドキュメント**: [test-specification-phase38.md](test-specification-phase38.md)（本 Phase のテスト仕様）

> 本書は Phase 38 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書は計画 PR（文書のみ）で先行し、実装・テストは本書に基づく後続の実装 PR で main へマージする（Phase 34〜37 と同じ計画・実装の分割）。
> Phase 番号は 38 とする（Phase 35〜37 の計画はマージ済みで、実装は並行改修として進行中）。仕様決定は CP の次の採番で CQ（CP は並行作業の PR #97 が採番したため、当初案の CP から改番した）、手動確認 ID は MN-166 以降を使う（main の現行最大は Phase 37 の MN-165）。xUnit の新規 ID は管理ツール向けの ADM-14 以降を使う（main の現行最大は ADM-13）。

## 1. スコープ

### 背景と問題

「4K 画面にブラウザを移動すると極端に重くなる」という報告を起点に、計算ページの描画・再レンダーまわりを調査した。
解像度に比例して重くなる経路はフローグラフの canvas 描画に集中していたが、併せて「再計算や再レンダーのたびに不要な全量処理が走る」箇所が複数見つかった。

調査時のユーザー環境（chrome://gpu レポート）は、全モニターが scale=1（DPR=1）の 3 画面構成で、Chrome は内蔵 GPU（AMD Radeon Graphics、ANGLE D3D11・Dawn D3D12 とも iGPU）で描画していた。
この構成でグラフを最大化すると、canvas は約 830 万 px のバッファを 60fps で全面更新し続ける。
フローグラフは粒子アニメーションのため常時全画面を再描画する設計であり、充填コストがバッファのピクセル数にそのまま比例するため、画面が大きいほど iGPU での描画負荷が増える。

canvas のピクセル数以外にも、次の「変更なしでも全量処理が走る」経路が計算ページの軽さを損なっている。

1. `FlowGraph` の `update()` が親レンダーごとに全ジオメトリ（ノード計測・ベジェ・GPU バッファ）を再構築する。`OnParametersSet` が無条件で `_updatePending` を立てるため、レート入力のキーストローク・最大化トグル・高さ確定でも発火する
2. 管理ツールの計算プレビューが、再計算のたびに全文書検証（`Store.Validate()`）とスナップショット再構築（`MasterSnapshotFactory.Create`）を実行する。文書が未編集でも同じ処理が走る
3. 高さハンドルのドラッグ中、pointermove ごとに `_height` が更新されてパネル全体が再レンダーされる
4. `CalculatorPanel` がレンダーのたびに派生リスト・ルックアップ（マップ候補・有効イベント一覧・アイテム候補・環境行の検索）を組み直す
5. `applyView()` が view が不変でも毎フレーム `layer.style.transform` と uniform を書き込み、リサイズ時は ResizeObserver コールバックごとに canvas バッファを再確保する

本 Phase ではこれらを、表示・挙動の仕様を変えない範囲でまとめて軽減する。

### やること

- `FlowGraph` の JS `update()` 呼出を、モデル・表示方向・書式の変化時のみに限定する
- `AdminDocumentService` で検証結果とスナップショットを編集世代でメモ化し、未編集の連続再計算で全文書検証・再構築を省く
- 高さハンドルのドラッグ中は JS 側で領域高を追従し、確定時のみ Blazor 側の状態へ反映して pointermove ごとの全面再レンダーを抑える
- `CalculatorPanel` のレンダー毎の派生リスト・ルックアップを、各対象の変化点で更新するキャッシュへ置き換える
- `flow-graph.js` の描画負荷を下げる。canvas バッファの総ピクセル上限（仕様決定 CQ）、`applyView()` の transform・uniform 書き込みの差分適用、リサイズ時のバッファ再確保の安定化遅延、DPR 変化の検知を行う
- 管理ツールのエクスポート・アイコン系 JS を、機能の初回利用時に読み込む遅延ロードへ切替える
- xUnit ADM-14〜 と手動確認 MN-166〜 を実施する

### やらないこと

- 入力中の再レンダー抑制（`oninput` から `onchange` への変更）や結果パネルのコンポーネント分割はしない。`oninput` は打鍵ごとにパネル全体を再レンダーする現行の挙動であり、変更には仕様協議が要るため別途扱う
- `RebuildView` によるグラフモデルの常時構築は維持する。遅延構築への変更は仕様決定 BC の改定を伴うため対象外とする
- WGSL シェーダ・粒子シミュレーションのパイプライン自体の最適化はしない。充填コストは総ピクセル上限で抑える方針とする
- Blazor WASM の AOT・トリミング・配信設定の変更はしない
- OS・ブラウザ側の GPU 選択（Chrome を高性能 GPU へ割当てる設定）は、コード外の対処としてユーザーへ別途案内済みであり、本 Phase の作業には含めない

## 2. 変更一覧

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.SharedUi/Components/FlowGraph.razor` | `update()` の発火をパラメータ差分検出へ変更し、リサイズドラッグのポインタ追跡を JS 側へ委譲する |
| `src/EndfieldAicWeb.SharedUi/Components/CalculatorPanel.razor` | 単位切替を `RebuildView()` 経由にし、派生リスト・ルックアップをキャッシュする |
| `src/EndfieldAicWeb.SharedUi/wwwroot/js/flow-graph.js` | 総ピクセル上限（CQ）・transform/uniform の差分書込み・バッファ再確保の遅延・DPR 変化検知・リサイズドラッグの JS 追跡 |
| `src/EndfieldAicWeb.Admin/Services/AdminDocumentService.cs` | `Validate()` 結果と `Snapshot` を編集世代でメモ化する |
| `src/EndfieldAicWeb.Admin/wwwroot/index.html` と同 `js/` | エクスポート・アイコン系スクリプトを遅延ロードへ切替える |
| 管理ツールのサービス系テスト | ADM-14〜 を追加する |
| `docs/decision-records.md` | 仕様決定 CQ を追加する |
| `docs/requirements.md` | フローグラフ項に CQ の規則を追記する |
| `docs/implementation-plan.md` | Phase 38 のチェックリスト項目を追加する |

## 3. 設計の詳細

### 3-1. `update()` の差分発火

`FlowGraph.razor` の `OnParametersSet` で `_updatePending` を無条件に立てるのをやめ、前回値と比較して変化時のみ立てる。
比較対象は `Model`（参照比較）、`Vertical`、`FormatRate`（`Delegate.Equals` で同一メソッドか判定）とする。
`RebuildView` は再計算のたびに新しいモデルインスタンスを生成するため、参照比較でも再計算時には従来どおり発火する。

`Height`・`Maximized` の変化では発火させない。
canvas のリサイズは ResizeObserver が、最大化の見た目切替は CSS（`.flow-max`）が担うため、ノードの寸法は変わらず再計測は不要である。

単位切替（毎分・毎秒・期間）は、`FormatGraphRate` の出力文字列が変わるがデリゲート自体は同じメソッドのままなので、差分検出では発火しない。
`CalculatorPanel` の `_unit` 代入を `RebuildView()` を呼ぶメソッド経由に変え、新しいモデル参照で発火させる。

初回作成時は従来どおり `update()` が走る（前回値が未設定のため差分検出でも発火する）。

### 3-2. `AdminDocumentService` の検証・スナップショットのメモ化

`Validate()` の結果と `Snapshot` をメモ化する。キーは `Snapshot` が `(Document の参照同一性, _editCounter)`、`Validate()` がこれに不正入力の有無（`HasInvalidInput`）を加えたものとする。
編集ページからの変更通知は `NotifyChanged` が `_editCounter` を増やす現行構造のため、キーに含めるだけで両キャッシュが編集のたびに自然に失効する。
文書の再読み込み（`Document` の差替え）でも参照同一性が外れて失効するため、`_loadGeneration` を判定へ混ぜる必要はない。
検証キーに不正入力を含めるのは、`SetEditorInvalid` が `_editCounter` を増やさないためである。キーに含めなければ、検証後に数値欄へ未確定の不正入力が残ったままでも旧い「エラーなし」が再利用され、拒否されるべき入力のままプレビューが開く。スナップショットは文書内容だけに依存するため、キーは編集世代のみでよく、検証キャッシュとは失効条件を分ける。

キャッシュヒット時も `ValidationErrors`・`ValidationRan` などの公開状態は従来どおり更新する（結果の再代入であり、検証処理の再実行ではない）。
`_counterAtExport`・`_exportedDocument` によるエクスポート判定（IsDirty）には触れない。

呼び出し側（`PreviewPage.EnsureSnapshot` など）は変更しない。
`Store.Validate()` と `Store.Snapshot` が内部でメモ化されるため、マップ選択・環境台数・採取レート・ペア変更のたびの再計算で、文書未編集なら検証とスナップショット構築がスキップされる。

### 3-3. リサイズドラッグの JS 追跡

現行は Blazor の `@onpointermove` で `_height` を毎イベント更新し、確定は `OnResizeEnd` でのみ行う。
この方式ではドラッグ中にパネル全体が pointermove ごとに再レンダーされる。

pointerdown で JS 側の `beginResize(handle, wrap, clientY, startH, minH)` を呼び、JS が window の pointermove・pointerup・pointercancel を捕捉して `wrap.style.height` を直接更新する。
pointerup で確定高を `DotNetObjectReference` 経由で C# 側へ返し、`_height` の確定と `HeightChanged` の通知は従来の確定処理が担う。
ポインタキャプチャと最小高のクランプは従来どおり適用する。

Phase 36（仕様決定 CM・実装進行中）はハンドルに手動/自動モードとダブルクリック復帰を追加する。
ドラッグ確定での手動モード移行・ダブルクリックでの自動復帰・最大化中のハンドル非表示はいずれも変更せず、JS 側は「確定高を返す」までを担当し、モード遷移は C# 側の確定処理へ委ねる。
実装着手時は Phase 36 のマージ後コードへ合わせ、ハンドルのイベント構成（dblclick・pointerdown）が JS 追跡と競合しないことを確認する。

### 3-4. 派生リスト・ルックアップのキャッシュ

`CalculatorPanel` でレンダーのたびに組み直している次のものを、各対象の変化点で更新するキャッシュへ置き換える。

- `MapCandidates`（レンダー中に 2 回評価）: マップ一覧・選択の変化点（初期化・マップ変更・イベント系の確定）と、`RefreshEventViews` による `Checked` 更新の後で再計算して保持する
- `ActiveGameEventIds` の `ToList()`: イベント行の確定・トグル・初期化に加え、`RefreshEventViews` による `Checked` 更新の後でも更新する。`Recalculate` は毎回当日を評価してイベントの既定有効を変え得るため、日付をまたいだ再計算で有効イベントと選べるマップが食い違わないよう、この更新点は外せない
- `ItemOptions(row)` のフィルタ: 行側に候補リストを持たせ、カテゴリ変更・行追加・初期化で更新する
- `EnvCountText`・`EnvMin`・`EnvMax` の `FirstOrDefault`: 環境行の確定点で `id → 環境行` の辞書を作り、描画では辞書引きにする

`IsGatherCapReached`・`GatheredRate`・`GatherRatePlaceholder`・`GatherRateDefaultLabel` は `_calcResult`・`Snapshot` への参照引きが本体でアロケーションは小さい。
同じ方針で揃えられる範囲は揃えるが、状態を増やすほどのものでなければ現状のままとする（暫定解釈）。

### 3-5. canvas 描画の負荷低減（仕様決定 CQ）

`flow-graph.js` に次の変更を入れる。

1. **総ピクセル上限**。`dprFor(cw, ch) = Math.min(DPR_MAX, devicePixelRatio || 1, Math.sqrt(MAX_BUFFER_PIXELS / (cw * ch)))` とし、`canvas.width * canvas.height` が `MAX_BUFFER_PIXELS = 4,194,304`（約 4Mpx）を超えない範囲でバッファを確保する。
寸法は従来どおり丸めで決めた後、実寸法の積を再検査し、上限を超える場合は両辺を `Math.floor` で決め直す。実効比は常に `sqrt(上限 / CSS 積)` 以下なので、floor 後の積 `floor(cw·d) × floor(ch·d) ≤ cw·ch·d² ≤ 上限` が必ず成立する。この再検査は総量制約が効いた分岐だけでなく両分岐で行い、小数の CSS 寸法や未クランプの DPR で丸め上がりが起きても上限を逸脱しないようにする。
4K 最大化（DPR=1・約 830 万 CSS px）では実効比が約 0.71 まで下がり、エッジ帯と粒子がやや粗くなる。
ノードの文字とアイコンは DOM 描画のため上限の影響を受けない（CK のピクセル比上限はそのまま残り、総量上限はその上に積む形になる）。
2. **transform・uniform の差分書込み**。`applyView()` で書き込む transform 文字列・transformOrigin と、uniform のビュー成分（行列・スケール等）を直前値と比較し、変化時のみ DOM・GPU へ書き込む。
uniform にはアニメーション時刻も含まれるため、比較対象はビュー成分に限定し、時刻成分の転送は従来どおり毎フレーム行う。
view が動かない限り毎フレームの style 書込みとスタイル無効化を止める。
3. **バッファ再確保の遅延**。ResizeObserver コールバックでは要求サイズと変化時刻を保持するだけにし、実際の `canvas.width/height` 再代入は `frame()` で「要求サイズが `RESIZE_SETTLE_MS`（実装定数・推奨 50〜100ms）変わっていない」場合にのみ行う。
フレームより遅い間隔で届く連続リサイズでも、安定を「前フレームと同じ」と判定して再確保が繰り返されることがないよう、静止判定は時間閾値で行う。
ドラッグ・モニター間移動のような連続したリサイズでは、フレーム毎に行われていたバッファ再確保が、サイズが安定した時点の 1 回に収束する。
4. **DPR 変化の検知**。`matchMedia("(resolution: <dpr>dppx)")` の change で DPR 変化を検知してリサイズを要求する。
CSS 寸法が不変でもモニター間移動で DPR が変わった場合にバッファが追従する（従来は変換行列と実解像度がずれてエッジがにじむ可能性があったと推測する）。

描画の前提（仕様決定 CJ の失敗退避・時刻リセット・粒子密度規則）は変更しない。

### 3-6. 管理ツール JS の遅延ロード

`index.html` の `download.js`・`pako.min.js`・`UPNG.js`・`icons.js` を常時読み込みから外し、各機能の初回利用時に必要な分だけを読み込むローダー関数を追加する。
依存関係は `icons.js`→`UPNG.js`（APNG のデコード・エンコードで参照）、エクスポート→`pako.min.js`・`download.js` とする。
`ui.js`（`scrollElementIntoView`）は全ページで使う軽量スクリプトのため従来どおり常時読み込みとする。
対象スクリプトと呼出箇所の対応は実装時に確定し、機能が使われるまで script 要素を追加しない形とする（暫定解釈：切り分けの対象は呼出箇所の実測で最終確認する）。
遅延ロードの失敗時は従来どおりエラーを呼び出し元へ返す。

## 4. 受け入れ条件

- 4K 画面・グラフ最大化でも 1 フレームの充填対象が 4,194,304 px を超えず、ノードの文字・アイコンの鮮明さは維持される
- 最大化・高さ確定・レート入力のキーストロークなどモデル不変の再レンダーで `update()` が走らず、再計算・縦横切替・単位切替ではグラフが正しく更新される
- 高さドラッグ中はパネル全体の再レンダーが走らず、確定時に高さが保持され、Phase 36 の手動/自動モードと整合する
- 管理ツールで文書未編集の連続再計算が検証・スナップショット構築を再実行せず、編集後は直ちに新しい結果へ反映される（ADM-14〜）
- 管理ツールの各ページでエクスポート・アイコン操作が遅延ロード経由で従来どおり成功する

## 5. 検証計画

- 自動テスト：`AdminDocumentService` のメモ化を ADM-14〜（xUnit）で検査する。`dotnet test` 全緑を回帰確認し、`tools/validate_master.py` 通過も確認する（データ未変更の回帰）
- 手動確認：`docs/phases/test-specification-phase38.md` の MN-166〜 をブラウザ（WebGPU 対応 Chrome）で実施する
- CDP 計装：検証用に `update` 呼出回数・transform 書込み回数のカウンタを既存のデバッグハンドル機構（WeakMap 経由の検証専用経路）へ追加し、MN-170 で数値を確認する。計装は `debugFail`・`debugAdvance` と同じ検証専用経路のため残してよい

## 6. リスクと引き継ぎ

- Phase 36（CM・実装進行中）は `FlowGraph` のリサイズ・最大化まわりを変更する。実装着手時に main へ取り込み、リサイズハンドル・最大化トグル・自動縦幅の変更点と整合させる。Phase 35（CL・実装 PR #95）・Phase 37（CO）は flow-graph.js のエッジ幾何・選択規則に触れるが、本 Phase の変更箇所（resize・applyView・update 呼出管理・FlowGraph.razor・CalculatorPanel.razor・Admin）とはファイル内で分離できる見込みである
- 総ピクセル上限 `4,194,304` は推奨値である。4K・DPR1 の最大化で実効比は約 0.7 になる。エッジの見た目の劣化が気になる場合は上限値を調整する
- `update()` の差分検出で「発火すべき場面が抜ける」回帰が最も起きやすい。`FormatRate` のデリゲート同一性のもとで出力が変わる経路（単位切替）は RebuildView 経由でカバーするが、同種の経路があれば実装時に洗い出す
- エクスポート系 JS の遅延ロードは、対象スクリプトの利用箇所を呼出側から確認して進める。遅延化で初回操作が待機になる点は変わるが、ローカル配信のため実用上の差は小さいと見込む
