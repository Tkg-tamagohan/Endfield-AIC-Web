# Phase 6 実装詳細計画

**対象フェーズ**: Phase 6（管理ツール UI: Admin Blazor WASM＋Access 公開）
**前提ドキュメント**: [implementation-plan.md](implementation-plan.md)、[requirements.md](requirements.md)、[decision-records.md](decision-records.md)
**関連ドキュメント**: [test-specification-phase6.md](test-specification-phase6.md)（本 Phase のテスト仕様）

> 本書は Phase 6 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### 作るもの

- `EndfieldAicWeb.Admin` の管理 UI（Blazor WASM）
  - ホーム `/`：正本 JSON の読み込み（同梱のデプロイ済み・URL 指定・ファイル選択）、DataVersion の編集、整合性検証の実行と結果一覧、JSON エクスポート（ファイルダウンロード）
  - エンティティ編集ページ `/items`・`/facilities`・`/environments`・`/events`・`/recipes`：左ペイン一覧（検索・新規・削除）＋右ペイン編集フォーム
  - 計算プレビュー `/preview`：編集中データで Domain の計算を実行し、素材・設備・環境・電力・余剰・警告を表示する（投入データの妥当性確認用、要件 §7-3）
- `EndfieldAicWeb.Application` の管理用ユースケース群（§8）
- `EndfieldAicWeb.Admin` の構成変更
  - Infrastructure 参照の追加（`MasterJsonLoader`・`MasterExporter`）
  - `data/master.json`・`data/icons/` の同梱ターゲット（App と同じ方式。アイコン自体の取り込みは Phase 7）
  - `wwwroot/_headers`・`wwwroot/js/download.js`
- `deploy-pages.yml` の `deploy-admin` ジョブ追加（Pages プロジェクト `endfield-aic-admin` への配信）
- App 側 `wwwroot/_headers` の `/data/*` への `Access-Control-Allow-Origin: *` 追加（Admin から公開アプリの正本 JSON を URL 指定で読めるようにするため。公開データのため `*` でよい）
- Cloudflare 側の設定：Pages プロジェクト `endfield-aic-admin`、および同ドメインへの Access アプリケーション（メール OTP）

### 作らないもの

- アイコン取り込み・正規化・`icons/` 出力（Phase 7）。Icons マニフェストは読み込んだ文書のものをエクスポートでそのまま保持する
- 実データ投入（Phase 7）。開発用サンプルをそのまま使う
- 協調編集・サーバー側保存・書き込み API（仕様決定 E）
- 計算プレビューでのペア代替選択・散布機台数上書き。プレビューは妥当性確認が目的のため既定選択の結果だけを示す。細かい確認は公開アプリ側で行う
- bUnit・E2E 自動化。ブラウザ動作は要件 §9 の方針どおり手動確認とし、ロジックは Application 層の単体テストで担保する

## 2. 読み込み・検証・エクスポートの流れ（要件 §7-1/2/4）

1. 読み込みは次の 3 系統。いずれも `MasterJsonLoader.Load` で構文・構造・意味を検証し、違反があれば一覧表示して編集状態へ入らない。
   - 「同梱マスタを読み込む」：Admin 自体に同梱される `data/master.json`（デプロイ時点の正本）。
   - URL 指定：既定値は公開アプリの `https://endfield-aic.pages.dev/data/master.json`。App 側 `_headers` の `Access-Control-Allow-Origin: *` でオリジン横断の取得を可能にする。
   - ファイル選択：`InputFile` でローカルの JSON を読む。
2. 編集は読み込んだ `MasterDocument` を直接書き換える。変更のたびに全体検証を走らせず、「検証を実行」ボタンとエクスポート時の 2 箇所で `MasterValidator` 相当の規則を適用する。
3. エクスポートは `MasterExporter.Export`（全置換、違反時は `MasterValidationException`）に、ホームで入力した `DataVersion` を載せて実行する。既定値は読み込み版のパッチを 1 上げた提案値（`DataVersionBumper`）。違反があれば一覧を表示して書き出さない。
4. 出力は `master.json` というファイル名でダウンロードする（`IJSRuntime` 経由の Blob ダウンロード）。成果物をリポジトリへコミット → PR → CI 検証 → マージで配信される（仕様決定 D）。

