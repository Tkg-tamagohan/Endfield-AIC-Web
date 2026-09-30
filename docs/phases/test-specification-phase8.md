# Phase 8 テスト仕様書

**対象**: Phase 8 成果物（アイコン正規化の改修: 原寸保持＋APNG アニメーション対応）
**前提ドキュメント**: [requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AA）、[implementation-plan.md](../implementation-plan.md)、[implementation-plan-phase8.md](implementation-plan-phase8.md)

> 本書は Phase 8 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> 従来のテスト項目は Phase 7 以前の仕様書を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。管理ツールの編集セッションは `tests/EndfieldAicWeb.Admin.Tests`、マニフェスト照合は `tests/EndfieldAicWeb.Infrastructure.Tests` に配置する |
| テストデータ | 実際の APNG バイト列をコード内へ base64 で埋め込む（I-03）。GIF 入力→APNG 変換自体はブラウザ依存処理のため、自動テストは「APNG バイト列がパイプラインを無改変で通る」ことを対象とする |
| 実行コマンド | `dotnet test` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | Admin・App を `dotnet run` で起動し、ブラウザプレビューで §5 の項目を確認する（ui-mock-first ルールに従い、ユーザー確認を先に取った） |
| 正規化・APNG 変換ロジック | `wwwroot/js/icons.js` のブラウザ処理（Canvas・WebCodecs ImageDecoder・UPNG.js）のため自動テスト対象外。§5 の手動確認で扱う |
| Blazor UI 自動テスト | 対象外（bUnit・E2E は導入しない。従来 Phase と同じ方針） |

## 2. フィクスチャ定義

### I-03: APNG バイト列

acTL チャンクを含む実際の APNG（3 フレーム、80×80、遅延 400/800/400ms、Pillow で生成した合成画像）。
C# 側の規約はバイト列を対象にするため内容は何でもよいが、将来 PNG 構造を前提にした検査が紛れ込んだときに落ちるよう、静止 PNG ではなく APNG を用いる。

## 3. テスト項目一覧

### ANM: APNG のパイプライン通過

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| ANM-01 | APNG の登録とプレビュー | I-03 を `RegisterIcon` で登録 | エントリの `File` が `icons/<Key>.png`、`Sha256`・`Bytes` が I-03 からの算出値、`IconDataUrl` が `data:image/png` で I-03 と同内容を返す |
| ANM-02 | APNG を含む zip エクスポート往復 | I-03 を登録した文書で `ExportZip` → `LoadZip` | zip 内 `icons/<Key>.png` が I-03 とバイト一致。読み込み側で取得済みとして数えられ、`IconDataUrl` が返る |
| ANM-03 | APNG のマニフェスト照合 | I-03 とそのエントリで `IconManifestVerifier.Verify` | エラーなし。I-03 を 1 バイト改変した実体は `Sha256` 違反で失敗する |
| ANM-04 | APNG の zip 配置往復 | I-03 を含む zip を `IconArchive` で生成 → `TryReadZip` | `icons/<名>.png` キーで I-03 とバイト一致して取れる |

## 4. 受け入れ条件との対応

- 全項目緑であること。
- ANM-01〜04 で「APNG を `icons/<Key>.png`・`image/png` の規約のまま通す」（仕様決定 AA）を担保する。将来 PNG 構造を前提にした処理が加わっても退行として検出できる。
- 「128 ピクセル未満の画像が拡大されずに登録される」「アニメーション画像が管理ツール・公開アプリの双方で動いて表示される」（受け入れ条件）はブラウザ処理のため §5 で担保する。

## 5. 手動確認項目（ブラウザプレビュー）

実装のブラウザプレビューで次を確認した。小画像の配置はブラウザコンソールで正規化出力のピクセルを検証した。

| ID | 確認内容 | 結果 |
|---|---|---|
| MN-01 | 128px 未満の静止画（60×40）を正規化すると、40×40 の原寸が 128×128 透過キャンバスの中央（オフセット 44）に拡大なしで配置される | OK（Devin 実機・出力 PNG のピクセル検証） |
| MN-02 | 128px 超の静止画（300×200）は 128×128 へ縮小された静止 PNG になる | OK（Devin 実機・出力検証） |
| MN-03 | GIF 入力が APNG として登録され、エディタのプレビューでアニメーション再生される。出力は acTL+3 フレームで入力遅延（300/600/1200ms）を保持する | OK（Devin 実機確認） |
| MN-04 | APNG 入力も APNG として登録され、入力遅延（400/800/400ms）を保持する | OK（Devin 実機・出力検証） |
| MN-05 | エクスポートした master-export.zip を展開した `data/icons/` の APNG をブラウザで直接開くとアニメーション再生できる | OK（Devin 実機確認） |
| MN-06 | App のアイテム候補一覧で APNG アイコンがアニメーション表示され、静止アイコンと `?` プレースホルダは従来どおり出る | OK（Devin 実機確認） |
| MN-07 | ユーザー自身による操作確認（ui-mock-first） | OK |

## 6. 備考

- ImageDecoder のフレーム扱いはブラウザ実装に依存する。Chrome は blend/dispose 適用済みの合成フレームを返すことを実機で確認したため、実装は `visibleRect` が全面を覆うフレームをクリア後に描画し、部分矩形のみ従来の重ね描きで近似する。検証した GIF で出力フレームが PIL の合成結果と一致することを確認した（MN-03）。
- `ImageDecoder` が使えない環境やアニメーションとして扱えない入力は、先頭フレームの静止画へ縮退する（警告付き）。APNG 入力の副経路として UPNG.js の `UPNG.decode` を使う。
- UPNG.encode が遅延を 16bit 分子（分母 1000ms）で書き込むため、65535ms を超えるフレーム遅延は同一フレームの繰り返しに分割して合計時間を保つ（上限 64 分割）。70 秒遅延の GIF で [65535, 4465] への分割を実機確認した。
- 同梱ライブラリ（UPNG.js・pako）のライセンスは `src/EndfieldAicWeb.Admin/wwwroot/js/THIRD-PARTY-LICENSES.txt` に同梱した。
