---
name: testing-blazor-apps
description: Endfield-AIC-Web の Blazor WASM アプリ（App/Admin）をローカルまたは公開 URL でブラウザ確認する手順
---

# Blazor WASM アプリの起動・ブラウザ確認手順

前提: Phase 1 で導入する `src/EndfieldAicWeb.App`・`src/EndfieldAicWeb.Admin`（Blazor WASM スタンドアロン）が存在すること。これらのプロジェクトがない段階では起動コマンドは実行できない。

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
- アドレスバー直打ちでルートに戻るとき、Chrome のオートコンプリートが履歴の `/counter` 等を補完して別パスへ飛ぶことがある。確実に戻るにはトレーリングスラッシュ付きで `127.0.0.1:5180/` と入れるか、アプリ内の「ホーム」ナビリンクをクリックする。

## レスポンシブ確認

- 計算 UI（`Pages/Home.razor`）の 2 カラム `.layout` は **780px 未満**で 1 カラム化（`wwwroot/css/app.css` の `@media (max-width: 780px)`）。780px は Chrome 最小幅を上回るため、`wmctrl -e` のウィンドウリサイズだけで検証できる。
- Chrome の最小ウィンドウ幅は約 500px で、`wmctrl -e` でのリサイズは 530px 程度までしか縮められない。真の 375px 幅が必要ならウィンドウリサイズではなく CDP のデバイスエミュレーションを使う。

## computer-use での操作注意（計算ページ）

- 「＋ 行を追加」等の小さいボタンは端のクリックが 1px ずれで外れることがある。失敗したら `zoom` で実座標を取り直して中央をクリックする。
- ネイティブ `<select>`（ペア選択プルダウン）はクリックで開き、選択肢を直接クリックすれば `@onchange` が発火する（JS 不要）。
- `@bind:event="oninput"` の入力（数量・期間の日/時/分）は type で即時反映。`@onchange` の入力（散布機台数）は Tab（blur）を送るまで確定しないので、type 後に Tab を押す。
- コンボボックスは `@onmousedown` で項目選択。候補リストは `@onfocus`/`@onblur` で開閉するため、入力クリック → type → 候補をクリックの順で安定する。
- **日本語テキストは computer `type` アクションで入力できないことがある**（入力欄に何も入らずプレースホルダのまま）。その場合は入力欄をクリックしてフォーカスした上で、シェルから `DISPLAY=:0 xdotool type --delay 60 "高純度"` を実行すると確実に入る。xdotool も稀に文字を落とす（例:「高純度」→「高度」）ので入力後はスクリーンショットで確認し、誤りなら `ctrl+a` → `Delete` でクリアしてから打ち直す。ASCII・数値・URL は通常の `type` で問題なく入る。
- アドレスバー（オムニボックス）への `ctrl+l` + `type` も稀に効かないことがある。失敗したらアドレスバーを直接クリック → `ctrl+a` → `type` → `Return`、それでも駄目ならブラウザの戻るボタンや `xdotool key ctrl+l` + `xdotool type` を使う。

## 環境

`wmctrl -r :ACTIVE: -b add,maximized_vert,maximized_horz` で最大化。狭幅化は `wmctrl -r :ACTIVE: -b remove,maximized_vert,maximized_horz` → `wmctrl -r :ACTIVE: -e 0,x,y,w,h`。

## ゴールデンパスの期待値（同梱マスタ基準）

`data/master.json`（DataVersion 0.2.0）を対象に、基本導線で期待される表示の基準。マスタ更新で件数が変わったら本表も更新する。

| 確認箇所 | 期待値 |
|---|---|
| 生産リストのアイテム候補 | 6 件（全 7 アイテムから仮想アイテム `item-power` を除く） |
| イベント | `ev-first` のみ。期間外のため期間外イベント側（折りたたみ）で、既定チェックは外れている |
| 計算結果（例: アイテム選択＋数量入力後） | 素材・設備・消費電力の各節が出る（既定は個/分表示） |
| Admin「現在の文書」パネル | アイコン: 5 件（ファイル取得 5/5）。検証実行後は違反 0 |
| フッター | 非公式ファンツールの明記がある |

