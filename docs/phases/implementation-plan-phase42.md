# Phase 42 実装詳細計画

**対象フェーズ**: Phase 42（基礎素材の指定： 中間素材の外部調達化とグラフの青表示）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 CZ〜DB。関連: X・F・U・AH・AT・AD・BO・CD）
**関連ドキュメント**: [test-specification-phase42.md](test-specification-phase42.md)（本 Phase のテスト仕様）

> 本書は Phase 42 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書 PR（本計画・仕様決定 CZ〜DB・requirements への反映）を先行し、実装・テストは本書を根拠に別 PR で行う。
> Phase 番号は 42 とする（main の現行最大は Phase 41）。仕様決定は CY の次の採番で CZ〜DB、xUnit は新接頭辞 BAS（BAS-01 以降）と FG-68 以降、手動確認は MN-191 以降を使う（main の現行最大は FG-67・MN-190。並行セッションの採番衝突に注意して push 前に main を再確認する）。

## 1. スコープ

### 背景と問題

生産計画では中間素材（レシピ成果物かつ採取素材でないアイテム）の需要がすべてレシピへ展開され、その上流の設備・電力・環境・採取まで計画に計上される。
実プレイでは「この中間素材は在庫や外部供給で賄うので、その経路を省略した図が欲しい」場面がある。
現行では採取素材（`IsGatherable`）だけが需要展開の終端になるため、中間素材の経路を省略する手段がない。

### やること

- 需要展開で基礎素材指定アイテムを全量外部調達の終端とする計算経路を追加する（仕様決定 CZ）
- 基礎素材指定の外部調達を採取と区別する供給種別を追加し、素材行の供給内訳を「外部調達 N」と表示する（仕様決定 CZ・DB）
- 結果の素材行ごとの基礎素材トグルを追加し、複数指定・再計算またぎの保持・目標重複の自動解除・母集団外の自動破棄を行う（仕様決定 DA）
- 指定アイテムのグラフノードを青系の併用クラスで描き、メタ行に外部調達量を併記する（仕様決定 DB）

### やらないこと

- アイテムごとの調達レート指定（上限付き外部調達）。需要全量を無制限の外部調達とする最小形とし、採取レートと同型の拡張として将来に回す（§8）
- 採取素材・仮想アイテム・目標アイテムへの指定経路。母集団はレシピ成果物かつ非採取のアイテムに限定する（CZ）
- 指定状態のマスタ保存や URL 共有。保持はセッション内に限る（§8）
- 指定アイテムのペア代替ドロップダウン。ペア上書きが効果を持たないため提示自体をしない（CZ）
- 「基礎素材」の行タグや指定一覧節の追加。指定の識別は供給チップとノード色で行う（DB）
- 省略分の内訳表示（畳まれた上流経路の材料一覧など）。省略は計画からの除去とする

## 2. 用語

- **基礎素材**: 素材行から基礎素材に指定されたアイテム。需要展開の終端となり、需要は全量外部調達として計上される（仕様決定 CZ）
- **外部調達（基礎素材由来）**: 基礎素材指定による `Raw` 計上分。採取（`SupplyKind.Gathered`）と同じ外部調達帳簿だが、供給種別を分けて表示上区別する（仕様決定 DB）
- **母集団**: 指定可能な素材行の母集団。レシピ成果物かつ非採取のアイテムで、仮想アイテム（`TransportKind=None`）と目標アイテムを除く（CZ・DA）

## 3. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.Domain/Calculation/CalculationInputs.cs` | `ContextFilter` に基礎素材指定のアイテム Id 集合 `SpecifiedBaseItemIds`（`IReadOnlyCollection<string>`、既定 `[]`）を追加する（CZ）。Calculate 系のシグネチャは変えない |
| `src/EndfieldAicWeb.Domain/Calculation/CalculationSession.cs` | 構築時に指定集合を `HashSet` で保持する（CZ） |
| `src/EndfieldAicWeb.Domain/Calculation/CalculationSession.Expand.cs` | `Expand` で、イベント非有効の検査（X）の後・正味需要の算出の後・採取分岐の前に、指定アイテムを `Raw` へ正味需要を全量計上して return する分岐を追加する（CZ） |
| `src/EndfieldAicWeb.Domain/Calculation/CalculationOutputs.cs` | `SupplyKind` に基礎素材指定の外部調達を表す種別（仮 `ExternalProcurement`）を追加する（DB） |
| `src/EndfieldAicWeb.Domain/Calculation/ProductionPlanAggregator.cs` | `Raw` の `Supplies` 計上を、指定集合に含まれるアイテムは新種別・それ以外は従来の `Gathered` に分ける（CZ・DB） |

