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
