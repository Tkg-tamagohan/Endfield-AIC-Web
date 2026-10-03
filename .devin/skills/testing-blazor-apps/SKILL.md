---
name: testing-blazor-apps
description: Endfield-AIC-Web の Blazor WASM アプリ（App/Admin）をローカルまたは公開 URL でブラウザ確認する手順
---

# Blazor WASM アプリの起動・ブラウザ確認手順

対象は App（:5180）と Admin（:5181）の Blazor WASM スタンドアロン 2 アプリ。計算ページの UI は Phase 22 で共有 RCL `src/EndfieldAicWeb.SharedUi` の `Components/CalculatorPanel.razor` に集約され、App・Admin の両ページは同じコンポーネントをパラメータ違いで描画する。

## 起動

.NET 8 SDK は `~/dotnet` にある。各シェルで `export PATH="$HOME/dotnet:$PATH"` してから、App と Admin を別ポートで起動する。

```sh
dotnet run --project src/EndfieldAicWeb.App --no-launch-profile --urls http://127.0.0.1:5180
dotnet run --project src/EndfieldAicWeb.Admin --no-launch-profile --urls http://127.0.0.1:5181
```

- `--no-launch-profile` を付けないと launchSettings の `launchBrowser: true` でブラウザが自動起動し、ポートも launchSettings 側（5280 等）に吸われる。ポート指定を確実に効かせるには両方付ける。
- Blazor WASM スタンドアロンの `dotnet run` は Blazor Dev Server 経由で静的配信される。SPA フォールバックがあるため `/counter` 等の任意パスも HTTP 200 で `index.html` が返る。「404 確認」はサーバのステータスではなく、WASM ルーターの NotFound ビュー（`App.razor` の `<NotFound>`）が画面上に出ることで確認する。
- `_framework/blazor.webassembly.js`（約 60KB）と `_framework/dotnet.native.wasm`（約 2.9MB、.NET 8 では `dotnet.wasm` ではなくこの名前）が 200 で配信されることを curl で事前確認すると良い。

## 公開サイト（Cloudflare Pages）

- 公開 URL: https://endfield-aic.pages.dev（Pages プロジェクト `endfield-aic`、`deploy-pages.yml` が `artifacts/app/wwwroot` を直接アップロード）
- SPA フォールバックは `_redirects` ではなく Pages 標準挙動（トップレベル `404.html` 不在時に未一致パスが `/` へ回る）。`curl -sI https://endfield-aic.pages.dev/foo` が `200 + text/html` ならフォールバックは機能している。ブラウザではアプリシェル内に「この URL にはページがありません。」が出るのが正しい挙動（Pages 既定の 404 ページではない）。
- `_headers` は `/data/*`・`/_framework/*` に `Cache-Control: public, max-age=0, must-revalidate`（curl の `cache-control` レスポンスヘッダで確認可能）。

## ブラウザ確認の注意

- Chrome は `/home/ubuntu/.local/bin/google-chrome`（Chrome for Testing）。`DISPLAY=:0`。
- WASM 読み込みには数秒かかる。`#app` 内のローディングスピナーが消えて本文が描画されるまで待つ。失敗時は画面下部に `#blazor-error-ui` のエラーバーが出る。
- アドレスバー直打ちでルートに戻るとき、Chrome のオートコンプリートが履歴の `/counter` 等を補完して別パスへ飛ぶことがある。確実に戻るにはトレーリングスラッシュ付きで `127.0.0.1:5180/` と入れる。App にナビリンクはなく、Admin ではナビの「データ管理」リンクが同じ役割を果たす。
- `127.0.0.1` が Chrome で NXDOMAIN になることがある: `http://127.0.0.1:5180/` を開くと `DNS_PROBE_FINISHED_NXDOMAIN` で「This site can't be reached」になり、Reload しても解消しないことがある（curl では 200 が返るのでサーバ側は正常）。その場合は `http://localhost:5180/` / `http://localhost:5181/` で開くと通る（Phase 26 で実測）。
- アドレスバー（ctrl+l）で別ページ URL を打つとアプリがフルリロードされ、読み込み済みの in-memory 文書が消える。ページ間の移動はアプリ内のナビゲーションリンクをクリックしてクライアントサイド遷移する。App は初回ロードで `data/master.json` を自動読込するため復元するが、Admin はホームで「同梱マスタを読み込む」を押し直す必要がある（Phase 20 で実測）。
- `google-chrome <URL>` で起動すると「New Tab」と目的ページの 2 ウィンドウが開くことがある。`read_dom` や `browser_console`、ページ HTML 取得は New Tab 側にバインドされ対象ページの DOM が取れない。テスト対象は同一ウィンドウのタブに集約し（ctrl+t で開く）、余分なウィンドウは閉じるか無視してスクリーンショット中心で検証する。