## 環境付き計算のゴールデンパス（Phase 9 以降）

汎用部品（item-part）を数量 10 で計算すると、環境・固定消費・フロー制限ヒントの表示を 1 回の計算で確認できる。App・Admin プレビュー共通の期待値:

- recipe-part の環境付きペア（加工機 CycleTime 3秒・ガス環境・固形燃料 30/分）が既定選択になる（CycleTime 最小規則で 3 < 4 のため環境なし 4 秒ペアより先）。
- 環境行: 「ガス環境 ガス散布機 1 台 ・ 活性ガス 360 個/分」（散布機台数は稼働ペア数=1 が自動値）。
- 素材: 原鉱石 20/分・固形燃料 30/分・活性ガス 360/分。供給チップは全て「採取」。
- 設備: 加工機 0.5 台 → 1 台。端数のため調整済ビューが既定になり、「汎用部品 の 原鉱石 を 0.333/秒 に制限」のフロー制限ヒントが出る（輸送容量系と同じく /秒 表記は単位統一の対象外）。ガス散布機 1 台。
- ペア選択プルダウンはネイティブ `<select>`（App・Admin プレビュー共通。Phase 13 で Admin にも追加）。開いて選択肢を撮れば「固形燃料 30/分（既定）」ラベルを目視確認できる。
- 毎秒トグルは素材の数量・チップのみ /秒 換算する。環境行（個/分）とペアラベル（/分）は表示単位トグルの対象外で変わらない。
- Admin の計算プレビュー（/preview）は Phase 13 で公開版へ追従し、ペア選択・散布機台数入力・単位トグル・期間入力・採取マップ/レート入力がある。残る意図的差異はアイテム選択が検索コンボではなくネイティブ `<select>`（RefSelect）な点のみ。

## 採取機能の手動確認用フィクスチャ（Phase 12/13）

同梱マスタにはイベント所属マップと「採取素材を産出するレシピ」がない。採取節の全要素（候補外保持・上限到達・レシピ展開）を見るには、稼働中 dev server の `src/EndfieldAicWeb.*/wwwroot/data/master.json` に次を追加して即時配信させる（`dotnet run` 再起動は CopyMasterJson が正本で上書きするため不可。再起動なしで反映）:

```json
// Maps に追加
{"GameEventId": "ev-first", "GatherRates": [{"ItemId": "item-ore", "IsUnlimited": true, "RatePerMinute": null}], "Id": "map-event", "Name": "イベント採取地", "Description": "イベント期間限定の採取地（計算プレビュー確認用）", "IconKey": null, "VersionAdded": "0.1.0", "VersionRemoved": null}
// Recipes に追加
{"GameEventId": null, "Inputs": [{"ItemId": "item-fuel", "Quantity": 1}], "Outputs": [{"ItemId": "item-ore", "Quantity": 1, "SortOrder": 0}], "Facilities": [{"FacilityId": "fac-assembler", "CycleTime": 4, "EnvironmentId": null, "FixedConsumption": null}], "Id": "recipe-ore", "Name": "原鉱石採掘", "Description": "採取上限超過分のレシピ展開確認用（計算プレビュー確認用）", "IconKey": null, "VersionAdded": "0.1.0", "VersionRemoved": null}
```

検証後は `cp data/master.json src/EndfieldAicWeb.*/wwwroot/data/` で正本へ戻す。

フィクスチャ適用時の期待値（App/Admin 共通）:

- `map-event` は所属イベント `ev-first` を有効にしたときだけ候補に出る。選択中にイベントを外すと「〈名〉（イベント無効）」で候補外保持され `GatherMapUnavailable` 警告＋採取節消失になる。
- 採取素材の利用可能レート上書きを既定値より下げると超過分が代替レシピへ展開され採取行に「上限到達」が付く。非数値・負値は「〈名〉 の利用可能レートは 0 以上の数値で入力してください。」で再計算されず前回結果が残る。
- 採取レート・散布機台数の入力値は数量変更・ペア切替・マップ切替をまたいで保持される。
- FixedConsumption は施設の合計台数（ceil）請求。例: 加工機 1.17 台 → 2 台で固定消費 30/分 × 2 = 60/分。直感より大きい値が正しいことがある。
- 単位切替は素材・チップ・未充足・余剰のみ換算。採取節・環境行・消費電力・ペアラベル・フロー制限ヒントは 個/分・/秒 のまま換算対象外。
- エラー表示が出ると要素が下にずれる。`@onchange` 入力のクリック座標はエラー行の有無で変わるので、エラー中は最新スクリーンショットで位置を取り直す。

