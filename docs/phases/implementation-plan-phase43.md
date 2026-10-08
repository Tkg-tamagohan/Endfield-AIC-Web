# Phase 43 実装詳細計画

**対象フェーズ**: Phase 43（Domain/Application の構造整理（移動系）: Application フォルダ分割・SemVersion 移動）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 DC。関連: DD）
**関連ドキュメント**: [test-specification-phase43.md](test-specification-phase43.md)（本 Phase のテスト仕様）、[implementation-plan-phase44.md](implementation-plan-phase44.md)（分割系の後続 Phase）

> 本書は Phase 43 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書 PR（本計画・仕様決定 DC・DD・requirements への反映）を先行し、実装・検証は本書を根拠に別 PR で行う。
> Phase 番号は 43 とする（main の現行最大は Phase 42）。仕様決定は DB の次の採番で DC・DD（Phase 44 と共用）、xUnit の新規採番はなし、手動確認は MN-198 以降を使う（main の現行最大は MN-197。並行セッションの採番衝突に注意して push 前に main を再確認する）。

## 1. スコープ

### 背景と問題

`src/EndfieldAicWeb.Application` はプロジェクトルート直下に 19 ファイルを並べるフラット構成で、Domain（`Models/`・`Calculation/`・`Validation/`）や Infrastructure（`Transfer/`・`Icons/`）が従う「フォルダ＝名前空間の一致」規約に乗っていない。
内容は計算経路系（公開アプリ・共有 UI から使う計算実行・入力構築・計画表示・グラフ構築）とマスタ文書系（主に管理ツールの編集・参照支援）の 2 系統に分かれるが、構造上の区別がなく、新規ファイルの置き場を読み手が推測する必要がある。
また Domain プロジェクトルートには `SemVersion.cs` が孤立しており、モデル型が揃う `Models/` から外れている。

本 Phase はファイルの移動と名前空間・using の追従のみを行う整理で、型・メンバー・挙動の変更を含まない。

### やること

- `EndfieldAicWeb.Application` を `Calculation/`・`PlanView/`・`Graph/`・`MasterEditing/` の 4 フォルダへ分割し、各ファイルの名前空間をフォルダに一致させる（仕様決定 DC。割り当ては §3 の表で固定）
- `src/EndfieldAicWeb.Domain/SemVersion.cs` を `Domain/Models/` へ移し、名前空間を `EndfieldAicWeb.Domain.Models` とする（仕様決定 DC）
- 参照側（`src/` 各プロジェクト・`tests/` 各プロジェクト）の using を新名前空間へ機械的に追従させる
- 生きている文書・スキルのパス参照を grep で照合し、陳腐化した記述を同じ変更で追従させる（対象: `AGENTS.md`・`REVIEW.md`・`docs/requirements.md`・`docs/implementation-plan.md`・`.devin/skills/`・個人プラグインの Endfield 関連スキル。リポジトリ AGENTS.md「構成変更ではスキルの参照を照合する」に従う）
- MN-198〜 の検査を実施する

### やらないこと

- 移動対象ファイルの内容変更。差分は名前空間宣言・using 追加/整理・ファイルパスを記した文書コメントの更新に限定する
- テストプロジェクト（`tests/`）のフォルダ分割とテストクラスの名前空間変更。テスト ID が xUnit 完全修飾名に紐付く帳簿運用のため、テスト側は現行構成を維持する（仕様決定 DC）
- 公開 API・型の完全修飾名以外の変更、クラス分割や責務の再配分。`MasterValidator`・`FlowGraphModelBuilder.AssignRanks` の partial 分割は Phase 44 で行う
- `EndfieldAicWeb.Admin`・`EndfieldAicWeb.App`・`EndfieldAicWeb.SharedUi`・`EndfieldAicWeb.Infrastructure`・`EndfieldAicWeb.Domain`（SemVersion 移動を除く）のフォルダ構成変更。これらは既に規約どおりか、本 Phase の範囲外とする
- 過去の Phase 文書・テスト仕様書内のパス参照の遡及更新（当時の作業記録のため）

