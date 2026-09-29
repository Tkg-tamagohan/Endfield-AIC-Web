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

## Admin ツール固有の操作注意（Phase 6 時点）

- ネイティブ `confirm` ダイアログ（削除・ダーティ時の読み込み直し）は **Return=OK / Escape=キャンセル**。マウスクリックはボタンが小さく外れることがあるのでキー操作が確実。
- ファイル読み込み（`<input type="file">` → GTK ファイルダイアログ）は **`ctrl+l` でロケーションバーが出るので絶対パスを type して Return**。フォルダを手繰るより速く確実。
- URL 読み込みの既定 `https://endfield-aic.pages.dev/data/master.json` は、本番 `_headers` で `/data/*` に `Access-Control-Allow-Origin: *` が付いているため **localhost の dev server からでも成功する**（2026-09 時点で実測）。「ローカルでは CORS 失敗が仕様」という古い想定は成立しないので、失敗したらむしろ退行を疑う。
- `dotnet run` を rebuild・再起動した直後はブラウザ側に古い WASM/文書状態が残ることがある。**`ctrl+shift+r`（ハードリロード）してから測定開始**すること。アプリ内状態は WASM メモリ上だけなのでリロードで「未読み込み」に戻る。
- 行削除・エンティティ削除で左ペインの行 y 座標が繰り上がる。**座標ではなく選択後にエディタの Id フィールド表示で対象を確認**してから編集する（誤って別エンティティを編集する事故を防げる）。
- エクスポート成功時は Chrome 右上のダウンロードバブルに `master.json ... Done` が出る（DL 実証のスクリーンショットに使える）。連続 DL すると `master (1).json` 等にリネームされる。`~/Downloads/` に実ファイルが残るので内容検証はシェルで可能。

## アイコン関連の検証（Phase 7 以降）

- 読み込み成功の判定には「現在の文書」パネルの `アイコン: N 件（ファイル取得 X/Y）` を使う。X/Y が一致しない場合はアイコンファイル取得に失敗している（HTTP・sha256 不一致など）。X はマニフェストの Sha256/Bytes に一致するファイルだけを数えるため、`.json` 読み込みで温存したストアが新マニフェストと不一致なら X は下がる。
- 画像取り込み（IconEditor「画像を選択」）の e2e 検証は、シェルで非正方形 PNG を生成（Python で 200×100 程度の RGB PNG を /tmp に書ける）→ file picker（ctrl+l + 絶対パス）→ プレビューと IconKey 自動補完を確認 → 再度 zip エクスポートし、zip 内 `data/icons/<Key>.png` の IHDR が 128×128 であることをシェルで確認、が確実。
- IconKey の直接入力は `@onchange` なので type 後に **Tab** で確定させる。確定しないとプレビューが更新されない。
- 「クリア」ボタンは IconKey 未設定時に disabled になる — disabled 状態自体も検証ポイントにできる。
- エクスポートされた zip の検証は `unzip -o ~/Downloads/master-export*.zip -d <dir>` + Python で sha256/Bytes を `data/master.json` の Icons マニフェストと照合する。連続 DL すると `master-export (1).zip` 等にリネームされるので glob で拾う。
- file input の `accept=".json,.zip"` 経路は両方テスト可能。`.json` 単体読み込みはアイコンストアを温存する（前回読み込みの zip 由来アイコンが残る）ため、X/Y が前回値を引き継ぐ表示になるのは仕様。
