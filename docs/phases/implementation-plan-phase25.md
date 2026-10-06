# Phase 25 実装詳細計画

**対象フェーズ**: Phase 25（環境設備の層・同名レシピの説明併記・採取ノード撤去）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 BM〜BO）
**関連ドキュメント**: [test-specification-phase25.md](test-specification-phase25.md)（本 Phase のテスト仕様）、[implementation-plan-phase15.md](implementation-plan-phase15.md)（グラフ導入）、[implementation-plan-phase23.md](implementation-plan-phase23.md)（層割り BF〜BH）

> 本書は Phase 25 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

- 出力を持たない環境の供給設備（散布機）を、その環境を利用する設備のうち最小の層と同じ層に置き、消費アイテムをその設備の直上流へ置く。行内順は利用設備の隣へ寄せる（仕様決定 BM）
- 計算ページでレシピ名を表示する全箇所へ、`Description` 非空時の「名前（説明）」併記を適用する（仕様決定 BN）
- 共通採取ノードと Gathered エッジを廃止し、採取供給のあるアイテムノードを緑色化してメタ行に採取量を併記する（仕様決定 BO）

### やらないこと

- 層割りの基本規則（BF〜BH: Layer0 起点の最長距離・循環の後退エッジ扱い）と容量超過判定（AN・AO）の変更
- 粒子密度（AP）・表示方向（BJ）・領域拡大（BK）・ズーム（BL）の変更
- グラフ以外のレシピ名表示箇所（管理ツールの編集ページ等）への説明併記。対象は共有 `CalculatorPanel` が表示する箇所のみ
- `IsGatherable` 属性や計算本体の採取ロジックの変更（グラフの表示モデルのみ）

## 2. 変更一覧

### Application

| ファイル | 変更 |
|---|---|
| `FlowGraphModelBuilder.cs` | `FlowGraphNodeKind.Gather`・`FlowGraphEdgeKind.Gathered`・`GatherNodeId` を削除。`FlowGraphNode` に `GatheredPerMinute` を追加し、アイテムノードへ採取供給量を保持させる。Gathered エッジ生成と採取ノード生成を撤去。`AssignRanks` を 2 段化し、環境供給設備の終端ノードを利用設備の最小層へ固定してから再層割りする。バリセンター順位で環境設備の先行ノードに利用設備を仮想的に含める |
| `SnapshotLookup.cs` | レシピの表示名に `Description` を併記する拡張（例: `RecipeLabel`）を追加する。`Description` が空のときは従来どおり名前のみ |
| `ResultViewText.cs` | `OptionLabel` と `SupplyText` のレシピ名を新拡張へ切り替える |

### SharedUi

| ファイル | 変更 |
|---|---|
| `Components/CalculatorPanel.razor` | 流量調整ヒント（`@RecipeName(l.RecipeId)`）のレシピ名を新拡張へ切り替える |
| `Components/FlowGraph.razor` | `NodeClass` の Gather 分岐を撤去し、`GatheredPerMinute > 0` のアイテムノードへ `fnode-gather` を付与する。`NodeMeta` の Gather 分岐を撤去し、`ItemMeta` に採取量の併記を追加する |
| `Components/FlowGraph.razor.css` | `.fnode-gather` はそのまま流用（アイテムノードへ掛かるようになるため必要に応じてコメント更新） |
| `wwwroot/js/flow-graph.js` | `EDGE_COLORS` から Gathered 分（末尾要素）を削除する。エッジ種別は列挙の序数で参照するため、末尾削除で既存 index は変わらない |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | BM・BN・BO（本計画で追加済み） |
| `docs/requirements.md` | グラフ項目への BM・BO 反映、レシピ選択項への BN 反映、§12 の解消（済） |
| `docs/implementation-plan.md` | 実装 PR で Phase 25 のチェックを `[x]` にする |
| `docs/remaining-issues.md` | 「ペア選択候補ラベルの重複」を対応済みの項目へ移し、実装 PR へのリンクを付ける |

### 検証手順の追従

| ファイル | 変更 |
|---|---|
| `.devin/skills/`・個人プラグインのグラフ関連スキル | 採取ノード撤去と緑化・環境設備の層変更に伴う DOM 構造と期待動作の記述を追従させる（リポジトリ AGENTS.md の規約） |

## 3. 変更詳細

### 3.1 環境設備の層（BM）

現行は、出力を持たない設備ノード（散布機など）を層割り対象外の「終端」として、最深消費アイテムの直下流（`max(消費アイテムの層) − 1`）に置く。散布機の消費アイテムは他設備に消費されないため Layer0 に固定され、散布機は縦表示で目標行よりさらに上の層に浮く。

`AssignRanks` を次の 2 段構成へ改める。

1. 現行どおり層割りする。終端ノードはこれまでどおり層を持たない。この段階で環境設備の消費アイテムは従来どおり未消費扱い（Layer0）になる
2. 環境設備の終端ノードに層を固定し、その設備を消費者に含めて再層割りする

「利用設備」は、選択ペアの `EnvironmentId` が当該環境と一致するランを持つ設備である。ラン→ペアの対応は既存の `pairByRecipe`（`plan.PairSelections` 由来、`recipe.Facilities` へのフォールバック付き）と同じ経路で引く。消費者の表示ノードはビューに従う。

- 集約表示: 設備ノード `fac:<FacilityId>`
- 台数分表示: 当該ランを占有するユニットノード（`RunShares` のキーにそのラン index を持つユニット）

