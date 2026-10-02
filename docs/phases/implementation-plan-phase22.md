# Phase 22 実装詳細計画

**対象フェーズ**: Phase 22（公開版と管理ツールの計算ページ UI の共有化）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AG、AI、AK、AS〜BA ほか）
**関連ドキュメント**: [remaining-issues.md](../remaining-issues.md)「公開版と管理ツールの計算ページ UI の共有化」、[implementation-plan-phase13.md](implementation-plan-phase13.md)（コピー追従の当時方針）、[test-specification-phase22.md](test-specification-phase22.md)（本 Phase のテスト仕様）

> 本書の判断事項はユーザーと協議済みであり、§2 のとおり確定した。
> 仕様決定 ID は BB〜BE で採番する（Phase 21 の仕様決定 BA 確定後の連番）。
> Phase 21 のマージを確認し、decision-records.md・requirements.md・implementation-plan.md への反映（§8）とあわせて本書とテスト仕様書をコミットする。

## 1. 背景と再検討の根拠

残課題「公開版と管理ツールの計算ページ UI の共有化」の再検討条件は「差分の取りこぼしや修正の二重化が実害として現れた時点」とされている。
現状はこの条件に相当すると判断する。

- 公開版 `src/EndfieldAicWeb.App/Pages/Home.razor` と管理ツール `src/EndfieldAicWeb.Admin/Pages/PreviewPage.razor` は、有効イベント・採取マップ・単位切替・素材・採取・環境・設備・消費電力・余剰の各節と、その裏側の状態機械（`Recalculate`・`RebuildView`・約 30 のハンドラとヘルパー）をほぼ同一に別実装している。重複は両者の約 6 割に及ぶ。
- Phase 19（仕様決定 AS・AT）では同一仕様を両ページへ別の形で実装し、Phase 20（仕様決定 AU）で公開版のコンボを廃止してネイティブ select 二段へ揃え直した。同一機能を 2 度にわたり二重実装・二度修正しており、取りこぼしの構造が実在する。
- Phase 20 の改修で Application の `ItemSearch`（コンボの検索補助）は参照箇所を失い、呼び出し元のないロジックとそのテストだけが残った。ページ単位の改修が共有ロジックの管理まで追えないことの実害である。
- 表示用語が分岐した。採取マップ節のヒントは公開版が「採取素材」、管理ツールが「天然資源」である。
- 生産フローグラフは公開版のみに存在し、管理ツールへの展開は仕様決定 AI で「別 Phase」として積み残されている。
- スタイルも `App/wwwroot/css/app.css` と `Admin/wwwroot/css/app.css` に重複して存在し、`.combo` 系は公開版のみ（Phase 20 で実体は消滅）、`.input.num` 等は管理ツールのみと片寄り始めている。

このため、仕様決定 AG の「機能移植はコピー追従、razor 共有化は見送り」を改め、Razor Class Library 化による共有を行う。

## 2. 確定した判断（2026-10-02 協議確定）

| 採番 | 項目 | 決定 |
|------|------|------|
| BB | 共有の単位と形 | 計算パネル全体を 1 コンポーネントとして共有 Razor Class Library へ移す。両ページはデータ読み込みのゲートのみを持つ薄い殻にする（AG の見送りを改訂） |
| — | 目標アイテム選択の統一先 | 仕様決定 AU で統一済み。共有パネルは Phase 20 のネイティブ select 二段（カテゴリ＋アイテム）をそのまま採用し、新規の決定は不要 |
| BC | 管理ツールへのグラフ展開 | 有効にする。本 Phase が仕様決定 AI の「別 Phase」にあたる |
| BD | 表示用語の統一 | 共有 UI の表示文言は「天然資源」に統一する。マスタ JSON の `Category` 値と Domain の警告メッセージは従来どおり「採取素材」のままとし、改訂は UI の表示文言に限る |
| BE | 計算失敗時の扱い | 共有パネルでは `Calculate` を常に捕捉して入力エラー欄へ表示する（公開版でも同じ防御を適用する） |
| — | PR の分け方 | 2 本に分ける（22-1: 挙動不変の移設、22-2: 差異の統一と Admin グラフ解放）。作業方針であり仕様決定には登録しない |

decision-records.md への登録文は次のとおり（採番確定済み）。

