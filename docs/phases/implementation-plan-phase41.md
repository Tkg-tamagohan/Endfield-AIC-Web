# Phase 41 実装詳細計画

**対象フェーズ**: Phase 41（旧リポジトリ依存の解消）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 CT〜CY）
**関連ドキュメント**: [test-specification-phase41.md](test-specification-phase41.md)（本 Phase のテスト仕様）

> 本書は Phase 41 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書は計画 PR（文書のみ）で先行し、実装・検証は本書に基づく後続の実装 PR で main へマージする（Phase 34〜40 と同じ計画・実装の分割）。
> Phase 番号は 41 とする（main の現行最大は Phase 40）。仕様決定は CS の次の採番で CT〜CY を使用済み（CX・CY はレビューで見つかった仕様未記載の実装済み規則の明文化）、手動確認は MN-184 以降を使う（main の現行最大は MN-183。並行セッションの採番衝突に注意して push 前に main を再確認する）。

## 1. スコープ

### 背景と問題

本リポジトリは旧デスクトップ版 `Tkg-tamagohan/Endfield-AIC-Planner`（WPF、凍結済み）の Web 移植として始まった。
移植は完了段階にあり、コード・ビルド・CI から旧リポジトリへのパス参照や URL 取得は存在しない。
一方で文書面の依存が残っている。

- docs・src・tests の各所に「旧 X」形式で旧リポジトリ `docs/decision-records.md`（`dev/webification` ブランチ、A〜BY）の仕様決定を指す参照があり、requirements.md はその出典を外部リンクとして案内している
- AGENTS.md が「Planner の docs/・data/ を参照する」「参照実装は Planner の Domain/Calculation/Validation 層」「文書フォーマットは Planner に倣う」と規定し、セッションが旧リポジトリのクローンを前提にする
- 旧リポジトリには README の凍結案内追記（仕様決定 A）が未実施で残っている

旧リポジトリの README 追記と GitHub Archive（仕様決定 CW）が確定したため、仕様の正を本リポジトリ内で完結させ、作業手順と参照関係を自リポジトリに閉じる作業を Phase 41 とする。

### やること

- 現行効力を持つ旧決定を既存の Web 側仕様決定へ対応づけ、「旧 X」参照をすべて新番号または自完結な表現へ置き換える（§4 の対応表、仕様決定 CT）
- AGENTS.md の Planner 参照規定を自リポジトリ完結の記述へ改訂する（仕様決定 CV）
- requirements.md・implementation-plan.md の旧リポジトリ案内（参考実装・決定記録リンク）を改訂する
- src・tests のコードコメント中の「旧 X」参照を整理する
- 旧リポジトリ側で README 凍結案内の追記 PR を作成・マージし、GitHub Archive を実行する（仕様決定 CW）
- 旧決定の新旧対応表を本書 §4 に記録する

### やらないこと

- 旧リポジトリのアイコン資産の移植（権利クリアだが自動生成の仮置き画像であり非移植を確定、仕様決定 CU）
- 廃止済み・Planner 固有の旧決定の統合（仕様決定 CT の範囲外。§4 対応表の「統合しない」区分）
- アイコン規約・取り込みパイプラインの変更（仕様決定 R・AA と requirements §5.11 は存置）
- 「旧デスクトップ版」「旧 JSON」「旧フィールド名」「旧同梱マスタ」「旧仕様」など、決定 ID でない「旧」の言及の改訂（出来事の記述であり参照ではない）
- 旧リポジトリ内 `.agents/skills/` の個別削除（アーカイブに伴い休眠するため、仕様決定 CW）

## 2. 変更一覧（本リポジトリ）

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | 冒頭の旧リポジトリ決定を「旧 X」形式で参照する旨の記述を CT 確定後の表現へ改訂する。統合対象に欠落があれば CZ 以降の新規行を追加する（CX・CY はレビューで見つかった仕様欠落分として登録済み） |
| `docs/requirements.md` | 冒頭の旧リポジトリ案内（凍結の記述と旧番号の参照ルール）と §11 関連ドキュメントの旧決定記録リンクを改訂し、本文中の「旧 X」参照を §4 の対応表に従って置き換える |
| `docs/implementation-plan.md` | 参考実装の行と「移植元の計算コア」「旧リポジトリと同じ慣行」等の記述を、移植完了とアーカイブを踏まえた表現へ改訂する |
| `docs/phases/*.md` | 「旧 X」参照を §4 の対応表に従って置き換える。マージ済み履歴文書のため、文意を変えず参照のみ解決する |
| `AGENTS.md` | 「移植元 Planner の先行資産を参照する」節を自リポジトリ完結の記述へ改訂する |
| `src/EndfieldAicWeb.Infrastructure/Icons/IconResolver.cs` | コメントの旧仕様決定参照を自完結な説明へ置き換える |
| `src/EndfieldAicWeb.Infrastructure/Icons/IconExportPlanner.cs` | コメントの旧仕様決定参照を仕様決定 AA・R 等へ置き換える |
| `src/EndfieldAicWeb.App/Services/IconCatalog.cs` | コメントの旧仕様決定参照を同上で置き換える |
| `src/EndfieldAicWeb.Admin/Services/AdminDocumentService.cs` | コメントの旧仕様決定参照を同上で置き換える |
| `src/EndfieldAicWeb.Domain/Calculation/CalculationWarning.cs` | コメントの旧仕様決定参照を仕様決定 R へ置き換える |
| `src/EndfieldAicWeb.Domain/Calculation/ProductionCalculator.cs` | コメントの旧仕様決定参照を仕様決定 R 等へ置き換える |
| `tests/EndfieldAicWeb.Domain.Tests/WarningTests.cs` | コメントの「旧 TRN-01/02」参照を自完結な説明（撤去済み輸送容量警告の発火構成を再利用した回帰、等）へ置き換える |

