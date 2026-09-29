# Phase 5 実装詳細計画

**対象フェーズ**: Phase 5（Cloudflare Pages デプロイ: 公開アプリの配信）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)

> 本書は Phase 5 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### 作るもの

- Cloudflare Pages プロジェクト（計算アプリの配信用。`pages.dev` サブドメイン）
- 公開成果物のパイプライン設定
  - `.github/workflows/deploy-pages.yml`（main への push と手動トリガでデプロイ）
  - `src/EndfieldAicWeb.App/wwwroot/_headers`（配信ヘッダ）
  - `EndfieldAicWeb.App.csproj` の `data/icons/` 同梱ターゲット（Phase 7 の実画像の受け皿）
- GitHub リポジトリの Actions シークレット `CLOUDFLARE_API_TOKEN`・`CLOUDFLARE_ACCOUNT_ID`

### 作らないもの

- 管理ツールの Pages プロジェクトと Cloudflare Access 設定（Phase 6）
- アイコン実画像と取り込みパイプライン（Phase 7）。`data/icons/` は現状空であり、同梱経路だけを用意する
- カスタムドメイン、PR プレビューデプロイ。要件になく、必要になった時点で追加する
- SPA フォールバック用の `_redirects`（§2 の理由により標準挙動へ委ねる）

## 2. デプロイ方式の確定（Phase 開始時の決定事項）

implementation-plan.md が提示する 2 方式（Pages の Git 連携、GitHub Actions からの wrangler デプロイ）のうち、**GitHub Actions から `cloudflare/wrangler-action` で直接アップロードする方式**を採る。

根拠は次のとおり。

- Git 連携は Cloudflare 側のビルド環境で `dotnet publish` を実行する必要があり、Pages の標準ビルドイメージに .NET SDK は含まれない。
  SDK の導入手順をビルドコマンドに組み込む必要が生じ、既存 CI（`.github/workflows/ci.yml`）と別系統のビルド手順を保守することになる。
- Git 連携の有効化は Cloudflare ダッシュボード上での GitHub OAuth 認可を要し、API トークンだけでは完了できない。
- 一方 Actions 方式は、既存 CI と同じ ubuntu ランナー・同じコマンド列で発行成果物を作れる。
  `CLOUDFLARE_API_TOKEN` は Pages デプロイ用途でプロビジョン済みの org シークレットであり、そのままリポジトリの Actions シークレットへ登録すればよい。
- ワークフロー・ヘッダ・リダイレクトの全構成がリポジトリ内のコードとして残り、変更が PR でレビューできる。

## 3. Pages プロジェクトと配信挙動

| 項目 | 設定値・挙動 |
|---|---|
| プロジェクト名 | `endfield-aic`（公開 URL: `https://endfield-aic.pages.dev`） |
| production_branch | `main` |
| ビルド方式 | 直接アップロード（Git 連携なし。wrangler が `dotnet publish` の成果物ディレクトリをアップロード） |
| 配信ルート | `dotnet publish` の出力 `wwwroot/`（`_framework/`・`data/`・`index.html` 等を含む） |
| SPA フォールバック | Pages の標準挙動に委ねる。トップレベルの `404.html` がないプロジェクトでは、一致する静的アセットがないパスへの要求が `/` に回される。`_redirects` の `/* /index.html 200` はアセット一致に関係なく全要求を書き換えてしまい、`data/master.json` や `_framework/*` まで `index.html` を返すため使用しない |
| リージョン | Pages はエッジ分散型でリージョン指定を持たない（要件 §8、リージョン選定ルール対象外） |

### `_headers` の内容

Pages の既定ヘッダはキャッシュ可能な応答に `Cache-Control: public, max-age=0, must-revalidate` を付ける。
`_headers` ではこの挙動を `data/` と `_framework/` に明示しておき、正本 JSON やフレームワーク更新がブラウザの強いキャッシュに滞留しないことを保証する。
.NET 8 の発行成果物はファイル名にコンテンツハッシュを含まないため、immutable 系の長期キャッシュは設定しない。