## 2. 用語

- **移動系**: ファイルの配置・名前空間の変更のみを行う整理。内容差分は名前空間宣言・using・文書コメントに限定される
- **分割系**: 1 ファイルのクラスを partial ファイルへ切り出す整理。Phase 44 で扱う
- **計算経路系**: 計算の実行・入力構築・スナップショット生成・計画表示・グラフ構築のユースケース群。公開アプリと共有 UI から使われる
- **マスタ文書系**: `MasterDocument` の編集・生成・参照支援のユースケース群。主に管理ツールから使われる

## 3. 変更一覧

### Application のフォルダ割り当て（仕様決定 DC）

新しい名前空間はすべてフォルダ名と一致させる（例: `Calculation/` 配下は `EndfieldAicWeb.Application.Calculation`）。

| 移動先 | ファイル | 関心 |
|---|---|---|
| `Calculation/` | `CalculationService.cs` | 計算実行ユースケース（ペア代替候補の列挙を含む） |
| `Calculation/` | `CalculationInputBuilder.cs` | 計算入力（`CalculationInputs`）の構築 |
| `Calculation/` | `MapSelection.cs` | マップ・イベント選択から入力への反映 |
| `Calculation/` | `MasterSnapshotFactory.cs` | `MasterDocument` → `MasterDataSnapshot` の構築（計算経路の入口） |
| `Calculation/` | `EventAutoActivation.cs` | イベントの自動有効化判定 |
| `PlanView/` | `ResultViewBuilder.cs` | 計画ビュー（調整済み・未調整）の構築 |
| `PlanView/` | `ResultViewText.cs` | 計画表示の文字列生成 |
| `PlanView/` | `PlanViewDefaults.cs` | 計画表示の既定値 |
| `PlanView/` | `AmountUnit.cs` | 期間入力の単位モデル |
| `PlanView/` | `SnapshotLookup.cs` | スナップショットからの表示名・アイコンキー検索 |
| `PlanView/` | `PairOption.cs` | ペア代替選択肢の表示向けモデル |
| `Graph/` | `FlowGraphModelBuilder.cs` | フローグラフモデルの構築 |
| `Graph/` | `FlowGraphModelBuilder.AssignRanks.cs` | グラフのランク割当・層内順序 |
| `MasterEditing/` | `EntityFactory.cs` | エンティティの新規生成（既定値） |
| `MasterEditing/` | `DataVersionBumper.cs` | `DataVersion` の更新 |
| `MasterEditing/` | `MasterReferenceFinder.cs` | マスタ内参照の検索（削除安全確認等） |
| `MasterEditing/` | `RecipeAutoFill.cs` | レシピ入力の自動補完 |
| `MasterEditing/` | `ItemCatalog.cs` | マスタエンティティのカタログ・ピッカー用候補 |
| `MasterEditing/` | `IconKeyFallback.cs` | マスタエンティティのアイコンキー解決 |