## 3. 他リポジトリとリポジトリ外の作業

| 対象 | 作業 |
|---|---|
| `Tkg-tamagohan/Endfield-AIC-Web` 以外の PR | `Tkg-tamagohan/Endfield-AIC-Planner` の README に「本リポジトリは凍結済みで後継は Endfield-AIC-Web」旨の案内を追記する PR を作成してマージする（仕様決定 A の未実施分） |
| GitHub 操作 | Planner の README 追記マージ後に GitHub Archive を実行する。アーカイブはリポジトリ設定の操作であり、権限がなければユーザーへ依頼する |
| セッション環境 | Phase 41 完了後、Web 側セッションで Planner のクローンは不要（仕様決定 CW）。Planner の `.agents/skills/` はアーカイブとともに休眠する |

## 4. 旧決定の新旧対応表

「旧 X」の出典は旧リポジトリ `docs/decision-records.md`（`dev/webification` ブランチ、A〜BY。<https://github.com/Tkg-tamagohan/Endfield-AIC-Planner/blob/dev/webification/docs/decision-records.md>）とし、出典の記載は本節のみに集約する（仕様決定 CT）。
「旧 X」参照の置き換え先を次の表で規定する。
判定の基準は、現行の requirements.md・decision-records.md に内容が保持されているか（包含）、改定・廃止で失効したか（廃止）、Web 版のスコープに入らないか（対象外）の 3 区分である。
「包含」の行は対応する Web 側仕様決定への参照へ置き換え、本文に規則が既に記述されている箇所は「（旧 X 踏襲）」の由来注記を外すだけでよい。
本文に規則がなく旧決定にだけ内容があると判明した場合は、作業者が CZ 以降の新規仕様決定として内容を転記し、本表へ追記する（仕様未記載で実装済みだったアイコン解決規則 2 件は CX・CY として登録済み）。

