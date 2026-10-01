# Phase 6 テスト仕様書

**対象**: Phase 6 成果物（Application 層の管理用ユースケース、および Admin 管理ツール UI・公開構成）
**前提ドキュメント**: [requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)、[implementation-plan.md](../implementation-plan.md)、[implementation-plan-phase6.md](implementation-plan-phase6.md)

> 本書は Phase 6 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。`tests/EndfieldAicWeb.Application.Tests` に配置する（Admin 層の純粋ロジックは Application に寄せたため、ここで網羅できる） |
| テストデータ | コード上で `MasterDocument` のフィクスチャを構築する（ApplicationFixtures の既存ヘルパー方式）。`data/master.json` の往復は Infrastructure.Tests が担保済みのため扱わない |
| 実行コマンド | `dotnet test` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | Admin を `dotnet run` で起動し、ブラウザプレビューで §5 の項目を確認する（ui-mock-first ルールに従い、ユーザー確認を先に取った） |
| Blazor UI 自動テスト | 対象外（bUnit・E2E は導入しない。implementation-plan-phase6.md §1 参照） |
| Cloudflare Access | 外部設定のため自動テスト対象外。§5 の手動確認で扱う |

## 2. フィクスチャ定義

### M-01: 参照を含む最小文書

削除前の参照列挙を試すための `MasterDocument`（要件 §7 の編集対象と同じ構造）。

- アイテム: `i-ore`（採取素材）、`i-gas`（Pipe）、`i-part`（イベント `ev-on` 所属）、`i-fc`（固定消費用）
- 設備: `f-asm`、`f-disp`
- 環境: `env-gas`（供給設備 `f-disp`、消費アイテム `i-gas`、イベント `ev-on` 所属）
- イベント: `ev-on`
- レシピ: `r-part`（入力 `i-ore`×2、出力 `i-part`×1、ペア `[f-asm 4秒, f-asm 3秒 env-gas 固定消費 i-fc]`、イベント `ev-on` 所属）

## 3. テスト項目一覧（Application 層）

### IDF: 新規 Id の採番

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| IDF-01 | 空の一覧 | 既存 Id なし・接頭辞 `item` | `item-001` |
| IDF-02 | 連番の末尾追加 | `item-001`・`item-002` 在り | `item-003` |
| IDF-03 | 欠番の穴埋め | `item-001`・`item-003` 在り | `item-002` |
| IDF-04 | 接頭辞の区別 | `item-001` のみ在り・接頭辞 `fac` | `fac-001`（他種別の番号に影響されない） |
| IDF-05 | 桁の固定 | `item-001`〜`item-009` 在り | `item-010`（3 桁ゼロ埋めのまま） |

### ENT: 新規エンティティの既定値

「＋ 新規」ボタンで作られる初期値が、そのまま文書に置いても必須違反を出さないことを確認する。生成物を M-01 相当の文書に追加し `MasterValidator.ValidateAll` を実行して、そのエンティティ自身の違反がないことを見る。

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| ENT-01 | アイテム | 新規 Id・文書の DataVersion | Id/Name/VersionAdded/Category/TransportKind が既定値で埋まり、そのアイテムの違反がない |
| ENT-02 | 設備 | 同上 | 幅・高さ・消費電力が既定値で埋まり、その設備の違反がない |
| ENT-03 | 環境 | 既知の設備 Id・アイテム Id を渡す | ProviderFacilityId/ConsumeItemId/消費速度が埋まり、その環境の違反がない |
| ENT-04 | 環境（候補なし） | 参照先を渡さない | 必須違反として検証に残る値（空文字）になる |
| ENT-05 | イベント | 新規 Id | 期間未設定（常設）で、そのイベントの違反がない |
| ENT-06 | レシピ | 出力アイテム・設備の Id を渡す | Outputs・Facilities が 1 行ずつ入り、ペアの RecipeId が自身に一致。そのレシピの違反がない |
| ENT-07 | レシピ（候補なし） | 参照先を渡さない | Outputs/Facilities が空で、行数不足の違反として検証に残る |

### VER: DataVersion 提案

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| VER-01 | 通常版 | `0.1.0` | `0.1.1` |
| VER-02 | 桁繰上げなし | `1.9.9` | `1.9.10` |
| VER-03 | ゼロ padding なし形式 | `10.20.30` | `10.20.31` |
| VER-04 | 非 semver 形式 | `1.0`・`v1.0.0`・`1.0.0-beta`・`1.0.0.1`・`""`・null | いずれも null（提案しない） |