## レスポンシブ確認

- 計算 UI（`src/EndfieldAicWeb.SharedUi/Components/CalculatorPanel.razor`）の 2 カラム `.layout` は **780px 未満**で 1 カラム化（スコープド CSS の `CalculatorPanel.razor.css` にある `@media (max-width: 780px)`）。780px は Chrome 最小幅を上回るため、`wmctrl -e` のウィンドウリサイズだけで検証できる。
- Chrome の最小ウィンドウ幅は約 500px で、`wmctrl -e` でのリサイズは 530px 程度までしか縮められない。真の 375px 幅が必要ならウィンドウリサイズではなく CDP のデバイスエミュレーションを使う。
- flex-wrap で折り返す行（Phase 20 の生産リスト行 `.target-row` や Admin の `.item-picker`）は、「はみ出して見えない」溢れバグと「折り返して見える」狭幅対応の区別が必要。`browser_console` で `getBoundingClientRect` を実行してコンテナ幅と子要素の basis 合計を取ると、折り返しが意図的か溢れか判別できる。ブラウザズーム（ctrl+minus）で広げても列自体が固定幅なら行幅は変わらない。

## computer-use での操作注意（計算ページ）

- 「＋ 行を追加」等の小さいボタンは端のクリックが 1px ずれで外れることがある。失敗したら `zoom` で実座標を取り直して中央をクリックする。
- ネイティブ `<select>`（ペア選択プルダウン）はクリックで開き、選択肢を直接クリックすれば `@onchange` が発火する（JS 不要）。稀にドロップダウンが閉じるだけで選択が確定しないことがある。その場合は select を開いて `Up`/`Down` で目的の選択肢へ移動し `Return` で確定するのが確実（Phase 18 で実測）。開いたときハイライトは現在値にあるため、目的の選択肢までは相対移動で済む（例: 先頭が現在値なら `Down` 3 回で 4 番目）。
- ペア選択の選択肢ラベルは `レシピ名 ／ 設備名 N秒・環境` で、同一アイテムに複数レシピがあると完全に同じラベルが並ぶ（炭塊の recipe-carbon03/carbon04 はいずれも「炭塊 ／ 精錬炉 2秒・環境なし」）。区別は「（既定）」マーカーと並び順だけなので、非既定を選ぶ検証では結果の素材行が変わったことで意図どおり選べたかを確認する。
- ペア上書きは数量変更・アイテム差し替え・再計算をまたいで保持される。検証中に切り替えた後で既定経路の結果を撮り直したいときは、select で「既定（自動選択）」を選び直す。
- 開いたドロップダウンは `zoom` で撮影すると選択肢の件数・並び・現在値位置をまとめて確認できる。「選択中値が絞り込み外なら末尾に残す」仕様（Phase 14 仕様、Phase 19 で全選択窓へ展開）の検証は、末尾にある現在値とそのハイライトをこの撮影で目視する。
- カテゴリ select の幅は選択中の表示文字数で変わるため、カテゴリを変更するたびに右隣のアイテム select の x 座標がずれる。カテゴリを変えたら最新スクリーンショットで座標を取り直す（隣の select を狙ったつもりがカテゴリ select を開いてしまう事故が起きやすい）（Phase 19 で実測）。
- `@key` なし行リストでは、行削除で残行に削除行の内部状態（カテゴリ絞り込み等）が引き継がれる退行があり得る（Phase 19 で `target-row` への `@key` 付与で修正済み）。検証は残行と削除行で異なる状態値を仕込んでから削除すると判別力が高い（例: 1 行目＝精錬素材、2 行目＝種 で 1 行目を消し、残行が「種」のままか）。
- `@bind:event="oninput"` の入力（数量・期間の日/時/分）は type で即時反映。`@onchange` の入力（散布機台数）は Tab（blur）を送るまで確定しないので、type 後に Tab を押す。
- **日本語テキストは computer `type` アクションで入力できないことがある**（入力欄に何も入らずプレースホルダのまま）。その場合は入力欄をクリックしてフォーカスした上で、シェルから `DISPLAY=:0 xdotool type --delay 60 "高純度"` を実行すると確実に入る。xdotool も稀に文字を落とす（例:「高純度」→「高度」）ので入力後はスクリーンショットで確認し、誤りなら `ctrl+a` → `Delete` でクリアしてから打ち直す。ASCII・数値・URL は通常の `type` で問題なく入る。
- アドレスバー（オムニボックス）への `ctrl+l` + `type` も稀に効かないことがある。失敗したらアドレスバーを直接クリック → `ctrl+a` → `type` → `Return`、それでも駄目ならブラウザの戻るボタンや `xdotool key ctrl+l` + `xdotool type` を使う。