| 旧 ID | 内容 | 判定 | 置き換え先 |
|---|---|---|---|
| 旧 A | 発電設備のモデル化 | 廃止（仕様決定 Q・Y で発電モデル廃止） | 統合しない。言及箇所は「廃止済みの旧発電モデル」等の自完結表現へ |
| 旧 C | 採取素材の個/分表示 | 包含 | 仕様決定 R・AB |
| 旧 D | 複数目標 | 包含 | 仕様決定 R |
| 旧 I | 既定レシピ選択（VersionAdded 最新） | 包含（F・BA・CL・CP で改定済み） | 仕様決定 F・BA・CL・CP |
| 旧 K | 副産物の充当と循環検出 | 包含（AQ で循環を拡張） | 仕様決定 R・AQ |
| 旧 L | 輸送容量警告 | 廃止（AN で改訂後、BV で撤去） | 統合しない。言及箇所は「撤去済みの輸送容量警告」等へ |
| 旧 P | マスタ更新の全置換 | 包含 | 仕様決定 R・D |
| 旧 Q | DataVersion/SchemaVersion | 包含 | 仕様決定 R |
| 旧 Z | マスタ整合性ガード | 包含（①③④はレイアウト・DB 由来で Web では不成立、②相当のみ有効） | 仕様決定 R（「ペア基準の整合性ガード」） |
| 旧 AG | 有効イベントのコンテキスト指定 | 包含 | 仕様決定 R |
| 旧 AM | アイコンの権利方針 | 包含 | 仕様決定 R・requirements §5.11 |
| 旧 AN | IconKey の対象範囲とレシピのフォールバック | 包含（対象範囲は N で全エンティティへ拡張済み。主出力フォールバックは CX で明文化） | 仕様決定 N・CX |
| 旧 AO | アイコンのアセット配布とマニフェスト | 包含（マニフェスト節・File 規約・ハッシュ一致は R・§5.11。収録外キーのファイル名解決は CY で明文化。配布 zip・キャッシュ・オンライン自動適用等の WPF 固有部分は対象外） | 仕様決定 R・CY・requirements §5.11 |
| 旧 AP | アイコン正規化とキー文字種 | 包含（AA で規格改定済み） | 仕様決定 AA・R・requirements §5.11 |
| 旧 AQ | アイコンの表示範囲と形状 | 包含 | 仕様決定 AL・requirements §5.11 |
| 旧 BA | RecipeFacility のモデル化 | 包含 | 仕様決定 P・requirements §5.7 のモデル定義 |
| 旧 BC | 適格ペアの既定選択 | 包含（U・BT で改定済み） | 仕様決定 U・BT |
| 旧 BE | 需要の複数設備分割禁止 | 包含 | 仕様決定 R |
| 旧 BI | 対象ユーザー | 包含 | requirements §3 の記載（由来注記を外す） |
| 旧 BK | 無料枠超過時の方針 | 包含 | requirements §8 の記載（由来注記を外す） |
| 旧 BN | 計画内のレシピ→設備一意 | 包含 | 仕様決定 R |
| 旧 BP | 発電・非発電ペア混在禁止 | 廃止（発電モデル廃止に伴い失効。R では「整合性ガード」として名残のみ） | 仕様決定 R、または言及の除去 |
| 旧 BQ | 管理ツールのレシピ計算 | 包含（実装済み） | 仕様決定 AG・requirements §7 のワークフロー |
| 旧 TRN-* | 輸送容量警告のテスト系列 | 廃止（Phase 30 で系列ごと撤去） | 統合しない。テストコメントは自完結表現へ |
| その他の旧決定 | レイアウト・配布形式・WPF・作業 DB・Web 化戦略（旧 AR〜BY）等 | 対象外 | 統合しない。本リポジトリから参照されていない |

## 5. 実施順序

1. 本リポジトリ側の参照解消（§2 の全ファイル）を実装 PR として main へマージする
2. Planner 側 README の凍結案内 PR を作成してマージする
3. Planner を GitHub Archive する
4. 完了確認として本リポジトリで §4 の残存参照検査を実行する

旧リポジトリはアーカイブ後も読み取り専用で残るため、手順 1 と 2 の順序は厳密でなくてよい。
ただし README 追記はアーカイブ後に変更できないため、手順 3 の前に必ず完了させる。

## 6. 検証方針

機械検査と目視で確認する（テスト仕様書の MN-184〜）。
`rg "旧 ?[A-Z]{1,2}\b"` で決定 ID 形式の参照が許容区分（§4 の「統合しない」行で意図的に残した表現を除く）に限られること、`Endfield-AIC-Planner` へのリンクが AGENTS.md と正の所在から除去されていること、`dotnet build`・`dotnet test`・`tools/validate_master.py` が通ることを確認する。
文書のみの変更のため xUnit の追加はない。

## 7. 改訂経緯と関連する既知差分

計画 PR（#119）のレビュー指摘を反映した改訂を記録する。

- 仕様未記載で実装済みだったアイコン解決規則 2 件を仕様決定 CX（レシピの主出力フォールバック）・CY（収録外キーのファイル名解決）として追加し、requirements §5.11 を同期、対応表の旧 AN・旧 AO 行を更新した
- CU のアイコン資産の表現を「権利クリアだが自動生成の仮置き画像」へ修正（Phase 7 計画の「自作画像」記述との見かけの齟齬を解消）
- requirements.md 冒頭・§11 を仕様決定 CV・CW・CT 確定後の記述へ同期し、§11 の旧決定リンクは出典を §4 対応表へ集約する表現へ変更
- MN-184 を対応表部分の検査へ分割、MN-186 を置換前ベースラインを基準にする手順へ修正

レビューで発覚した実装と仕様の差分。

- `IconKeyFallback.EffectiveIconKey` は IconKey が空文字のレシピに主出力フォールバックを適用しない（requirements §5.11 は null または空文字を未設定と定義。仕様決定 CX に差分として記録済み）。ユーザー判断で本 Phase の実装で修正する（`!string.IsNullOrEmpty` 判定へ追従＋回帰テスト ADM-18。Admin のアイコン編集ヒントも同一判定へ追従）
- `AdminDocumentService.FetchIconsAsync` は URL 読み込みでマニフェスト収録分のみ取得するため、収録外キーは ZIP 読み込み・公開アプリと結果が異なる（CY は取得済みファイルへの解決規則であり、取得拡張の要否は別途判断）
