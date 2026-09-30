# Phase 8 実装詳細計画

**対象フェーズ**: Phase 8（アイコン正規化の改修: 原寸保持＋アニメーション対応）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)、[implementation-plan-phase7.md](implementation-plan-phase7.md)（アイコンパイプラインの現行実装）

> 本書は Phase 8 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### 作るもの

- `EndfieldAicWeb.Admin` の画像正規化（`wwwroot/js/icons.js`）の改修
  - 小さい画像を拡大しない正規化規則（出力は常に 128×128 キャンバス、クロップ領域が 128 ピクセル以下なら原寸を中央配置）
  - アニメーション画像の取り込み（GIF・APNG を入力し、フレーム単位で同一規則を適用して APNG で保存）
  - APNG エンコードのため `UPNG.js`（MIT ライセンス）を `wwwroot` 配下に同梱する
- `IconEditor.razor` の対応（`accept` への `image/apng` 追加、必要なら JS 関数名の追従）
- 仕様文書の更新（requirements §5.10、decision-records への仕様決定 AA、implementation-plan の Phase 8 チェックリスト）
- 本書と、[test-specification-phase8.md](test-specification-phase8.md)（実装時に作成）

### 作らないもの

- GIF 形式での保存。アニメーションの保存形式は APNG に一本化し、`icons/<Key>.png`・`image/png` の既存規約を変えない
- `ImageDecoder` が使えないブラウザ向けの GIF デコードライブラリ追加。デコードできない環境では先頭フレームの静止画に縮退する現行相当の挙動で許容し、必要が判明した時点で検討する
- マニフェスト・スキーマ・リゾルバ・エクスポート・表示側の変更。APNG は拡張子 `.png` と MIME `image/png` のまま扱えるため、これらは無変更で成立する（§3.3）
- アニメーションのループ回数制御。UPNG.js が出力する APNG は無限ループ固定とし、GIF の一般的な挙動と同じにする
- bUnit・E2E 自動化（従来どおりブラウザ動作は手動確認とし、ロジックは単体テストで担保）

## 2. 仕様の確定事項

### 正規化規則（仕様決定 AA）

- 取り込んだ画像は中央正方形にクロップし、出力を常に 128×128 に揃える。
- クロップ領域の一辺が 128 ピクセルを超える場合は 128×128 へ縮小する。128 ピクセル以下の場合は拡大せず、原寸のまま 128×128 の透過キャンバス中央に配置する。
- 静止画の出力形式は従来どおり PNG である。

### アニメーション（仕様決定 AA）

- アニメーション画像（GIF・APNG）を取り込める。各フレームへ上記の正規化規則を適用し、128×128 の APNG として保存する。
- APNG は PNG の拡張形式であり拡張子 `.png`・MIME `image/png` をそのまま使うため、ファイル名規約・マニフェスト・スキーマに変更を入れない。
- フレームの遅延時間は入力画像の値を引き継ぐ。ループは無限ループとする。
- アニメーションのデコードは WebCodecs の `ImageDecoder` を主経路とし、APNG 入力については同梱する UPNG.js のデコード機能を副経路とする。どちらも使えない場合は先頭フレームの静止画として取り込む（警告を出してよい）。
- アニメーション WebP も `ImageDecoder` でデコードできれば同じ経路で取り込める。受け入れは副次的な範囲とし、動作しない場合の専用対応は行わない。

### 変わらないもの

- キー文字種（`^[A-Za-z0-9_-]{1,64}$`）、マニフェスト構造（`Key`・`File`・`Sha256`・`Bytes`）、`icons/<Key>.png` のファイル規約、Sha256/Bytes による照合、未設定・未解決時のプレースホルダ表示、レシピの主出力アイテムへのフォールバック。
- 権利クリアな画像のみを同梱する規約（仕様決定 R で継承する旧 AM）。

## 3. 設計詳細

### 3.1 正規化処理（`wwwroot/js/icons.js`）

現行の `normalizeIconPng` を拡張し、静止画とアニメーションの双方を扱う入口関数にする。

1. 入力（data: URI）をデコードする。
   - `ImageDecoder` が利用できる場合はそちらで試行し、`frameCount > 1` ならアニメーション経路へ進む。
   - APNG 入力で `ImageDecoder` がアニメーションとして扱えない場合は、`UPNG.decode` でフレーム列を取得する。
   - いずれも失敗・非対応の場合は静止画経路へフォールバックする（先頭フレーム相当）。