## 環境

`wmctrl -r :ACTIVE: -b add,maximized_vert,maximized_horz` で最大化。狭幅化は `wmctrl -r :ACTIVE: -b remove,maximized_vert,maximized_horz` → `wmctrl -r :ACTIVE: -e 0,x,y,w,h`。

## ゴールデンパスの期待値（同梱マスタ基準）

`data/master.json`（DataVersion 0.2.6）を対象に、基本導線で期待される表示の基準。マスタ更新で件数が変わったら本表も更新する。

| 確認箇所 | 期待値 |
|---|---|
| 生産リストのアイテム候補 | 7 件（レシピ成果物のみ＝サンドリーフ・サンドリーフの種・芽針・芽針の種・炭塊・結晶外殻・息壌。カテゴリは植物・種・精錬素材・息壌の 4 種。選択中の値が母集団外でも末尾に残す、仕様決定 AS/AT） |
| イベント | GameEvents 0 件のため「有効イベント」節自体が非表示 |
| 採取マップ select | Maps 0 件のため「未選択（採取無制限）」1 件のみ |
| アイコン | Icons 0 件・全エンティティ IconKey=null のため全て「?」プレースホルダ |
| 計算結果（例: 結晶外殻を 60/分で計算） | 素材「源石鉱物 60/分＝採取」・設備「精錬炉 2 台」・消費電力 10・環境節なし |
| 計算結果（例: 炭塊を 2/分で計算） | 種↔作物の正味増循環を経由して充足（仕様決定 AQ・Phase 18）。既定レシピは芽針系 recipe-carbon04（仕様決定 BA・Phase 21）。素材「炭塊 2/分・芽針 2/分・芽針の種 1/分＝いずれもレシピ」「水 1/分＝採取」・設備「栽培機・精錬炉・採種機 各 1 台（実数 0.03）」・消費電力 35。未充足・警告なし |
| 計算結果（例: 息壌を 30/分で計算） | 0.2.5 以降 recipe-xiranite02 に安定環境ペア（天有洪炉 2秒・env-stable）が付き、同梱マスタでも環境節が出る。素材「炭塊 30/分・芽針 30/分・芽針の種 15/分＝レシピ」「水 45/分・不活性ガス 6/分＝採取」・設備「天有洪炉 1 台（実数 1）・栽培機・精錬炉・採種機 各 1 台（実数 0.5）・ガス散布機 1 台」・環境行「安定環境 ガス散布機（自動 1 台まで）・不活性ガス 6 個/分」・消費電力 85。未充足・警告なし |
| Admin「現在の文書」パネル | アイテム 40・設備 6・環境 2・イベント 0・マップ 0・レシピ 8・アイコン 0 件（Icons=0 のとき「ファイル取得」括弧は出ない）。検証実行後は違反 0 |
| フッター | 非公式ファンツールの明記とデータ版 0.2.6 の表示がある |

