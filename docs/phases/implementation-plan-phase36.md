# Phase 36 実装詳細計画

**対象フェーズ**: Phase 36（グラフ描画領域の自動縦幅拡張とグラフツールバー集約）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 CM・CN。関連: AO・BK・BL）
**関連ドキュメント**: [test-specification-phase36.md](test-specification-phase36.md)（本 Phase のテスト仕様）

> 本書は Phase 36 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書は計画 PR（文書のみ）で先行し、実装・テストは本書に基づく後続の実装 PR で main へマージする（Phase 34・35 と同じ計画・実装の分割）。
> Phase 番号は 36 とする（Phase 35 は PR #93 が計画中のため空き番号を避けた）。仕様決定は CL の次の採番で CM・CN、手動確認 ID は MN-148 以降を使う（PR #93 が MN-145〜147 を使用）。

## 1. スコープ

### 背景と問題

描画領域は既定 420px（モバイル幅 300px）で、手動リサイズまたは最大化でのみ拡げられる（仕様決定 BK）。
`fitView()` は領域へグラフ全体を収めるため等倍未満へ縮小するのみで、縦に深い計画ではノードが読めないほど小さくなる。
深い計画を開くたびに手動リサイズか最大化が必要になるのが現状の痛点であり、内容に応じて領域を自動で伸ばすことが本 Phase の目的である。

「設備を台数分表示」の切替は、最大化オーバレイの外側にある結果ツールバーへ置かれている。
最大化中はオーバレイが画面を覆うため切替に到達できず、台数分表示のまま最大化すると切り替えられない。

### やること

- 描画領域の高さを、グラフ世界矩形（ノード群と縦表示の後退エッジ側面ループ張り出し込み）の高さがズーム 1.0 で収まる値へ自動で拡張する（仕様決定 CM）
- 下限を既定値（420px・モバイル幅 300px）、上限をビューポート高さとする。ビューポートが下限を下回る画面では下限を優先する。縦・横表示の双方へ適用する
- 領域高の自動適用ごとに `refit` を呼びフィットを掛け直し、等倍で全内容が見える表示へ戻す
- 手動リサイズのドラッグ確定で手動モードへ移行して自動拡張を停止し、リサイズハンドルのダブルクリックで手動指定を解除して自動へ復帰する
- 最大化中は自動拡張を休止し、復帰時に再評価する
- 「設備を台数分表示」切替を結果ツールバーからフローグラフのツールバーへ移設し、最大化オーバレイ内でも切替可能にする（仕様決定 CN）
- 手動確認項目 MN-148〜 を実施する

### やらないこと

- Domain・Application・`FlowGraphModelBuilder` は変更しない。モデル（rank・order・ノード種別）とエッジの表示契約は据え置き
- 横方向の自動幅拡張は行わない。領域幅はページレイアウトが決める値であり、横に張り出す内容は従来どおり縮小フィットの対象とする
- 高さ変更のアニメーションは行わない。即時反映とする
- npm・Node.js のテスト基盤は導入しない（Phase 34 と同じ理由）
- 最大化中に結果ツールバーの他の操作（調整済・単位・期間・グラフ切替）をオーバレイへ複製することはしない。対象は台数分表示の切替だけである

