# Phase 44 実装詳細計画

**対象フェーズ**: Phase 44（Domain/Application の構造整理（分割系）: MasterValidator・FlowGraphModelBuilder.AssignRanks の partial 分割）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 DD。関連: DC）
**関連ドキュメント**: [test-specification-phase44.md](test-specification-phase44.md)（本 Phase のテスト仕様）、[implementation-plan-phase43.md](implementation-plan-phase43.md)（移動系の先行 Phase）

> 本書は Phase 44 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書 PR（本計画・仕様決定 DC・DD・requirements への反映）を先行し、実装・検証は本書を根拠に別 PR で行う。
> Phase 番号は 44 とする（main の現行最大は Phase 42、43 は Phase 43 文書で採番済み）。仕様決定は Phase 43 の DC・DD を共用、xUnit の新規採番はなし、手動確認は Phase 43 の採番に続く値を使う（並行セッションの採番衝突に注意して push 前に main を再確認する）。

## 1. スコープ

### 背景と問題

リポジトリには「複数の関心を持つ大きいクラスは partial class で関心ごとのファイルに分割する」慣行が既にある（`CalculationSession.{Counts,Cycles,Disposal,Expand}.cs`・`FlowGraphModelBuilder.{,AssignRanks}.cs`・`MasterJsonReader.{,Validation}.cs`）。
その慣行に乗っていない最大の未分割ファイルが 2 件ある。

- `src/EndfieldAicWeb.Domain/Validation/MasterValidator.cs`（734 行）: 6 エンティティのフィールド内検証・文脈検証・共通ヘルパーが 1 ファイルに同居する
- `src/EndfieldAicWeb.Application/FlowGraphModelBuilder.AssignRanks.cs`（904 行、全コードベース最大）: ランク割当（層の確定）とランク内順序付け（掃引・交差数計測・入替後処理）の 2 フェーズが 1 ファイルに同居する

本 Phase はこれらを既存慣行どおり partial ファイルへ verbatim 分割し、仕様決定 DD で分割規約を明文化する。型・メンバー・挙動の変更を含まない。
本 Phase は Phase 43（Application フォルダ分割）のマージを前提とし、以降のパス記述は移動後の構成に従う。

### やること

- `MasterValidator` を §3 の割り当てどおり対象エンティティ別の partial ファイルへ verbatim 分割する（仕様決定 DD）
- `FlowGraphModelBuilder.AssignRanks` を §3 の割り当てどおり層割当・層内順序の partial ファイルへ verbatim 分割する（仕様決定 DD）
- MN-205〜 の検査を実施する（MN の採番は Phase 43 に続く番号。Phase 43 未実施で本 Phase を先に行う場合は MN-198 以降を使い、文書を更新する）

### やらないこと

- 分割するメンバーの内容変更（シグネチャ・本体・コメントの改訂を含まない verbatim 移動）
- 名前空間・クラス名・公開 API・型の完全修飾名の変更
- メソッド間の責務再配分・リネーム・重複ロジックの共通化
- Phase 43 のフォルダ分割（先行 Phase のスコープ）

## 2. 用語

- **verbatim 分割**: 宣言・メソッド・コメントを一字も変えずに partial ファイルへ移す分割。検証は (a) クラス直下のメンバー単位でブロック分割してソート比較し、メンバー内の行順を含めて一致を確認する方法と、(b) 差分行の多重集合比較で移動行が相殺されて差分が partial 化に伴う行（using・名前空間・クラス宣言・閉じ括弧）のみになることの確認、で行う（MN-205〜206）
- **層割当（Layers）**: `AssignRanks` の前段。ノードのランク（層）を初期層伝播・循環層割当・環境固定で確定するフェーズ
- **層内順序（Order）**: `AssignRanks` の後段。確定したランク内のノード順序を双方向バリセンター掃引・交差数計測・隣接ペア入替後処理で決めるフェーズ（仕様決定 CR・CS）

## 3. 変更一覧

### Domain