生産リストのアイテム選択は App・Admin プレビューともカテゴリ select＋アイテム select のネイティブ select ペアに統一されている（仕様決定 AU）。候補は AT の母集団制限で少数のためテキスト検索は設けない。

## 環境付き計算のゴールデンパス（旧同梱マスタ 0.2.0 での実測例）

同梱マスタ 0.2.6 には Environments（安定環境・酸性環境）と散布機（ガス散布機）に加えて、環境ペアを持つ recipe-xiranite02（安定環境）が 1 件登録されており、環境行の基本表示は息壌の計算でそのまま確認できる（前表参照）。本節の値は旧サンプルデータでの実測例であり、固定消費・フロー制限ヒントの表示形式の目安として残す。固定消費を持つペアは同梱マスタにないため、そちらの再検証には既存レシピのペアへ `FixedConsumption` を足すフィクスチャが要る。投入は後述の採取フィクスチャと同じく稼働中 `wwwroot/data/master.json` への注入で行う。

汎用部品（item-part）を数量 10 で計算すると、環境・固定消費・フロー制限ヒントの表示を 1 回の計算で確認できる。App・Admin プレビュー共通の期待値:

- recipe-part の環境付きペア（加工機 CycleTime 3秒・ガス環境・固形燃料 30/分）が既定選択になる（CycleTime 最小規則で 3 < 4 のため環境なし 4 秒ペアより先）。
- 環境行: 「ガス環境 ガス散布機 1 台 ・ 活性ガス 360 個/分」（Phase 26・BQ で自動台数は「環境を要する機械数合計 ÷ CoverableMachines の切上げ」。旧仕様の「稼働ペア数=台数」は廃止）。
- 素材: 原鉱石 20/分・固形燃料 30/分・活性ガス 360/分。供給チップは全て「採取」。
- 設備: 加工機 0.5 台 → 1 台。端数のため調整済ビューが既定になり、「汎用部品 の 原鉱石 を 20/分 に制限」のフロー制限ヒントが出る（推奨流量制限・輸送容量は Phase 16 の仕様決定 AM で個/分表記）。ガス散布機 1 台。
- ペア選択プルダウンはネイティブ `<select>`（App・Admin プレビュー共通。Phase 13 で Admin にも追加）。開いて選択肢を撮れば「固形燃料 30/分（既定）」ラベルを目視確認できる。
- 毎秒トグルは素材の数量・チップのみ /秒 換算する。環境行（個/分）・ペアラベル（/分）・フロー制限ヒント（/分）と警告文（個/分）は表示単位トグルの対象外で変わらない。
- 輸送容量の最小発火例: 結晶外殻 45/分（源石鉱物需要・結晶外殻生産がともにベルト 30 個/分超過で警告 2 件「必要流量 45.00 個/分 が輸送容量（Belt 30 個/分）を超えています。必要レーン数: 2」）。同じ目標で推奨流量制限行「結晶外殻 の 源石鉱物 を 45/分 に制限」も確認できる。
- Admin の計算プレビュー（/preview）は公開版と同じ共有 `CalculatorPanel` を使う（Phase 22 の SharedUi 化）。残る差異は `HeaderHint`・`VersionLabel`・`OnSnapshotRejected` 等のパラメータのみで、Phase 22-2 で EnableFlowGraph 等のトグルは撤去された。`RefSelect` は Admin の編集ページ（アイテム・レシピ等）で継続使用中。

### 散布機台数の変動シナリオ（Phase 26・BQ/BR）

Phase 26（仕様決定 BP〜BR）で自動台数は「環境を要する稼働中ランの機械数合計 ÷ `CoverableMachines` の切上げ」になり、台数の変動はレシピ数ではなく機械数が `CoverableMachines`（同梱=4）の倍数を跨ぐときに起きる。同梱マスタ 0.2.6 ではレシピ数を足さなくても数量の引き上げだけで再現できる:

- 息壌 31/分 → 機械数 1.0 で自動 1 台・不活性ガス 6 個/分。
- 息壌 150/分 → 機械数 5.0 で自動 2 台・不活性ガス 12 個/分。
- 台数を自動値未満（例: 2 → 1）に下げると `台数 × CoverableMachines` が機械数上限になり、超過分が未充足＋ `EnvironmentCoverageExceeded` 警告（「散布機がカバーできる機械数（N 機）を超える M 機分の生産が未充足です」）。0 台は全量未充足で、環境行は「（自動 N 台まで）」のまま残る。
- 保持値が新上限を超える場合は従来どおり自動値へ整合されて即時再計算される（仕様決定 AH）。直接入力の上限超過は「0〜N の整数で入力してください」エラーで再計算されない（N は `RequiredDispenserCount` = 実績＋有効削減機械数から見積もった必要台数）。

旧同梱マスタでの実測記録（レシピ数=台数時代の「自動 2 台まで・活性ガス 720 個/分」→ ペア切替で 1 台）は PR #36 を参照。現仕様で同型の変動を再現するには、環境ペアの機械数合計が CoverableMachines の倍数を跨ぐ数量設計が要る（環境ペアを持つレシピをもう 1 件足すだけでは 2 台目が出ない）。

## 採取機能の手動確認用フィクスチャ（Phase 12/13）

同梱マスタ 0.2.6 にはマップ自体がなく、イベントも未登録である。採取節の要素（上限到達・レシピ展開）を見るには、稼働中 dev server の `src/EndfieldAicWeb.*/wwwroot/data/master.json` に次を追加して即時配信させる（`dotnet run` 再起動は CopyMasterJson が原本で上書きするため不可。再起動なしで反映される）。

```json
// Maps に追加
{"GameEventId": null, "GatherRates": [{"ItemId": "item-originiumOre", "IsUnlimited": true, "RatePerMinute": null}, {"ItemId": "item-cleanWater", "IsUnlimited": true, "RatePerMinute": null}], "Id": "map-check", "Name": "採取確認地", "Description": "採取節の表示確認用（計算プレビュー確認用）", "IconKey": null, "VersionAdded": "0.1.0", "VersionRemoved": null}
// Recipes に追加（採取上限超過分のレシピ展開確認用）
{"GameEventId": null, "Inputs": [{"ItemId": "item-cleanWater", "Quantity": 1}], "Outputs": [{"ItemId": "item-originiumOre", "Quantity": 1, "SortOrder": 0}], "Facilities": [{"FacilityId": "fac-refining", "CycleTime": 4, "EnvironmentId": null, "FixedConsumption": null}], "Id": "recipe-ore", "Name": "鉱物代替レシピ", "Description": "採取上限超過分のレシピ展開確認用（計算プレビュー確認用）", "IconKey": null, "VersionAdded": "0.1.0", "VersionRemoved": null}
```

イベント所属マップの候補外保持を再現するには、上記に加えて GameEvents 節へイベントエンティティを投入し、マップの `GameEventId` をその Id に変える。

`CycleDetected`（循環依存）警告は同梱マスタでは再現できない（Phase 18 以降、実レシピの循環は全てループゲイン < 1 で定常解へ収束する）。ゲイン ≥1 の循環警告を UI で確認するには、Recipes へ `item-cuprium×2 → item-cuprium×1` のような自己循環レシピ（出力は他レシピと衝突しない、既存レシピのないアイテムを選ぶ）を同じ手順で注入してハードリロードする。

検証後は原本へ戻す。glob が App と Admin の 2 ディレクトリに展開されるため、`for d in src/EndfieldAicWeb.*/wwwroot/data/; do cp data/master.json "$d"; done` とループで両アプリ分を戻す（`cp 対象 .../data/` の形は最後の 1 件にしか効かない）。

フィクスチャ適用時の期待値（App/Admin 共通）:

- イベント所属マップ（GameEventId 設定済みのマップ）は所属イベントを有効にしたときだけ候補に出る。選択中にイベントを外すと「〈名〉（イベント無効）」で候補外保持され `GatherMapUnavailable` 警告＋採取節消失になる。
- 採取素材の利用可能レート上書きを既定値より下げると超過分が代替レシピへ展開され採取行に「上限到達」が付く。非数値・負値は「〈名〉 の利用可能レートは 0 以上の数値で入力してください。」で再計算されず前回結果が残る。
- 採取レート・散布機台数の入力値は数量変更・ペア切替・マップ切替をまたいで保持される。
- FixedConsumption は施設の合計台数（ceil）請求。例: 固定消費 30/分を持つ設備が 1.17 台必要なら 2 台分の 60/分が請求される。直感より大きい値が正しいことがある。
- 単位切替は素材・チップ・未充足・余剰のみ換算。採取節・環境行・消費電力・ペアラベル・フロー制限ヒント・警告文は 個/分・/分 のまま換算対象外。
- エラー表示が出ると要素が下にずれる。`@onchange` 入力のクリック座標はエラー行の有無で変わるので、エラー中は最新スクリーンショットで位置を取り直す。

## 旧フィールド名 JSON の拒否確認（Phase 9 以降）

Admin ホームのファイル選択で、旧名（IsBaseMaterial / ConsumeRatePerSecond / RatePerSecond / 基礎素材）に戻した JSON を読み込むと「JSON の解析に失敗しました: … missing required properties … IsGatherable」の違反で拒否される。旧文書は保持される。

## Admin ツール固有の操作注意（Phase 6 時点）

- ネイティブ `confirm` ダイアログ（削除・ダーティ時の読み込み直し）は **Return=OK / Escape=キャンセル**。マウスクリックはボタンが小さく外れることがあるのでキー操作が確実。
- ファイル読み込み（`<input type="file">` → GTK ファイルダイアログ）は **`ctrl+l` でロケーションバーが出るので絶対パスを type して Return**。フォルダを手繰るより速く確実。
- URL 読み込みの既定 `https://endfield-aic.pages.dev/data/master.json` は、本番 `_headers` で `/data/*` に `Access-Control-Allow-Origin: *` が付いているため **localhost の dev server からでも成功する**（2026-09 時点で実測）。「ローカルでは CORS 失敗が仕様」という古い想定は成立しないので、失敗したらむしろ退行を疑う。
- `dotnet run` を rebuild・再起動した直後はブラウザ側に古い WASM/文書状態が残ることがある。**`ctrl+shift+r`（ハードリロード）してから測定開始**すること。アプリ内状態は WASM メモリ上だけなのでリロードで「未読み込み」に戻る。
- 行削除・エンティティ削除で一覧の行 y 座標が繰り上がる。**座標ではなく選択後にエディタの Id フィールド表示で対象を確認**してから編集する（誤って別エンティティを編集する事故を防げる）。
- エクスポート成功時は Chrome 右上のダウンロードバブルに `master.json ... Done` が出る（DL 実証のスクリーンショットに使える）。連続 DL すると `master (1).json` 等にリネームされる。`~/Downloads/` に実ファイルが残るので内容検証はシェルで可能。
- エクスポート失敗で違反一覧が出るとエクスポートパネル全体が約 20px 下にずれる。違反表示中は事前の座標メモでボタンを押さず、スクリーンショットか zoom で現位置を取り直してからクリックする（「提案値を使う」「エクスポート」ボタンで 1px 外しやすい）。
- DataVersion 空欄で zip エクスポートすると「DataVersion は必須です」違反で拒否されダウンロードも起きない（仕様）。検証パネルの違反表示で確認できる。
- datalist（アイテム編集のカテゴリ欄など `list="..."` 付き input）の全候補確認は、**フィールドを空にしてからクリック（または欄端の▼）する**。値が入った状態で開くとその値で前方一致絞り込みされる。ドロップダウン候補のクリックで値が入り、blur で @onchange が確定する。新規値が保存されたかの確認は「入力→Tab 確定→再度空にして開き、その文字列が候補に出る」が確実。
- datalist の値変更は クリック → `ctrl+a` → `xdotool type` → Tab（@onchange 確定）の手順が確実。ドロップダウンはクリック位置次第で選択肢の上にかぶさることがある。
- 行選択肢が動的に再構築されるネイティブ `<select>`（Phase 14 のアイテム絞り込み等）は、選択肢の再構築でブラウザ側 selectedIndex がずれて**表示値がモデルと乖離**し得る（別アイテムや未選択表示に化ける）。選択肢の件数・並び・現在値位置は `read_dom` の `<option>` 列挙と `selectedindex` で確認し、モデルの実値は検証実行や再描画後の表示で確かめる。表示とモデルの乖離はバグ報告対象（Phase 14 では `@key` による select 再生成で修正済み）。
- 絞り込みに使ったカテゴリが消えるケース（最後の 1 件の削除や別カテゴリへの変更）では、絞り込み select が空値へ自動復帰して一覧が空転しないことを確認する（Phase 19 の `ListPane.OnParametersSet` 修正に対応）。