## 2. 変更一覧

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.SharedUi/wwwroot/js/flow-graph.js` | `update()` の末尾で、世界矩形の高さから要求領域高を算出し `dotnetRef.invokeMethodAsync('OnContentHeight', h)` で通知する。算出は `Math.min(Math.max(世界矩形高, minGraphHeight()), Math.max(minGraphHeight(), window.innerHeight))` とし、整数化は `Math.ceil` とする（上限が下限を下回る画面では下限を優先する）。ResizeObserver のコールバックでも直近の世界矩形高から再通知し、ビューポート高・モバイル閾値の変化を追従させる |
| `src/EndfieldAicWeb.SharedUi/Components/FlowGraph.razor` | `[JSInvokable] OnContentHeight` を追加し、自動モード・非最大化のとき実効高さへ適用して `_refitPending` を立てる。`OnAfterRenderAsync` で `_refitPending` 時に `refit` を呼ぶ。実効高さは `Height ?? _autoHeight`（両方 null なら CSS 既定）とする。`.flow-resize` に `@ondblclick` を追加して `HeightChanged(null)` を発火し自動へ復帰する。リサイズの `HeightChanged` は高さが実際に変わったときだけ発火する。`ExpandFacilities`・`ExpandFacilitiesChanged` パラメータを追加しツールバーへチェックを描く |
| `src/EndfieldAicWeb.SharedUi/Components/FlowGraph.razor.css` | ツールバー内チェック（`.check` 相当）の余白を調整する |
| `src/EndfieldAicWeb.SharedUi/Components/CalculatorPanel.razor` | 結果ツールバーの「設備を台数分表示」チェックを削除し、`FlowGraph` へ `ExpandFacilities`・`ExpandFacilitiesChanged` を渡す。変更時に `_graphExpandFacilities` 更新と `RebuildView()` を呼ぶ |
| `docs/decision-records.md` | 仕様決定 CM・CN を追加 |
| `docs/requirements.md` | フローグラフ項（§2.1）に自動縦幅拡張と切替配置を追記 |
| `docs/implementation-plan.md` | Phase 36 のチェックリスト項目を追加 |

## 3. 設計の詳細

### 3-1. 高さ通知と実効高さの適用（仕様決定 CM）

領域高の決定権は Blazor 側（FlowGraph）に置き、JS は「内容が等倍で収まる領域高」を通知するだけとする。
`update()` はレイアウトのたびに世界矩形（`worldBounds`）を確定しており、等倍で全内容を収める領域高はその高さに等しい。
`Math.ceil` で整数化するのは、`canvas.clientHeight` が要求値をわずかに下回ると `fitView` の拡大率が 1.0 未満へ落ちるためである。

通知値は `Math.min(Math.max(世界矩形高, minGraphHeight()), Math.max(minGraphHeight(), window.innerHeight))` とする。
下限は `minGraphHeight()` が返す既定値（幅 780px 以下で 300px、それ以外で 420px）で、手動リサイズの下限と同じ関数を使って二重管理を避ける。
上限は `window.innerHeight` で、最大化オーバレイが領域へ与える実効高さとほぼ等しい。
ビューポートが下限を下回る画面（例: 高さ 400px 未満）では下限を優先し領域は既定値を維持する。既存 CSS の `min-height`（420px・モバイル 300px）がインラインの高さ指定より常に優先されるため、上限優先にすると通知値と実効高さが食い違うからである。
上限を超える内容を持つ計画では領域は上限で止まり、縮小フィット・パン・最大化といった従来の見方へ戻る。

`FlowGraph` 側は次の状態を持つ。

- `_autoHeight`：JS が通知した自動算出高（最後に受け取った値を保持）
- 実効高さ：`Height ?? _autoHeight`。`Height`（親が保持する手動指定）は数値なら手動モード、null なら自動モードを表す

`OnContentHeight` の処理は次の通り。

1. `_autoHeight` を通知値で更新する（最大化中・手動モードでも保持だけは更新する。復帰時の再計算を不要にするため）
2. 自動モードかつ非最大化で、通知値が現行の実効高さと異なるときだけ `_height = 通知値` と `_refitPending = true` を立てる
3. `OnAfterRenderAsync` で `_refitPending` が立っていれば `handle.refit()` を呼んで解除する。DOM への高さ反映のあとでないと `fitView` が古い領域高を読むため、通知を受けた直後ではなく描画後のこの位置で呼ぶ

領域高が変わるたびにフィットを掛け直すのは、拡張の目的が「等倍で全内容を見せる」ことにあるためである。
高さが変わらない通知では `refit` を呼ばず、ユーザーのパン・ズームは維持される。

### 3-2. 手動モードと自動への復帰（仕様決定 CM）

手動モードは `Height` が数値を持つ状態、自動モードは null の状態である。
親（CalculatorPanel）が保持する `_graphHeight` は BK どおりセッション内のみ保持され、自動算出値は親へ通知しないため、再マウント後は `update()` の通知から自動高さが再計算される。

- ドラッグ確定（`OnResizeEnd`）：実効高さが開始時と変わったときだけ `HeightChanged` へ値を渡し、手動モードへ移行する。位置を動かさなかったクリックでは発火しない。これにより、ハンドルを誤って一度だけ押した場合に自動モードが意図せず止まることを防ぐ
- 自動への復帰：`.flow-resize` の `@ondblclick` で `HeightChanged(null)` を発火し、親の `_graphHeight` を null へ戻す。ハンドルの `title` は「ドラッグで高さを変更・ダブルクリックで自動に戻す」とする
- 最大化中：オーバレイ内の領域は `height: auto !important` で画面を埋めるため自動拡張を休止し、`_autoHeight` の保持だけを続ける。復帰（最大化解除）では `OnParametersSet`→`update()`→通知の経路で再評価される

### 3-3. 台数分表示切替の移設（仕様決定 CN）

`FlowGraph` に `ExpandFacilities`（bool）と `ExpandFacilitiesChanged`（`EventCallback<bool>`）を追加し、`.flow-toolbar` 内の縦横切替と最大化ボタンの間へチェックを描く。
`Vertical`・`Height`・`Maximized` と同じ双方向バインドの形に揃える。

CalculatorPanel は結果ツールバーのチェックを削除し、FlowGraph へ `ExpandFacilities="_graphExpandFacilities"` と変更コールバックを渡す。
コールバックでは `_graphExpandFacilities` の更新と `RebuildView()` を呼ぶ（現行の `OnGraphExpandChanged` と同じ処理）。

移設は 1 箇所だけとし、結果ツールバーとオーバレイの両方へ置く複製はしない。
複製だと同一状態を持つ 2 つの操作点が生まれ、通常表示でも操作点が 2 箇所に分かれるためである。

### 3-4. 暫定解釈

- 自動モード中にユーザーがパン・ズームを変えても、次に領域高が変わる適用（内容・ビューポートの変化）でフィットへ戻る。これは「自動モードは常に全内容を見せる」という CM の意図に一致するとして、追加の保持機構は設けない
- 再マウント直後は `update()` の通知が届くまで CSS 既定の高さで表示され、その後に自動高さへ移る。一瞬の高さ変化は許容とする
- 台数分表示の切替でモデル（ノード数）が変わると世界矩形も変わるため、切替後に自動高さが再適用される。これは通知経路が `update()` に載ることで自然に満たされる

## 4. 受け入れ条件

- 深い計画で領域が既定値を超えて自動で伸び、ズーム 1.0 前後で全ノードが読めるサイズで表示される
- 内容がビューポート高さを超える計画では領域は上限で止まり、従来どおり縮小フィットで表示される
- ビューポート高さが既定値（420px・モバイル幅 300px）を下回る画面では、領域は既定値を維持して下限を下回らない
- ハンドルドラッグで高さを変えると手動モードへ移り、以後の再計算・ビューポート変化で高さが自動で変わらない
- ハンドルのダブルクリックで自動モードへ戻り、内容に合わせた高さへ再適用される
- 小さいグラフでは 420px（モバイル幅 300px）を下回らない
- 最大化中は領域が画面を埋めたままで、復帰すると自動高さへ戻る
- 「設備を台数分表示」チェックがグラフのツールバーにあり、通常・最大化の双方で切替が効く
- `dotnet build`・`dotnet test` 全緑、`tools/validate_master.py` 通過（データ未変更の回帰確認）