- BB: 計算ページ UI の共有化。公開版と管理ツールの計算ページを共有 Razor Class Library（`EndfieldAicWeb.SharedUi`）の単一コンポーネントで実装する。AG のコピー追従方針は、差分の取りこぼしと修正の二重化が実害として現れたため改訂する。ページ側はデータ読み込みのゲートのみを持つ。
- BC: 管理ツールへのフローグラフ展開。管理ツールの計算プレビューでも生産フローグラフを表示する。AI が積み残した「別 Phase」に本 Phase を充てる。
- BD: 計算ページの表示用語統一。採取マップ節ヒント等の表示文言を「天然資源」に統一する。マスタ JSON の Category 値と警告メッセージは「採取素材」のままとし、UI の表示文言のみを改訂する。
- BE: 計算失敗時の扱い。共有計算パネルは計算実行を常に捕捉し、例外は入力エラー欄へ表示する。公開版でも同じ防御を適用する。

## 3. スコープ

### やること

- 共有 Razor Class Library `src/EndfieldAicWeb.SharedUi`（仮名、SDK は `Microsoft.NET.Sdk.Razor`、net8.0）を新設し、App・Admin 両プロジェクトから参照する
- 計算ページ本体を `Components/CalculatorPanel.razor`（仮名）として共有ライブラリへ移す。対象は入力パネル（生産リスト・有効イベント・採取マップ・計算ボタン）、結果パネル（ツールバー・警告・素材・採取・環境・設備・消費電力・余剰）、および裏側の状態機械（`TargetRow`・`EventCheck`・`EnvInput`・`GatherInput`・`Recalculate`・`RebuildView`・グラフ管理）である
- `EntityIcon`（両アプリに同名で存在するもの）を共有ライブラリへ一本化する
- `FlowGraph.razor` と `wwwroot/js/flow-graph.js` を共有ライブラリへ移す。JS の import パスは `./_content/EndfieldAicWeb.SharedUi/js/flow-graph.js` とする
- 共有コンポーネントのスタイルは CSS isolation（`*.razor.css`）へ移し、両 `app.css` から共有分の定義を除去する
- 差異の統一: 管理ツールでグラフを有効化（BC）、表示文言を「天然資源」へ統一（BD）、`Calculate` の常時捕捉（BE）、アイテム選択肢の「（イベント）」注記を公開版に揃える
- Phase 20 で参照を失った `ItemSearch`（Application）と `ItemSearchTests` を削除する

### やらないこと

- 編集系ページ（Items・Recipes・Environments・Maps・Events・Facilities）の共有化。計算ページのみを対象とする
- 管理ツールの `ItemPicker`・`RefSelect`・`SelectOption` の廃止。編集系ページで継続して使う。共有パネルへ移すかどうかは実施時の判断とする（§6 暫定解釈）
- Application・Domain・Infrastructure 層の機能変更。共有に必要なロジック（`CalculationInputBuilder`・`ItemCatalog`・`MapSelection`・`PlanViewDefaults`・`ResultViewBuilder`・`FlowGraphModelBuilder`・`IconKeyFallback`）は Application に既にある
- `data/master.json`・スキーマ・警告メッセージの変更
- bUnit 等のコンポーネントテスト基盤の導入（remaining-issues.md の別項目として引き続き見送り）
- `PageTitle`・ナビ・レイアウトなどページ外周の共通化。各アプリに残す

## 4. 構成とシーム

### 4.1 プロジェクト

```
src/
├── EndfieldAicWeb.SharedUi/          # 新設（RCL）。Blazor コンポーネントと計算ページの UI
│   ├── Components/CalculatorPanel.razor
│   ├── Components/EntityIcon.razor
│   ├── Components/FlowGraph.razor
│   ├── ICalculatorIcons.cs           # アイコン解決の抽象（仮名）
│   └── wwwroot/js/flow-graph.js
├── EndfieldAicWeb.App/               # Pages/Home.razor は読み込みゲートのみに縮小
└── EndfieldAicWeb.Admin/             # Pages/PreviewPage.razor は読み込みゲートのみに縮小
```

`SharedUi → Application` の参照を追加し、App・Admin は `SharedUi` を参照する（既存の Application・Infrastructure 参照は維持）。
CSS isolation のバンドルは両アプリの `index.html` で既に `<App>.styles.css` が読み込まれているため、共有コンポーネントの `*.razor.css` はそのまま収集される。
`flow-graph.js` は RCL の静的アセットとして配信され、両アプリとも `./_content/EndfieldAicWeb.SharedUi/js/flow-graph.js` で import する。

### 4.2 ホストとのシーム

共有パネルが必要とするものは、パラメータと DI で注入する。