## 3. 画面構成

Phase 4 と同じシェル（`site-header`・`site-main`・`site-footer`）とダークテーマを使う。ヘッダー内にページ遷移ナビを置く。管理ツールは非公開・管理者のみ利用のため、スマートフォン最適化は Phase 4 ほど厳密に求めないが、狭幅で潰れない 1 カラム化は入れる。

| ページ | 内容 |
|---|---|
| `/` | 読み込みブロック（同梱/URL/ファイル）、現在の文書情報（DataVersion・件数）、検証実行・結果一覧、DataVersion 入力とエクスポートボタン |
| `/items` | 一覧（名前・Id・カテゴリで部分一致検索、新規、削除）＋編集フォーム（共通属性・Category・IsBaseMaterial・TransportKind・GameEventId） |
| `/facilities` | 同上＋編集フォーム（共通属性・Width・Height・PowerConsumption） |
| `/environments` | 同上＋編集フォーム（共通属性・ProviderFacilityId・ConsumeItemId・ConsumeRatePerSecond・GameEventId） |
| `/events` | 同上＋編集フォーム（共通属性・ActiveFrom・ActiveTo。空欄は期間なし・常設） |
| `/recipes` | 同上＋編集フォーム（共通属性・GameEventId・Inputs・Outputs（SortOrder つき）・Facilities ペア（FacilityId・CycleTime・EnvironmentId・FixedConsumption の有無と ItemId/RatePerSecond）） |
| `/preview` | 目標行（アイテム＋個/分）・有効イベントの切替・計算ボタン、素材・設備・環境（dispenser 台数付き）・電力・余剰・警告の表示（`ResultViewBuilder`、調整済/未調整の表示切替は公開アプリと同じ構成） |

### 編集フォームの規則

- 参照を取る項目（Item 系の ItemId、Environment の ProviderFacilityId/ConsumeItemId、ペアの FacilityId/EnvironmentId、各 GameEventId）は自由入力ではなく文書内の `<select>` で選ぶ（空選択 = null）。
- Id は自由入力とする（旧 Admin 踏襲）。Id 変更や削除による参照切れは検証違反として報告する。
- 削除ボタンは行ごとに置き、削除対象が他エンティティから参照されている場合は `MasterReferenceFinder` の一覧を確認文に載せてから消す（ブロックはしない）。
- 新規作成は `EntityFactory` の既定値で追加してすぐ編集フォームへ遷移する。
- 数値項目は文字列入力とし、確定（blur）時に `double.TryParse`（InvariantCulture）で検査する。不正値は編集側に保持したままエラーを表示し、文書へは確定しない。

## 4. 検証と警告の見せ方

- 検証結果は `MasterValidationError` の一覧としてホームに出し、編集中もナビのバッジ等で件数を把握できるようにする（件数バッジはホーム表示のみでも可）。
- 計算プレビューはスナップショット生成に先立ち検証を実行し、違反があれば先にエラー一覧を示す（実体化できる文書が不正でも `CalculationService` 側で投げないよう事前に遮る）。

## 5. デプロイ構成

- Pages プロジェクト：`endfield-aic-admin`（公開 URL `https://endfield-aic-admin.pages.dev`）、production_branch `main`、直接アップロード（Phase 5 と同方式）。
- `deploy-pages.yml` に `deploy-admin` ジョブを追加する。手順は `deploy-app` と同列（test → validate_master.py → `dotnet publish src/EndfieldAicWeb.Admin -c Release -o artifacts/admin` → wrangler deploy）。
- `wwwroot/_headers` は `/data/*`・`/_framework/*` に `Cache-Control: public, max-age=0, must-revalidate` を置く（App と同じ）。SPA フォールバックは Pages 標準挙動に委ねる（Phase 5 §3 と同じ理由）。
- Pages・Access はエッジ分散型でリージョン指定を持たない（リージョン選定ルール対象外）。

## 6. Cloudflare Access（仕様決定 E）

`endfield-aic-admin.pages.dev` 全体を Access アプリケーション（`self_hosted`）として保護し、ポリシーは「許可メール = 管理者本人の Gmail 1 件」のみとする。認証方式は Zero Trust 既定の One-time PIN（メール OTP）で、追加の IdP 設定は不要。