### Domain

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.Domain/SemVersion.cs` | `src/EndfieldAicWeb.Domain/Models/SemVersion.cs` へ移動し、名前空間を `EndfieldAicWeb.Domain.Models` とする |

### 参照側（using 追従）

| 対象 | 変更 |
|---|---|
| `src/EndfieldAicWeb.Application/` 内のファイル | 他フォルダの型を参照する場合に対応する `using` を追加する。同一フォルダ内の参照は変更不要 |
| `src/EndfieldAicWeb.Admin/`・`src/EndfieldAicWeb.App/`・`src/EndfieldAicWeb.SharedUi/` | 移動した型への `using EndfieldAicWeb.Application;` を新名前空間へ追従させる |
| `tests/EndfieldAicWeb.*.Tests/`・`tests/EndfieldAicWeb.Testing/` | 同様に using を追従させる。テストクラス自体の名前空間・完全修飾名は変更しない |

### 文書・スキル（実装 PR で追従）

| 対象 | 変更 |
|---|---|
| `AGENTS.md`・`REVIEW.md`・`docs/requirements.md`・`docs/implementation-plan.md`・`.devin/skills/` | 移動対象ファイルのパスを記した記述を grep で洗い出して追従させる（棚卸し時点では直接的なファイルパス参照は見つかっていないが、実施時に再検査する） |
| 個人プラグインの Endfield 関連スキル（`testing-blazor-apps-*`・`fixture-injection-blazor-testing`・`webgpu-blazor-ui-testing` 等） | 同上。見つかった場合はプラグイン側の更新手順（`manage_plugin`）で追従する |

### 文書（文書 PR で反映済み）

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | DC・DD（反映済み） |
| `docs/requirements.md` | §6.2 へのフォルダ構成の追記（反映済み） |
| `docs/implementation-plan.md` | Phase 43・Phase 44 行（反映済み）。実装 PR でチェックを `[x]` にする |

## 4. 実施手順

1. `git mv` で Application の 19 ファイルを §3 の割り当てどおり 4 フォルダへ配置し、各ファイルの `namespace` 宣言をフォルダに合わせて更新する
2. `git mv` で `SemVersion.cs` を `Domain/Models/` へ移し、名前空間を `EndfieldAicWeb.Domain.Models` に更新する
3. `rg 'EndfieldAicWeb\.Application' src tests` で残る参照を洗い出し、using を新名前空間へ追従させる。`dotnet build` が残漏れをコンパイルエラーとして出すので、エラー 0 件を以て追従完了とする
4. 文書・スキルのパス参照を照合する（§3「文書・スキル」）
5. MN-198〜 の検査を実施する

## 5. 検証方針

[test-specification-phase43.md](test-specification-phase43.md) に従う。
本 Phase は移動のみのため新規 xUnit は追加しない。検証は (a) 名前空間とフォルダの一致、(b) 差分が名前空間・using・文書コメントに限定されることの確認、(c) 既存テストの全緑、(d) ブラウザでの煙突確認で構成する。

## 6. 暫定解釈

実装中に変更する場合は本節を更新する。

1. `ItemCatalog`・`IconKeyFallback` は Admin だけでなく `CalculatorPanel`・`IconCatalog`（App）からも参照されるが、関心はマスタ文書の編集・参照支援であるため `MasterEditing/` に置く。フォルダは利用アプリではなく関心で分ける
2. `MasterSnapshotFactory` は `MasterDocument` からスナップショットを構築する計算経路の入口とみなし `Calculation/` に置く
3. `PairOption`・`SnapshotLookup`・`AmountUnit` は計算結果の表示向けモデル・検索とみなし `PlanView/` に置く
4. 過去の Phase 文書・テスト仕様書は当時の作業記録のため、移動によるパス陳腐化があっても遡及更新しない
5. git のリネーム検出を活かすため、移動ファイルの内容変更は名前空間宣言と必要最小限の using に限定し、フォーマットや並び替えは行わない

## 7. 残課題

- `MasterValidator`・`FlowGraphModelBuilder.AssignRanks` の partial 分割は Phase 44 で実施する
- `EndfieldAicWeb.Admin`・`EndfieldAicWeb.App` 内の `Services/`・`Components/` 等の整理は、ファイル増加で視認性が悪化した時点で別 Phase として検討する

## 8. 受け入れ条件

- `EndfieldAicWeb.Application` の全ファイルが 4 フォルダに収まり、名前空間がフォルダと一致する
- `SemVersion` が `EndfieldAicWeb.Domain.Models` にある
- 移動ファイルの内容差分が名前空間宣言・using・文書コメントに限定されている
- テストクラスの完全修飾名が移動前後で不変である
- `dotnet build` と `dotnet test` が全緑で、`tools/validate_master.py` を通過する（データ未変更の回帰確認）
- 公開アプリで計算が実行でき、管理ツールでマスタ JSON の読み込み・計算プレビューが動作する