```
/data/*
  Cache-Control: public, max-age=0, must-revalidate
/_framework/*
  Cache-Control: public, max-age=0, must-revalidate
```

## 4. ワークフロー構成（deploy-pages.yml）

トリガは `push: main` と `workflow_dispatch`（コミットを伴わない再デプロイ用）。
手順は CI と同じ検証を挟んでからデプロイし、検証を通らない成果物が公開されないようにする。

`pages deploy` の `--branch` は実行対象 ref（`github.ref_name`）に揃える。
main での実行は本番デプロイ、それ以外のブランチでの dispatch はプレビューデプロイとなり本番へ触れない。
連続する push で新旧のジョブが重なったとき古い成果物が後勝ちしないよう、`concurrency` グループ（`cancel-in-progress: false`）で直列化する。
グループは ref 単位（`pages-deploy-${{ github.ref }}`）とし、プレビュー用 dispatch がキュー中の本番実行を押し出さないようにする。

1. `actions/checkout`
2. `actions/setup-dotnet`（`8.0.x`）
3. `dotnet test -c Release`
4. `tools/validate_master.py`（CI と同じく `jsonschema[format]==4.25.1` で正本検証）
5. `dotnet publish src/EndfieldAicWeb.App -c Release -o artifacts/app`
6. `cloudflare/wrangler-action@v3` で `wrangler pages deploy artifacts/app/wwwroot --project-name=endfield-aic`
   - wrangler のバージョンは公開から 7 日以上経過した安定版でピンする（`wranglerVersion`）

### シークレット

| 名前 | 内容 |
|---|---|
| `CLOUDFLARE_API_TOKEN` | org シークレットと同一の Pages 編集権限つきトークン |
| `CLOUDFLARE_ACCOUNT_ID` | アカウント ID `1aa68e0ac8f41a6a2bdc8bf2ec8ef733` |

リポジトリ管理者が GitHub の Settings → Secrets and variables → Actions で登録する（`gh secret set` でも可。Devin の App トークンには secrets 書き込み権限がないため手作業となる）。
未登録の間はワークフローが認証失敗で止まるため、登録はマージと合わせて行う。

## 5. 成果物への同梱

- `data/master.json`: 既存の `CopyMasterJson` / `CopyMasterJsonToPublish` ターゲットで `wwwroot/data/` と発行成果物へコピー済み（Phase 4）。
- `data/icons/`: 同様の構成で `wwwroot/data/icons/` と発行成果物へコピーするターゲットを追加する。
  対象は `*.png` に限定し、`.gitkeep` などの非画像は成果物へ載せない。画像がない間は `Copy` タスクが何もしないため、実画像の追加を待たずに経路だけ整備できる。

## 6. 検証手順

1. ローカルで `dotnet publish` し、出力 `wwwroot/` に `index.html`・`_framework/`・`data/master.json`・`_headers` が含まれることを確認する。
2. wrangler でプロジェクトを作成し、ローカルから初回デプロイする。
3. 公開 URL を `curl` で検証する。
   - `/` が 200 で `index.html` を返す
   - `/data/master.json` が 200 で正本を返す
   - `/_framework/blazor.webassembly.js` が 200 を返す
   - 存在しないパスが SPA フォールバックで `index.html` を返す
   - `/data/*` と `/_framework/*` の応答に設定した `Cache-Control` が出る
4. ブラウザで公開 URL を開き、WASM が起動して計算 UI が表示されることを確認する。
5. シークレット登録後は、main へのマージでワークフローが自動デプロイすることを以後の PR で確認する。

## 7. 作業順序

1. 本書を作成する。
2. csproj の icons 同梱ターゲットと `wwwroot/_headers` を追加する。
3. `deploy-pages.yml` を作成する。
4. `dotnet build`・`dotnet test` を通し、ローカル publish 成果物を確認する。
5. Pages プロジェクト作成・初回デプロイ・§6 の検証を実施する。
6. Actions シークレットを登録する。
7. `implementation-plan.md` の Phase 5 チェックリストを更新し、README に公開 URL を追記して PR を作成する。