| 依存 | 受け渡し方法 | App 側 | Admin 側 |
|------|-------------|--------|----------|
| マスタスナップショット | `SnapshotSource`（`Func<MasterDataSnapshot?>` 相当のパラメータ）を再計算の先頭で呼ぶ | `MasterDataService.Snapshot` を返す | `EnsureSnapshot()`（検証＋再構築）を実行し `_snapshot` を返す |
| アイコン解決 | `ICalculatorIcons`（仮名。`Url(iconKey)` と `RecipeIconKey(recipeId)`）を DI で注入 | `IconCatalog.Url`・`IconCatalog.RecipeIconKey` | `AdminDocumentService.IconDataUrl`・`AdminDocumentService.EffectiveIconKey` |
| 計算実行 | `CalculationService`（既存の共有 DI） | 同左 | 同左 |
| フッター・ヒント文 | 文字列パラメータ（`VersionLabel`・`HeaderHint` 等、仮名） | 「データ版 @Document.DataVersion」 | 「データ版 … ・ SourceLabel」「編集中データで計算します。」 |
| 検証失敗の通知 | `OnSnapshotRejected`（仮名の `EventCallback`）。`SnapshotSource` が null を返したときパネルが発火し、ページへ再描画を委ねる | 発生しない前提（同梱マスタはロード済み） | `EnsureSnapshot` 失敗時に `_snapshotErrors` を保持したままページのゲートを再表示する |
| グラフ表示 | 常に有効（BC）。`FlowGraph` は共有ライブラリ内にあり差異パラメータは設けない | 現行どおり | 本 Phase で有効化 |

読み込み前のゲートは各ページに残す。
App 側は `MasterData.IsLoaded` の分岐（読み込み中・エラー一覧）を、Admin 側は `Store.IsLoaded` の案内と `_snapshotErrors` の `ValidationErrorList` をそのまま持ち、ゲートを抜けた先で `<CalculatorPanel>` を配置する。

## 5. 変更の内容

### 5.1 PR 22-1（挙動不変の移設）

挙動を変えずに共有化だけを行う段階。
差異が残る要素は、22-2 までの間はパラメータで吸収する。

1. `EndfieldAicWeb.SharedUi` を作成し、ソリューションと両プロジェクトの参照を追加する。
2. `CalculatorPanel.razor` を公開版 `Home.razor` の構造を基に共有化する。アイテム選択は Phase 20 のネイティブ select 二段（カテゴリ＋アイテム、`ItemCatalog.OptionsForSelection` による絞り込みと `@key` による再生成）、レシピアイコンは `ICalculatorIcons`、計算呼び出しは BE（常時捕捉）に従う。
3. 共有 `EntityIcon.razor` を作成し、両アプリの同名コンポーネントを置き換える（Admin 編集系ページも共有版へ追従）。
4. `FlowGraph.razor`・`flow-graph.js` を共有ライブラリへ移し、import パスを `_content/` 形式へ直す。
5. 計算パネル専用の共有スタイルを `*.razor.css` へ移し、両 `app.css` から該当分の定義を除去する。`.input`・`.btn` 等の汎用クラスは編集ページでも使うため残す（§6 暫定解釈）。
6. 両ページを読み込みゲートのみへ縮小する。`@code` 内の計算状態・ハンドラ・ヘルパーは全てパネル側へ移す。

### 5.2 PR 22-2（差異の統一と Admin グラフ解放）

確定した判断に従って差異を解消する段階。

1. 管理ツールでグラフを有効化する（BC）。`data-flow-ref` によるグラフノード→リスト行のスクロールもそのまま効く。
2. 表示用語を「天然資源」へ統一する（BD）。公開版の採取マップ節ヒントと該当する表示文言を改める。
3. アイテム選択肢の「（イベント）」注記を公開版に揃える。Admin 側の選択肢にも同じ注記を付ける。
4. 計算失敗時の扱いを BE で統一する（22-1 で既に共通実装となるため、主に確認作業）。
5. ヒント文・フッターの表現を確定する。
6. `ItemSearch` と `ItemSearchTests` を削除する。

## 6. 暫定解釈