## スクロール挙動（scrollIntoView）の検証（Phase 14 以降）

- エンティティページの行選択・新規作成後に編集パネルへ `scrollIntoView({behavior:"smooth", block:"start"})` が発火する（`js/ui.js` の `scrollElementIntoView` + ページ側 `OnAfterRenderAsync` フラグ方式）。発火は選択・新規作成の両ハンドラ共通。
- **100% ズームではページがビューポートに収まりスクロール不可**（スクロール範囲 0 のため scrollIntoView は実質 no-op）。検証には `ctrl+equal` で 200〜300% にズームしてコンテンツをビューポートより高くし、ページ最下部までスクロールしてから行をクリックする。パネル先頭へスムーズスクロールで戻ることを確認する。
- `block:"start"` のため、パネルが表示領域内でも「パネル上端がビューポート上端に揃う」よう毎回スクロールが発火し得る（ナビバーが隠れる位置まで動くのは仕様）。パネルがビューポートより高い場合は最下部の行選択で明確に戻る。
- 途中で新しい JS/razor 変更が push されたときは `dotnet run` 再起動（rebuild）＋ブラウザ `ctrl+shift+r` ハードリロードが必須。`js/ui.js` のような新規 wwwroot アセットは古い index.html のままだと読み込まれない。

## ナビバーのリンク座標

- Admin のナビリンク（データ管理/アイテム/設備/環境/イベント/マップ/レシピ/計算プレビュー）はピル型で幅が狭く間隔も約 17〜25px と密。`zoom` で座標を取り直して中央をクリックしないと隣リンクへ誤爆する。クリック後は必ずアドレスバーのパスで到達先を確認する。

## アイコン関連の検証（Phase 7 以降）

- 読み込み成功の判定には「現在の文書」パネルの `アイコン: N 件（ファイル取得 X/Y）` を使う。X/Y が一致しない場合はアイコンファイル取得に失敗している（HTTP・sha256 不一致など）。X はマニフェストの Sha256/Bytes に一致するファイルだけを数えるため、`.json` 読み込みで温存したストアが新マニフェストと不一致なら X は下がる。
- 画像取り込み（IconEditor「画像を選択」）の e2e 検証は、シェルで非正方形 PNG を生成（Python で 200×100 程度の RGB PNG を /tmp に書ける）→ file picker（ctrl+l + 絶対パス）→ プレビューと IconKey 自動補完を確認 → 再度 zip エクスポートし、zip 内 `data/icons/<Key>.png` の IHDR が 128×128 であることをシェルで確認、が確実。
- IconKey の直接入力は `@onchange` なので type 後に **Tab** で確定させる。確定しないとプレビューが更新されない。
- 「クリア」ボタンは IconKey 未設定時に disabled になる — disabled 状態自体も検証ポイントにできる。
- エクスポートされた zip の検証は `unzip -o ~/Downloads/master-export*.zip -d <dir>` + Python で sha256/Bytes を `data/master.json` の Icons マニフェストと照合する。連続 DL すると `master-export (1).zip` 等にリネームされるので glob で拾う。
- file input の `accept=".json,.zip"` 経路は両方テスト可能。`.json` 単体読み込みはアイコンストアを温存する（前回読み込みの zip 由来アイコンが残る）ため、X/Y が前回値を引き継ぐ表示になるのは仕様。
- `Icons[].File` は `icons/<Key>.png` 形式がロード時の構造検証で強制される。再照合（ストア温存分を新マニフェストの Sha256/Bytes で数え直す処理）の不一致系をテストするために File を改名すると、読み込み自体が違反で失敗して再照合まで辿り着かない。読み込みは通るが再照合だけ落ちる JSON を作るには、File は正しい形のまま `Bytes`/`Sha256` を実体とずらす（例: Bytes を +1）。
- 同梱マスタ 0.2.6 は全エンティティが IconKey 未設定のため、一覧は全件「?」プレースホルダになる。レシピは主出力アイテムの IconKey へフォールバックする（例: recipe-origocrust01 は主出力 item-origocrust も未設定のため「?」のまま）。フォールバック対象があるかはエディタのヒント「主出力アイテムのアイコンが使われます。」の有無で判定できる。
- 提案キー衝突（`icon-<Id>` の -n 連番）は種別またぎで作れる: 別エンティティの IconKey 欄に占有したいキー（例: icon-fac-dispenser）を手入力+Tab 確定 → 対象エンティティに画像登録 → icon-fac-dispenser-2 が補完される。後始末として占有側を「クリア」で null に戻さないと、エクスポートが未登録キー違反で止まる。
## エクスポート物を App 側で e2e 確認する手順（Phase 8 以降）