2. 静止画経路では、現行と同じく `Image` 要素でデコードして中央正方形にクロップする。一辺が 128 ピクセルを超えれば 128×128 へ縮小描画し、以下なら `(128 - size) / 2` の位置へ原寸描画する。
3. アニメーション経路では、フレームごとに 128×128 キャンバスへ同一規則で描画して `ImageData` の RGBA バイト列と遅延時間を収集し、`UPNG.encode(frames, 128, 128, 0, delays)` で APNG バイト列を得る。
   - `ImageDecoder` が返すフレームが差分フレーム（部分領域のみ更新）かどうかはブラウザ実装に依存するため、実装時に Chrome で実画像を流して検証する。差分が返る場合はオフスクリーン Canvas で前フレームへ重ね描きしてから採取する。
   - どちらの経路でも返す値は `data:image/png;base64,...` の data: URI で統一し、C# 側（`RegisterIcon`）の契約を変えない。

### 3.2 依存ライブラリ

- `UPNG.js`（<https://github.com/photopea/UPNG.js>、MIT ライセンス確認済み）を `wwwroot/js/` 配下に同梱し、`index.html` に `<script>` を追加する。
- NuGet 依存は増やさない（Phase 7 と同じ方針）。リポジトリ規約のとおりライセンスを確認してから導入する。

### 3.3 変更が入らない箇所

APNG を `.png`・`image/png` のまま扱うため、次は全て無変更で成立する。

- Domain/Application: `IconKeyRules`（`FileName`・`ManifestFile`）、`IconKeyFallback`。
- Infrastructure: `IconResolver`（マニフェスト照合・収録外キーのファイル名一致）、`IconManifestVerifier`、`IconFiles`（Sha256/Bytes 照合は形式非依存）、`IconExportPlanner`、`IconArchive`（`entry.File` の拡張子をそのまま zip へ使う）。
- Admin: `AdminDocumentService.IconDataUrl`・`RegisterIcon`（`data:image/png`・PNG バイト列扱いで APNG をそのまま通せる）。
- App: `IconCatalog.Url`（`data:image/png` で APNG を正しく表示できる）、`EntityIcon.razor`（`<img>` がブラウザ側で APNG を再生する）。
- データ・検証: `data/master.schema.json`（`File` は長さ制約のみで拡張子を縛らない）、`tools/validate_master.py`（`.png` 接尾辞の走査のまま成立）、既存の `data/icons/*.png`。
- 既存テスト: `IconPipelineTests`・`IconManifestTests`・`AdminIconTests`（いずれも形式非依存または C# 側規約のテスト）。

### 3.4 ドキュメント更新

- requirements §5.10 を新しい正規化規則と APNG 保存へ書き換える。
- decision-records に仕様決定 AA（アイコン正規化とアニメーション）を追加する。
- implementation-plan に Phase 8 のチェックリストを追加する。

## 4. 画面変更の確認手順（ui-mock-first）

1. `dotnet build`・`dotnet test` を通す。
2. Admin を `dotnet run --project src/EndfieldAicWeb.Admin --no-launch-profile --urls http://127.0.0.1:5181` で起動し、自分で次を確認する。
   - 128 ピクセル未満の画像を取り込むと、拡大されずシャープなまま 128×128 中央に配置される。
   - アニメーション GIF を取り込むと、プレビューでアニメーション再生される。
   - エクスポートした `master-export.zip` を展開し、`data/icons/` の APNG ファイルがブラウザで直接開くとアニメーション再生できる。
3. App を `dotnet run --project src/EndfieldAicWeb.App --no-launch-profile --urls http://127.0.0.1:5180` で起動し、APNG アイコンが一覧でアニメーション表示されること、静止アイコンが従来どおり出ることを自分で確認する。
4. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する（ui-mock-first。テスト作成はこの後）。

## 5. 作業順序

1. 本書を作成する。
2. `UPNG.js` を同梱し、`index.html` へ `<script>` を追加する。
3. `icons.js` に正規化規則（拡大禁止・中央配置）とアニメーション経路を実装し、`IconEditor.razor` の `accept` 等を追従する。
4. requirements §5.10・decision-records（AA）・implementation-plan（Phase 8）を更新する。
5. ローカル起動で §4 の自分確認を実施する。
6. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する。
7. [test-specification-phase8.md](test-specification-phase8.md) を作成し、必要なテストを実装して `dotnet test` 全緑にする。
8. PR を作成する。