- `TargetRow` の初期レート文字列は `"30"` とする（仕様決定 AZ の継承）。
- 採取節の表示単位は常に個/分とし、単位切替の対象外とする（Phase 12・13 の暫定解釈を共有実装へ継承）。
- イベント既定の再評価（`RefreshEventViews`）は再計算のたびに当日で行う（仕様決定 Z の継承）。
- 環境・採取レートの入力保持と整合処理（仕様決定 AH・AD・AE）は現行ロジックをそのまま共有化する。
- ページ遷移時の UI 状態（行・チェック・保持値）は従来どおり破棄され再初期化される。永続化は本 Phase の対象外。
- Admin の `EnsureSnapshot` は再計算の先頭で必ず呼ぶ（現行どおり）。共有パネル側の `SnapshotSource` が null を返した場合は再計算を中断し、`OnSnapshotRejected`（仮名の `EventCallback`）を発火してページへ通知する。子コンポーネントのイベント処理だけではページ側のゲートは再描画されないため、通知を受けたページが自身の `_snapshotErrors` ゲート（`ValidationErrorList`）を再表示する仕組みとする。
- CSS isolation（`*.razor.css`）へ移すのは計算パネル専用の規則に限る。`.input`・`.btn`・`.warn` など編集ページでも使う汎用クラスは両 `app.css` に残し、共有 CSS の配信（`_content/` 経由）は本 Phase の対象外とする。分離クラスは当該コンポーネントのマークアップにのみ効くため、`EntityIcon`・`FlowGraph` はそれぞれ自身の `*.razor.css` を持つ。
- コンポーネント横断の規則は `::deep` か所有側の `*.razor.css` へ振り分ける。`.fnode .icon-slot` はノード内の `EntityIcon` のマークアップを指すため `FlowGraph.razor.css` で `.fnode ::deep .icon-slot` とする。`.flow-flash` は `scrollToRef` がパネルのリスト行（`data-flow-ref`）へ付与するクラスであり `CalculatorPanel.razor.css` へ置く。`::deep` でも届かない規則が出た場合はグローバルの `app.css` へ残し、その判断を個別に記録する。
- `data-flow-ref`（グラフノード→リスト行のスクロール）は共有実装内で常時付与する。Admin でグラフを有効化すればそのまま効く。
- 共有 `EntityIcon` は DI のアイコン解決を使うため、Admin 編集系ページを含め全箇所で同じコンポーネントを使える。
- 共有パネルの生産リスト行は Phase 20 のネイティブ select マークアップをそのまま持ち込む。`ItemPicker`・`RefSelect` 自体を共有ライブラリへ移して計算行でも使う案もあるが、編集系ページへの波及が大きいため本 Phase では行マークアップの維持を優先する。
- Phase 番号・仕様決定 ID・テスト ID は Phase 21 マージ後の main に合わせて確定した（BB〜BE・MN-72〜）。実施中にさらに番号が進んだ場合は既定手順でずらす。
- 22-2 で確定した差異の扱い: `EnableFlowGraph`・`EventItemSuffix`・`MapHint`・`EventSectionHint` の各パラメータは撤去し、グラフ表示・「（イベント）」注記・採取マップ節ヒント・イベント節ヒントは両アプリ共通の共有実装とする。`HeaderHint`（「編集中データで計算します。」の有無）と `VersionLabel`（出典ラベルの有無）はアプリ差が残るためパラメータとして維持する。

## 7. テスト方針

- 本 Phase で新規の純粋ロジックは原則として発生しない（既存 Application 関数の再利用）。発生した場合は Application へ抽出し、xUnit＋ID 採番でカバーする。
- `ItemSearch` の削除に伴い `ItemSearchTests` も削除する。それ以外の既存テストは変更しない前提とし、壊れた場合は原因を切り分けて報告する。
- UI コンポーネント自体のテスト基盤（bUnit）は引き続き導入しない（remaining-issues.md の別項目）。
- 検証はブラウザプレビューでのユーザー確認を正とする（ui-mock-first）。手動確認項目は `test-specification-phase22.md` を参照。
  - 22-1: 公開版・管理ツールともに、既存の主要フロー（目標入力→計算→ペア選択・散布機台数・採取レート入力・単位切替・期間入力・グラフ表示）が移設後も同じ動きをすることを確認する。
  - 22-2: 統一後の UI（Admin 側グラフ・表示用語・イベント注記）を確認する。
- `dotnet test` 全緑と `~/.venvs/validate/bin/python tools/validate_master.py` 通過を確認する。
- CI（build-test）が両プロジェクトと共有ライブラリのビルドに通ることを確認する。

## 8. 作業順序

1. 採番を確定し（BB〜BE・MN-72〜80）、decision-records.md へ §2 の決定文を登録、requirements.md を同期（§4.1・§6.1・§6.2・§10）、implementation-plan.md に Phase 22 の節を追加（`[ ]` のまま）、remaining-issues.md の当該項目へ計画済みの注記を入れる。本書・テスト仕様書とあわせて文書 PR を作成する（本 PR）。
2. PR 22-1 の実装とプレビュー確認。
3. PR 22-2 の実装とプレビュー確認。
4. `implementation-plan.md` の Phase 22 節を `[x]` とし、`remaining-issues.md` の当該項目へ対応済みの PR リンクを記入する。

## 9. 受け入れ条件

- 公開版と管理ツールの計算ページが同一の共有コンポーネントで描かれ、両者の既存機能が動作する。
- 管理ツールの計算プレビューで生産フローグラフが表示・操作できる。
- プレビューでのユーザー確認を経て、ブラウザ（デスクトップ・スマホ幅）で主要フローが両アプリで動作する。
- `dotnet test` 全緑。`tools/validate_master.py` 通過。CI 緑。