| ファイル | 内容 |
|---|---|
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.cs` | クラス宣言・`ValidateAll`・共通ヘルパー（`CheckCommonFields`・`CheckGameEventRef`・`CheckIconKey`・`RequireNonEmpty`・`RequireNoWhitespace`・`RequireEnum`・`EnsureUniqueIds`）・汎用検査 `ValidateRecipeItems<T>` を残す |
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.Item.cs`（新規） | `ValidateItem` |
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.Facility.cs`（新規） | `ValidateFacility` |
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.Environment.cs`（新規） | `ValidateEnvironment` |
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.GameEvent.cs`（新規） | `ValidateGameEvent` |
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.Recipe.cs`（新規） | `ValidateRecipe`・`ValidatePairs` |
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.GameMap.cs`（新規） | `ValidateGameMap` |
| `src/EndfieldAicWeb.Domain/Validation/MasterValidator.Context.cs`（新規） | `ValidateGameMapInContext`・`ValidateRecipeInContext` |

各分割ファイルは `partial class MasterValidator` の宣言とファイル先頭の using を共有し、メンバーは元ファイルでの記述順を維持して配置する（差分の読みやすさのため）。

### Application（Phase 43 マージ後のパス。未マージの場合は移動前パスに読み替える）

| ファイル | 内容 |
|---|---|
| `src/EndfieldAicWeb.Application/Graph/FlowGraphModelBuilder.AssignRanks.cs` | `AssignRanks` エントリ（本体）と隣接構築（`Preds`/`Succs` の組立）を残す |
| `src/EndfieldAicWeb.Application/Graph/FlowGraphModelBuilder.AssignRanks.Layers.cs`（新規） | `PropagateInitialLayers`・`MaxAssignedSucc`・`AssignCycleLayers`・環境固定/`VirtualPreds` 構築 |
| `src/EndfieldAicWeb.Application/Graph/FlowGraphModelBuilder.AssignRanks.Order.cs`（新規） | `OrderNodesWithinRanks`・`FindBestOrderByRank`・`ApplyAdjacentPairSwaps`・`RefreshOrder`・`InsertDispensers`・`Position`・`BarycenterKey`・`RunPass`・`OrderAtRank`・`CountCrossings`・`TargetOutputGap`・`SnapshotOrder` |

メンバーは元ファイルでの記述順を維持して配置する（差分の読みやすさのため）。

### 文書（文書 PR で反映済み）

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | DC・DD（反映済み） |
| `docs/requirements.md` | §6.2 への追記（反映済み） |
| `docs/implementation-plan.md` | Phase 44 行（反映済み）。実装 PR でチェックを `[x]` にする |

## 4. 実施手順

1. `MasterValidator` の各 `Validate*` メソッドを対象エンティティ別の partial ファイルへ verbatim で移す。ファイル先頭の `using` は各ファイルへ必要分を複写する
2. `FlowGraphModelBuilder.AssignRanks` のメソッド群を `AssignRanks.Layers.cs`・`AssignRanks.Order.cs` へ verbatim で移す
3. `dotnet build` が通ることを確認し、MN-205〜 の検査を実施する

## 5. 検証方針

[test-specification-phase44.md](test-specification-phase44.md) に従う。
本 Phase は verbatim 分割のみのため新規 xUnit は追加しない。検証は (a) メンバーブロックが verbatim で一致すること（行順を含む）、(b) 公開 API・修飾名が不変であること、(c) 既存テストの全緑で構成する。

## 6. 暫定解釈

実装中に変更する場合は本節を更新する。

1. `ValidateRecipeItems<T>` は `ValidateRecipe`・`ValidateGameMap` の双方から使われる汎用検査のため、対象別ファイルではなく共通ヘルパーと同じ主ファイルに残す
2. `InsertDispensers`（散布機の隣接挿入、仕様決定 BM）は順序リストへの挿入操作のため `AssignRanks.Order.cs` に置く
3. 分割ファイルは `Class.Concern.cs` の既存命名規約に従う（`CalculationSession.Expand.cs`・`FlowGraphModelBuilder.AssignRanks.cs` と同型）

## 7. 残課題

- 残りの大きいファイル（`FlowGraphModelBuilder.cs` 590 行・`ResultViewBuilder.cs` 464 行・`MasterJsonReader.Validation.cs` 455 行）は単一関心のファイルとみなせるため今回は対象外とする。分割が必要になった時点で別途検討する

## 8. 受け入れ条件

- §3 の割り当てどおり分割され、クラス直下のメンバーブロック集合が分割前後で一致する（行順を含む verbatim 移動）
- 公開 API・型の完全修飾名・メンバー集合が分割前後で不変である
- `dotnet build` と `dotnet test` が全緑で、`tools/validate_master.py` を通過する（データ未変更の回帰確認）