環境設備側の表示ノードもビューに従う。台数分表示では `DispenserEnvironmentId` が当該環境のユニットノードへ、その環境だけの利用設備の最小層を適用する。集約表示では設備ノードへ、担う全環境の利用設備を合わせた最小層を適用する。

固定した環境設備の層は「利用設備ノードの最小層」とする。利用設備が 0 件の環境は従来規則（最深消費アイテムの直下流）へ退避する。これは防御的経路であり、計算機では散布機台数が稼働中ランの環境にのみ計上されるため実際には到達しない（手組みの計画で検査する）。ランも回す兼用設備は出力を持つため終端ではなく、本規則の対象外として従来どおり層割りする。

再層割りは、固定した環境設備を確定済みの消費者として層の伝播をもう一度回す。各ノードの層は `max(第 1 段の層, 1 + max(環境設備を含む全後続の確定層))` で更新し、収束するまで繰り返す。消費アイテムは環境設備の層+1 以降へ沈み、ガス→散布機のエッジは順方向（上流→下流）を保つ。後退エッジ扱いの循環エッジは第 1 段と同じく層割りに使わない。

退化ケース（環境設備の消費アイテムが利用設備自身の産物である相互依存）は、固定には第 1 段の層を使い、再層割り後に利用設備の層が深くなっても環境設備は動かさないものとする。実マスタに該当データはなく、エッジの順方向は維持される。

行内順序（バリセンター）は、固定した環境設備の先行ノードとして実際の流入元（消費アイテム）に利用設備を仮想的に追加し、利用設備の隣へ並ぶようにする。

### 3.2 同名レシピの説明併記（BN）

レシピの `Description` が空でないとき、レシピ名を「`{Name}（{Description}）`」とする拡張を `SnapshotLookup` に追加する。`Description` が空のレシピは従来どおり名前のみとする。

適用箇所は共有 `CalculatorPanel` が表示するレシピ名の全箇所とする。

- ペア選択候補ラベル（`ResultViewText.OptionLabel`）
- 供給内訳の「レシピ」行と「副産物」行（`ResultViewText.SupplyText`）
- 流量調整のヒント（`CalculatorPanel.razor` の `@RecipeName(l.RecipeId)`）

これにより同名・同設備・同サイクルの複数経路（炭塊の 2 経路: サンドリーフ・芽針）が説明で見分けられる。remaining-issues.md の「ペア選択候補ラベルの重複」を本変更で解消する。

### 3.3 採取ノード撤去と緑化（BO）

共通採取ノード（`GatherNodeId`、全採取素材を束ねる単一ノード）と Gathered エッジを廃止し、採取供給はアイテムノード自身の属性として表示する。

- `FlowGraphNodeKind.Gather`・`FlowGraphEdgeKind.Gathered`・`GatherNodeId`・Gathered エッジ生成ループ・採取ノード生成ブロックを削除する
- `FlowGraphNode` に `GatheredPerMinute` を追加し、アイテムノードへ `ItemRequirement` の `SupplyKind.Gathered` 合計を保持させる
- `NodeClass` で `GatheredPerMinute > 0` のノードへ `fnode-gather` を付与し、従来の採取ノード配色（`#17211A` 背景・`#3A5A42` 枠）を流用する。`IsTarget`・`fnode-alert` との併存は従来のクラス合成どおり
- `ItemMeta` に `採取 {FmtRate(...)}` の併記を追加し、採取ノードが持っていた合計レート表示を補完する
- JS の `EDGE_COLORS` から Gathered 分を削除する

緑化の判定は計画内の採取供給であり、`IsGatherable` でも採取供給のないアイテム（代替レシピで全量を生産する場合等）は緑にしない。採取と生産が併存するアイテムは緑とする。

## 4. 確定した UI 上の判断

実装中に変更する場合は本節を更新する。

1. 環境設備の層固定は「利用設備ノードの第 1 段層の最小値」とし、再層割りで利用設備が深く動いても固定値は追従しない（退化ケース対策。§3.1）
2. 採取量のメタ併記は `採取 X/分` の語形とし、需要・未充足・余剰と同じ `ItemMeta` の部品列に並べる
3. 緑化クラス名は従来の `.fnode-gather` を継続使用し、配色は変えない

## 5. テスト

[test-specification-phase25.md](test-specification-phase25.md) に従う。
層割りとラベルは `FlowGraphModelBuilderTests`・`CalculatorPanelHelperTests` の xUnit で検査し、既存 FG 群の更新を含めて `dotnet test` 全緑を確認する。
描画側（緑化・層位置の見た目）は WebGPU 対応 Chrome と CDP による E2E で行う（検証手順は `webgpu-blazor-ui-testing`・`testing-blazor-apps-flow-graph` の各スキルに従う）。
ブラウザプレビューによるユーザー確認は実装後・手動検査項目の実施前に挟み、フィードバックを検査へ反映する（ui-mock-first ルール、implementation-plan.md §5「UI の確認」に従い、ユーザーが触ってから検査を進める）。

## 6. 受け入れ条件

- 散布機が利用設備と同じ層（複数環境・複数利用設備では最小層）に置かれ、消費アイテムはその直上流に来る。ガス→散布機のエッジは順方向に流れる
- 台数分表示でも散布機ユニットが担当環境の利用ユニットの最小層に置かれる
- 炭塊の 2 経路がペア候補・供給内訳・流量調整ヒントで説明付きで見分けられる
- 採取素材が素材ごとに緑ノードとして見え、採取量がメタ行で読める。採取ノードと Gathered エッジは出ない
- 公開 App と管理ツール計算プレビューの双方で同一動作になる（共有 RCL のため）
- `dotnet build` と `dotnet test` が全緑である