- API トークンに `Zero Trust: Access` 権限があれば API でアプリケーションとポリシーを作成する。権限がない場合は、Cloudflare ダッシュボード（Zero Trust → Access → Applications）での手順をユーザーへ案内する。
- 保護確認は、未認証で `https://endfield-aic-admin.pages.dev/` へアクセスした際に Access のログイン画面（メール OTP）へ誘導されること、および `curl` で 302/`401` 系応答になることで行う。

### 現状（2026-09-29 時点）

- Pages プロジェクト `endfield-aic-admin` は作成済みで、初回デプロイ済み（`https://endfield-aic-admin.pages.dev` が 200 で公開中。Access 未適用のため誰でも見える状態）。
- Zero Trust 組織が未作成のため Access アプリケーションを API から作れない（`access.api.error.not_enabled`）。また現行の API トークンには Zero Trust 系スコープがなく、組織作成 API も拒否される。

残作業（ユーザー側の Cloudflare 操作）:

1. Cloudflare ダッシュボード → Zero Trust を初めて有効化する。無料プランを選び、team name を決める（例: `tkg-aic`）。
2. Zero Trust → Access → Applications → Add an application → Self-hosted で、`Application domain` に `endfield-aic-admin.pages.dev`（サブドメイン `endfield-aic-admin`、ドメイン `pages.dev`）を指定して作成する。
3. ポリシーは「Allow + Include: Emails = 管理者本人の Gmail アドレス」1 件のみ。ログイン方式は既定の One-time PIN（メール OTP）のままでよい。
4. 未認証でアクセスして Access の OTP 画面へ誘導されること、別メールでは入れないことを確認する。

代案: Devin 側の `CLOUDFLARE_API_TOKEN` に Zero Trust 系の Edit 権限を追加し、組織作成後であればアプリケーションとポリシーは API で設定できる。組織の初回有効化（プラン選択・チーム名決定）はダッシュボードが必要と推測される（API での作成は現行権限では未検証）。

## 7. Application 層の構成

| 型 | 責務 |
|---|---|
| `EntityFactory` | 各エンティティの新規作成（Id 指定、検証を通る既定値入り） |
| `DataVersionBumper` | 現在版からの次版提案（semver 形なら patch+1、それ以外は提案なし） |
| `MasterReferenceFinder` | 指定 Id を参照している全エンティティ・箇所の列挙（削除確認・参照切れの把握に使う） |

読み込み・保持・検証・エクスポートの実行制御は Admin 側の `Services/AdminDocumentService` が担う（App の `MasterDataService` と同じ置き方）。

## 8. 検証手順

1. `dotnet build`・`dotnet test` を通す。
2. Admin を `dotnet run --project src/EndfieldAicWeb.Admin --no-launch-profile --urls http://127.0.0.1:5181` で起動し、次を自分で確認する。
   - 同梱マスタの読み込みで件数が出る
   - アイテム編集 → 検証実行で違反/健全の表示が動く
   - 計算プレビューで素材・警告が出る
   - エクスポートで `master.json` がダウンロードされ、内容が編集を反映している
3. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する（ui-mock-first。テスト作成はこの後）。
4. wrangler で `endfield-aic-admin` を作成し初回デプロイ、Access 設定、未認証での遮断を確認する。
5. [test-specification-phase6.md](test-specification-phase6.md) の項目どおりに Application 層テストを作成し、`dotnet test` 全緑にする。

## 9. 作業順序

1. 本書を作成する。
2. Application のユースケース群を実装する。
3. Admin：同梱ターゲット・読み込み・編集 UI・検証・エクスポート・プレビュー・スタイルを実装する。
4. ローカル起動して主要フローを自分で動作確認する。
5. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する。
6. Pages プロジェクト作成・初回デプロイ・Access 設定・ワークフロー追加を行う。
7. [test-specification-phase6.md](test-specification-phase6.md) を作成し、テストを実装して `dotnet test` 全緑にする。
8. `implementation-plan.md` の Phase 6 チェックリストを更新し、README に管理ツール URL を追記して PR を作成する。