- `src/*/wwwroot/data/` は `CopyMasterJson` MSBuild ターゲットがリポジトリルート `data/` からコピーする **gitignore 済みのビルド生成物**。追跡対象の `data/` に触れずに App へ新マスタを食わせられる。
- `dotnet run`（Blazor Dev Server）稼働中に `wwwroot/data/` へ追加・上書きしたファイルは**再起動なしで配信される**（新規ファイルも curl で 200 確認済み）。手順:
  1. Admin で master-export.zip を出力し `unzip` する
  2. `cp 展開dir/data/master.json src/EndfieldAicWeb.App/wwwroot/data/` と `cp 展開dir/data/icons/*.png src/EndfieldAicWeb.App/wwwroot/data/icons/`
  3. App を ctrl+shift+r でハードリロード（fetch は `data/master.json` 相対パス、IconCatalog が `data/icons/<Key>.png` を個別取得）
  4. 検証後は `cp data/master.json ...`＋追加アイコン削除で元に戻す（次回ビルド時にも CopyMasterJson が原本で上書きする）
- **注意**: `dotnet run` を再起動すると CopyMasterJson が wwwroot/data を原本で上書きするため、コピーはサーバー稼働中に行う。

## APNG アニメーションの検証（Phase 8）

- Chrome は `<img>` でも `file://` 直開きでも APNG を無限ループ再生する。エクスポート物の `data/icons/*.png` を `file:///tmp/.../icon-xxx.png` で直接開けば再生を目視できる。
- 静止スクショでアニメを証明するには **0.5〜1 秒間隔のバースト撮影**を取り、同じ領域のフレーム差を比較する。20px の `.icon-slot` ではフレーム差が小さいので、`zoom` より通常 `screenshot` を連発して後で Python（PIL）で領域比較するのが確実。
- **保存済みスクリーンショットは VM の実画面サイズ（`xrandr` で確認。本 VM は例として 3200×2400）で保存され、computer ツールの座標系（1024×768）とは異なる**。PIL で領域解析する際は `x*<実幅>/1024, y*<実高>/768` に換算する。
- APNG 構造はシェルで `b'acTL' in open(f,'rb').read()`、`n_frames`・`im.info['duration']`（PIL）で遅延 ms が読める。65535ms 超の遅延は UPNG.encode の 16bit 制約で**同一フレーム繰り返しに分割**される（例: 70000ms → [65535, 4465]）のが仕様。

## 動作確認済みの補足

- GTK ファイルダイアログは前回開いたディレクトリを記憶する。`/tmp/icontest` 等を一度開けば以後ファイル行クリック＋Open で選べる。