### Application

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.Application/CalculationService.cs` | ペア代替候補の列挙で指定アイテムの `ItemRequirement` を除外し、`PairOptionsByItemId` に出さない（CZ） |
| `src/EndfieldAicWeb.Application/ResultViewText.cs` | `SupplyText` で新供給種別を「外部調達 N」と表示する（DB） |
| `src/EndfieldAicWeb.Application/ResultViewBuilder.cs` | 未調整ビューの需要充当に新供給種別も含める（§4-4） |
| `src/EndfieldAicWeb.Application/FlowGraphModelBuilder.cs` | `Build` に基礎素材指定集合の引数を追加し、`FlowGraphNode` に指定フラグと外部調達量のフィールドを追加する（DB） |
| `src/EndfieldAicWeb.Application/CalculationInputBuilder.cs` | 基礎素材候補の判定（母集団・非目標）と保持値の整合（自動破棄・自動解除）の helper を追加する（DA） |

### SharedUi

| ファイル | 変更 |
|---|---|
| `src/EndfieldAicWeb.SharedUi/Components/CalculatorPanel.razor` | 基礎素材指定の集合状態を持ち、対象素材行にトグルを出す。切替で再計算し、指定集合を `ContextFilter` へ載せる（DA） |
| `src/EndfieldAicWeb.SharedUi/Components/FlowGraph.razor` | `NodeClass` に指定ノードの青系クラスを併用し、`ItemMeta` に外部調達量を併記する（DB） |
| `src/EndfieldAicWeb.SharedUi/Components/FlowGraph.razor.css` | 青系の `fnode-*` クラス（仮 `fnode-base`）を追加する。配色は `fnode-gather`・`fnode-disposal` と同型の 2 色（background・border-color）で、具体値は実装後の画面確認で調整する（DB） |

### 文書（本 PR で反映済み）

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | CZ〜DB（本計画で追加済み） |
| `docs/requirements.md` | §2.1・§4.1・§4.9・§10 への反映（済） |
| `docs/implementation-plan.md` | Phase 42 行（済）。実装 PR でチェックを `[x]` にする |

### テスト

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/BaseMaterialTests.cs`（新規） | 指定の計算反映（BAS-01〜） |
| `tests/EndfieldAicWeb.Application.Tests/CalculationServiceTests.cs` | ペア候補の除外（BAS 系として採番） |
| `tests/EndfieldAicWeb.Application.Tests/CalculatorPanelHelperTests.cs` | 「外部調達」表示文字列（BAS 系として採番） |
| `tests/EndfieldAicWeb.Application.Tests/CalculationInputBuilderTests.cs` | 保持値の整合（BAS 系として採番） |
| `tests/EndfieldAicWeb.Application.Tests/UnadjustedViewTests.cs` | 未調整ビューの外部調達充当（BAS 系として採番） |
| `tests/EndfieldAicWeb.Application.Tests/FlowGraphModelBuilderTests.cs` | グラフ表現（FG-68〜） |

## 4. 変更詳細

### 4-1. 需要展開の打ち切り（CZ）