## 旧フィールド名 JSON の拒否確認（Phase 9 以降）

Admin ホームのファイル選択で、旧名（IsBaseMaterial / ConsumeRatePerSecond / RatePerSecond / 基礎素材）に戻した JSON を読み込むと「JSON の解析に失敗しました: … missing required properties … IsGatherable」の違反で拒否される。旧文書は保持される。

## Admin ツール固有の操作注意（Phase 6 時点）

- ネイティブ `confirm` ダイアログ（削除・ダーティ時の読み込み直し）は **Return=OK / Escape=キャンセル**。マウスクリックはボタンが小さく外れることがあるのでキー操作が確実。
- ファイル読み込み（`<input type="file">` → GTK ファイルダイアログ）は **`ctrl+l` でロケーションバーが出るので絶対パスを type して Return**。フォルダを手繰るより速く確実。
- URL 読み込みの既定 `https://endfield-aic.pages.dev/data/master.json` は、本番 `_headers` で `/data/*` に `Access-Control-Allow-Origin: *` が付いているため **localhost の dev server からでも成功する**（2026-09 時点で実測）。「ローカルでは CORS 失敗が仕様」という古い想定は成立しないので、失敗したらむしろ退行を疑う。
- `dotnet run` を rebuild・再起動した直後はブラウザ側に古い WASM/文書状態が残ることがある。**`ctrl+shift+r`（ハードリロード）してから測定開始**すること。アプリ内状態は WASM メモリ上だけなのでリロードで「未読み込み」に戻る。
- 行削除・エンティティ削除で左ペインの行 y 座標が繰り上がる。**座標ではなく選択後にエディタの Id フィールド表示で対象を確認**してから編集する（誤って別エンティティを編集する事故を防げる）。
- エクスポート成功時は Chrome 右上のダウンロードバブルに `master.json ... Done` が出る（DL 実証のスクリーンショットに使える）。連続 DL すると `master (1).json` 等にリネームされる。`~/Downloads/` に実ファイルが残るので内容検証はシェルで可能。
- エクスポート失敗で違反一覧が出るとエクスポートパネル全体が約 20px 下にずれる。違反表示中は事前の座標メモでボタンを押さず、スクリーンショットか zoom で現位置を取り直してからクリックする（「提案値を使う」「エクスポート」ボタンで 1px 外しやすい）。
- DataVersion 空欄で zip エクスポートすると「DataVersion は必須です」違反で拒否されダウンロードも起きない（仕様）。検証パネルの違反表示で確認できる。

## アイコン関連の検証（Phase 7 以降）

