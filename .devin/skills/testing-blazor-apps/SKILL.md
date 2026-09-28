---
name: testing-blazor-apps
description: Endfield-AIC-Web の Blazor WASM アプリ（App/Admin）をローカルで起動してブラウザ確認する手順
---

# Blazor WASM アプリの起動・ブラウザ確認手順

## 起動

.NET 8 SDK は `~/dotnet` にある。各シェルで `export PATH="$HOME/dotnet:$PATH"` してから、App と Admin を別ポートで起動する。

```sh
dotnet run --project src/EndfieldAicWeb.App --no-launch-profile --urls http://127.0.0.1:5180
dotnet run --project src/EndfieldAicWeb.Admin --no-launch-profile --urls http://127.0.0.1:5181
```

- `--no-launch-profile` を付けないと launchSettings の `launchBrowser: true` でブラウザが自動起動し、ポートも launchSettings 側（5280 等）に吸われる。ポート指定を確実に効かせるには両方付ける。
- Blazor WASM スタンドアロンの `dotnet run` は Blazor Dev Server 経由で静的配信される。SPA フォールバックがあるため `/counter` 等の任意パスも HTTP 200 で `index.html` が返る。「404 確認」はサーバのステータスではなく、WASM ルーターの NotFound ビュー（`App.razor` の `<NotFound>`）が画面上に出ることで確認する。
- `_framework/blazor.webassembly.js`（約 60KB）と `_framework/dotnet.native.wasm`（約 2.9MB、.NET 8 では `dotnet.wasm` ではなくこの名前）が 200 で配信されることを curl で事前確認すると良い。

## ブラウザ確認の注意

- Chrome は `/home/ubuntu/.local/bin/google-chrome`（Chrome for Testing）。`DISPLAY=:0`。
- WASM 読み込みには数秒かかる。`#app` 内のローディングスピナーが消えて本文が描画されるまで待つ。失敗時は画面下部に `#blazor-error-ui` のエラーバーが出る。
- アドレスバー直打ちでルートに戻るとき、Chrome のオートコンプリートが履歴の `/counter` 等を補完して別パスへ飛ぶことがある。確実に戻るにはトレーリングスラッシュ付きで `127.0.0.1:5180/` と入れるか、アプリ内の「ホーム」ナビリンクをクリックする。

## レスポンシブ確認

- ブレークポイントは 641px（`Layout/NavMenu.razor.css`・`MainLayout.razor.css` の `@media (min-width: 641px)`）。これ未満でサイドバーが上部バー＋ハンバーガーに折りたたまれる。
- Chrome の最小ウィンドウ幅は約 500px で、`wmctrl -e` でのリサイズは 530px 程度までしか縮められない。それでも 641px 未満なので折りたたみ経路は検証できる。真の 375px 幅が必要ならウィンドウリサイズではなく CDP のデバイスエミュレーションを使う。

## 環境

`wmctrl -r :ACTIVE: -b add,maximized_vert,maximized_horz` で最大化。狭幅化は `wmctrl -r :ACTIVE: -b remove,maximized_vert,maximized_horz` → `wmctrl -r :ACTIVE: -e 0,x,y,w,h`。