- `ContextFilter` に `SpecifiedBaseItemIds` を追加する。`Calculate` 系はコンテキスト経由で受け渡すため、シグネチャの変更は最小に留める
- `CalculationSession` は構築時に指定集合を `HashSet<string>`（`StringComparer.Ordinal`）で保持する
- `Expand` の分岐順は、イベント非有効の検査（`IsItemInactive`、X 優先）→ 正味需要の算出（`Demand − Produced − Raw − Unmet`、0 以下なら return）→ 基礎素材指定の検査（`Raw[itemId] += net; return`）→ 採取分岐 → 循環検出 → レシピ選択、とする
- 正味需要の算出より後に置くため、副産物充当（`Produced`）を差し引いた残差のみが外部調達になる（CZ の副産物併存規則）。イベント非有効の検査より後に置くため、イベント限定アイテムへの指定は効果を持たず従来どおり未充足＋警告になる
- 指定アイテムは需要展開しないため循環検出（訪問済み集合）に入らず、指定を介した循環は解消する（CZ）
- Domain は受け取った指定集合をそのまま適用し、母集団制約（レシピ成果物かつ非採取）は UI・入力層の責務とする。採取素材が混入した場合は基礎素材の扱い（全量外部調達）を優先する（暫定解釈 1）

### 4-2. 供給種別の分離（CZ・DB）

- `SupplyKind` に基礎素材指定の外部調達を表す種別を追加する。既存の `Gathered`（採取）は据置とし、基礎素材指定と採取のチップを「外部調達 N」「採取 N」で区別する
- `ProductionPlanAggregator.Aggregate` で `Raw` 計上分を指定集合の有無で新種別と `Gathered` に分ける。指定集合は `CalculationSession` が保持するものを引き回す
- 新供給種別の追加で `SupplyKind` の switch 網羅にコンパイラ警告が出る箇所（`ResultViewText`・`ResultViewBuilder`・テスト helper 等）をすべて追従させる
- 現行では `Raw` 計上は採取由来のみのため、指定が空集合のとき計画は従来と同一になる（回帰は BAS で確認）

### 4-3. 素材行トグルと保持（DA）

- `CalculatorPanel` に基礎素材指定の集合状態（`HashSet<string>`）を追加し、`Recalculate` で `ContextFilter.SpecifiedBaseItemIds` へ複製して渡す
- トグル対象は「母集団（レシピ成果物かつ非採取・非仮想）かつ目標でない」素材行とし、それ以外の行はトグルを描かない。判定の helper は `CalculationInputBuilder` 側に置き、`ItemCatalog.WithRecipeOutput` と `IsGatherable`・`TransportKind` で母集団を構成する（AT と同型）
- トグルは素材行の供給チップ・ペア代替と同じブロックに「基礎素材」チェックボックスとして出し、切替で `Recalculate()` を呼ぶ（ペア代替と同じ再計算規則）
- 保持値の整合は AH と同型とする。目標解析・マスタ再読込後に「母集団外」「目標と重複」の値を集合から外し、計画に登場しない指定は保持を継続する（再登場で再適用）
- `CalculationService.Calculate` のペア候補列挙で指定アイテムを除くため、`PairOptionsByItemId` に出ずドロップダウンも出ない。既存のペア上書き入力に指定アイテムのものが残っていても計算へ効果を持たない（警告も出さない）

### 4-4. グラフ表現と未調整ビュー（DB）

- `FlowGraphModelBuilder.Build` に基礎素材指定集合の引数を追加する（`targets` と同じ要領で呼び出し側から渡す）。`FlowGraphNode` に指定フラグ（仮 `IsSpecifiedBase`）と外部調達量（仮 `ExternalProcuredPerMinute`）を追加し、量は `req.Supplies` の新種別合計とする
- 外部調達量を計画の供給種別から導くため、副産物のみで全量賄われた指定アイテムは量 0 のまま指定フラグのみ立つ（暫定解釈 2）
- `FlowGraph.razor` の `NodeClass` に青系クラスを併用する。`ItemMeta` に「外部調達 N」を併記する（`GatheredPerMinute` の採取併記と同型）
- 併用クラスの優先順位は、背景・基本枠色を青が処理の紫（`fnode-disposal`）より優先し、枠色は目標の橙（`fnode-target`）と未充足の赤（`fnode-alert`）がこれらより優先する。基礎素材指定はユーザーの明示的なマークで「指定ノードは青色」が要請の主眼であり、処理消費や併存状態はメタ行と枠色で識別できるためである（暫定解釈 5）。CSS の記述順は `.fnode-disposal` より後・`.fnode-target` より前に `.fnode-base` を置く
- 未調整ビュー（`ResultViewBuilder`）では、採取分を需要の充当へ含める既存の集計（`gatheredByItem`）に新供給種別も含める。含める対象は処理消費のクランプに使う利用可能量と未調整余剰の再計算である。未充足量は計画からそのまま表示されるため本種別の影響はない（暫定解釈 3）
- `fnode-base` の配色は `fnode-gather`（緑）・`fnode-disposal`（紫）と同型の青系 2 色とし、具体値は実装後のブラウザプレビューでユーザーと確認して調整する