- 読み込み成功の判定には「現在の文書」パネルの `アイコン: N 件（ファイル取得 X/Y）` を使う。X/Y が一致しない場合はアイコンファイル取得に失敗している（HTTP・sha256 不一致など）。X はマニフェストの Sha256/Bytes に一致するファイルだけを数えるため、`.json` 読み込みで温存したストアが新マニフェストと不一致なら X は下がる。
- 画像取り込み（IconEditor「画像を選択」）の e2e 検証は、シェルで非正方形 PNG を生成（Python で 200×100 程度の RGB PNG を /tmp に書ける）→ file picker（ctrl+l + 絶対パス）→ プレビューと IconKey 自動補完を確認 → 再度 zip エクスポートし、zip 内 `data/icons/<Key>.png` の IHDR が 128×128 であることをシェルで確認、が確実。
- IconKey の直接入力は `@onchange` なので type 後に **Tab** で確定させる。確定しないとプレビューが更新されない。
- 「クリア」ボタンは IconKey 未設定時に disabled になる — disabled 状態自体も検証ポイントにできる。
- エクスポートされた zip の検証は `unzip -o ~/Downloads/master-export*.zip -d <dir>` + Python で sha256/Bytes を `data/master.json` の Icons マニフェストと照合する。連続 DL すると `master-export (1).zip` 等にリネームされるので glob で拾う。
- file input の `accept=".json,.zip"` 経路は両方テスト可能。`.json` 単体読み込みはアイコンストアを温存する（前回読み込みの zip 由来アイコンが残る）ため、X/Y が前回値を引き継ぐ表示になるのは仕様。
- `Icons[].File` は `icons/<Key>.png` 形式がロード時の構造検証で強制される。再照合（ストア温存分を新マニフェストの Sha256/Bytes で数え直す処理）の不一致系をテストするために File を改名すると、読み込み自体が違反で失敗して再照合まで辿り着かない。読み込みは通るが再照合だけ落ちる JSON を作るには、File は正しい形のまま `Bytes`/`Sha256` を実体とずらす（例: Bytes を +1）。
- 同梱マスタには IconKey 未設定のエンティティがいるためフォールバック検証に使える（例: recipe-part は主出力 item-part の icon-item-part にフォールバック、recipe-part-hp は主出力 item-part-hp が未設定なので「?」のまま）。エディタでヒント「主出力アイテムのアイコンが使われます。」の有無が判定材料。
- 提案キー衝突（`icon-<Id>` の -n 連番）は種別またぎで作れる: 別エンティティの IconKey 欄に占有したいキー（例: icon-fac-dispenser）を手入力+Tab 確定 → 対象エンティティに画像登録 → icon-fac-dispenser-2 が補完される。後始末として占有側を「クリア」で null に戻さないと、エクスポートが未登録キー違反で止まる。
## エクスポート物を App 側で e2e 確認する手順（Phase 8 以降）

- `src/*/wwwroot/data/` は `CopyMasterJson` MSBuild ターゲットがリポジトリルート `data/` からコピーする **gitignore 済みのビルド生成物**。追跡対象の `data/` に触れずに App へ新マスタを食わせられる。
- `dotnet run`（Blazor Dev Server）稼働中に `wwwroot/data/` へ追加・上書きしたファイルは**再起動なしで配信される**（新規ファイルも curl で 200 確認済み）。手順:
  1. Admin で master-export.zip を出力し `unzip` する
  2. `cp 展開dir/data/master.json src/EndfieldAicWeb.App/wwwroot/data/` と `cp 展開dir/data/icons/*.png src/EndfieldAicWeb.App/wwwroot/data/icons/`
  3. App を ctrl+shift+r でハードリロード（fetch は `data/master.json` 相対パス、IconCatalog が `data/icons/<Key>.png` を個別取得）
  4. 検証後は `cp data/master.json ...`＋追加アイコン削除で元に戻す（次回ビルド時にも CopyMasterJson が正本で上書きする）
- **注意**: `dotnet run` を再起動すると CopyMasterJson が wwwroot/data を正本で上書きするため、コピーはサーバー稼働中に行う。

## APNG アニメーションの検証（Phase 8）

- Chrome は `<img>` でも `file://` 直開きでも APNG を無限ループ再生する。エクスポート物の `data/icons/*.png` を `file:///tmp/.../icon-xxx.png` で直接開けば再生を目視できる。
- 静止スクショでアニメを証明するには **0.5〜1 秒間隔のバースト撮影**を取り、同じ領域のフレーム差を比較する。20px の `.icon-slot` ではフレーム差が小さいので、`zoom` より通常 `screenshot` を連発して後で Python（PIL）で領域比較するのが確実。
- **保存済みスクリーンショットは実解像度（1600×1200）で、computer ツールの座標系（1024×768）と違う**。PIL で領域解析する際は `x*1600/1024, y*1200/768` に換算する。
- APNG 構造はシェルで `b'acTL' in open(f,'rb').read()`、`n_frames`・`im.info['duration']`（PIL）で遅延 ms が読める。65535ms 超の遅延は UPNG.encode の 16bit 制約で**同一フレーム繰り返しに分割**される（例: 70000ms → [65535, 4465]）のが仕様。

## 動作確認済みの補足

- GTK ファイルダイアログは前回開いたディレクトリを記憶する。`/tmp/icontest` 等を一度開けば以後ファイル行クリック＋Open で選べる。