### REF: 削除前の参照列挙

削除確認ダイアログに載せる参照一覧の列挙。M-01 の文書を対象にする。

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| REF-01 | アイテム（入力側） | `i-ore` | `Recipe r-part` の `Inputs[0].ItemId` の 1 件のみ |
| REF-02 | アイテム（出力側＋イベント所属なし経路） | `i-part` | `Recipe r-part` の `Outputs[0].ItemId` の 1 件のみ |
| REF-03 | アイテム（環境消費・固定消費） | `i-gas`・`i-fc` | `i-gas` は `Environment env-gas` の `ConsumeItemId`。`i-fc` は `Recipe r-part` の `Facilities[1].FixedConsumption.ItemId` |
| REF-04 | 設備 | `f-disp` | `Environment env-gas` の `ProviderFacilityId` の 1 件。`f-asm` は `r-part` の `Facilities[0]`・`[1]` の 2 件 |
| REF-05 | 環境 | `env-gas` | `Recipe r-part` の `Facilities[1].EnvironmentId` の 1 件 |
| REF-06 | イベント | `ev-on` | `Item i-part`・`Environment env-gas`・`Recipe r-part` の各 `GameEventId` の 3 件 |
| REF-07 | 参照なし | 無所属の孤立 Id | 空リスト |

## 4. 受け入れ条件との対応

- 全項目緑であること。
- IDF・ENT で「新規追加した時点で検証を通る初期値」を確認する（編集 → 検証の入口）。
- REF で削除前の影響確認（要件 §7 の整合性維持の補助）を確認する。
- VER でエクスポート時の DataVersion 更新支援を確認する。

## 5. 手動確認項目（ブラウザプレビュー）

実装のブラウザプレビューをユーザーに操作してもらい、次を確認した。反映済みのフィードバックは後注。

| ID | 確認内容 | 結果 |
|---|---|---|
| MN-01 | 同梱マスタを読み込み → 文書の件数・DataVersion が表示される | OK（Devin 実機確認） |
| MN-02 | 各エンティティページで選択・編集・新規・削除ができ、参照のある削除は参照箇所つき警告になる | OK（Devin 実機確認、ユーザー確認） |
| MN-03 | 検証を実行すると違反が一覧表示され、クリックで該当エンティティへ移動する | OK（Devin 実機確認） |
| MN-04 | 未エクスポートの変更時はダーティ表示が出て、再読み込みに確認ダイアログが挟まる | OK（Devin 実機確認） |
| MN-05 | 計算プレビューで編集中データの計算結果（素材・設備・環境・電力・余剰）が見える | OK（Devin 実機確認、ユーザー確認） |
| MN-06 | エクスポートで全置換 JSON がダウンロードされ、DataVersion が更新される。違反時はブロックされる | OK（Devin 実機確認） |
| MN-07 | URL 入力からデプロイ済みのマスタ JSON を読める（App 側の `Access-Control-Allow-Origin` 併用） | OK（Devin 実機確認。localhost・本番の両経路で成功） |
| MN-08 | `endfield-aic-admin.pages.dev` へアクセスすると Cloudflare Access のメール OTP を要求され、非許可メールは入れない | OK（302 リダイレクト・OTP 画面の表示を実機確認。非許可メール `not-an-admin@example.com` ではコードが送信されず先へ進めないことも確認。Cloudflare の仕様で非許可メールにも「送信済み」と出るため、実効拒否はコード非配信という形になる。管理者メールでの OTP 完了は管理者側で確認） |
| MN-09 | 非公式ファンツール向け管理ツールの明記・非公開であることが見える | OK（ヘッダ・フッタ表示） |

## 6. 備考

- Admin WASM のビルド成果物には `data/master.json` が同梱コピーされ、初期読み込み元はデプロイ時点の原本のコピーになる。URL・ファイル読み込みはその代替経路である。
- URL 読み込みは別オリジン（`endfield-aic.pages.dev`）への取得のため、App 側 `_headers` に `/data/*` への `Access-Control-Allow-Origin: *` を追加する（公開データのため * でよい）。CORS は取得先オリジンの応答ヘッダで判定されるため、localhost の dev server からも本番 URL の読み込みは成功する（ローカル `_headers` は関係しない）。