### 4-5. Admin プレビュー

- `CalculatorPanel` は公開版・Admin 計算プレビュー共通のため、トグル・供給表示・ノード色は両側へ自動的に出る。管理ツール側の追加作業はない

## 5. 影響の確認

- 指定が空集合のとき計画は従来と同一になる（新規分岐は空集合で不活性）。マスタデータ・スキーマ・Admin の登録画面には触れない
- 指定で消えるのは「指定アイテムを生産する側」の経路のみで、指定アイテムを入力に持つランや処理ランは残る。処理ランの対象アイテムが指定されていても、余剰の処理は従来どおり行われる（供給側の省略と消費側の処理は独立）
- 未調整ビューのラン倍率スケールは新供給種別に影響しない（外部調達はランを持たないため）

## 6. テスト

[test-specification-phase42.md](test-specification-phase42.md) に従う。
計算反映は Domain の xUnit（新接頭辞 BAS）、ペア候補除外・表示文字列・保持値整合は Application の既存テストファイルへ、グラフ表現は Application のモデル検査（FG）で検査する。UI のトグル・配色・メタ行はブラウザ E2E（MN-191〜）で確認する。
ブラウザプレビューによるユーザー確認は実装後・手動検査項目の実施前に挟み、フィードバックを検査へ反映する（ui-mock-first ルール、implementation-plan.md §5「UI の確認」に従う）。

## 7. 暫定解釈

実装中に変更する場合は本節を更新する。

1. Domain は受け取った指定集合をそのまま適用し、母集団制約（非採取・レシピ成果物・非目標）は UI・入力層の責務とする。採取素材が混入した場合は基礎素材の扱い（全量外部調達、採取上限の適用なし）を優先する
2. 副産物のみで全量賄われた指定アイテムは、外部調達量 0 のまま指定フラグのみ立つ。ノードは青系を維持し、メタ行の外部調達併記は量が 0 のとき出さない形を許容する（最終形はブラウザプレビューで確認）
3. 未調整ビューでは新供給種別を採取と同じく充当へ含め、処理消費のクランプに使う利用可能量と未調整余剰の再計算へ反映する（未充足量は計画からそのまま表示されるため対象外）
4. 素材行のトグルは供給チップ・ペア代替と同じ行ブロック内に置く。配置の詳細はブラウザプレビューで確認する
5. ノードの併用クラスでは基礎素材の青が処理の紫より優先され、枠色の橙（目標）・赤（未充足）はそれらより優先される。供給色が状態色に譲られる採取（緑は紫に譲られる）との対称では紫優先も選択肢だが、「指定ノードは青色ハイライト」の要請が主眼であるため青優先とした。処理消費はメタ行で識別できる

## 8. 残課題

- アイテムごとの調達レート指定（上限付き外部調達、超過分のレシピ展開）。採取レート（AE）と同型の拡張として検討余地があるが、需要全量の調達に限定した本 Phase では持たない
- 指定状態の保存・共有（URL パラメータやマスタへの保存）。現行はセッション内保持のみ

## 9. 受け入れ条件

- 中間素材の素材行で基礎素材を指定すると、そのアイテムへの合成経路（上流のラン・設備・環境要件・採取）が計画から外れ、素材行が「外部調達 N」表示になる
- 複数のアイテムを指定でき、解除で元の経路へ戻る
- グラフで指定アイテムのノードが青系になり、上流のエッジ・設備ノードが消え、メタ行に「外部調達 N」が出る
- 目標・採取素材の行にはトグルが出ず、指定が目標と重なったら自動解除される
- 指定アイテムのペア代替ドロップダウンが出ない
- `dotnet build` と `dotnet test` が全緑で、`tools/validate_master.py` を通過する（データ未変更の回帰確認）
