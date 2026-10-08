# Phase 44 テスト仕様

**対象フェーズ**: Phase 44（Domain/Application の構造整理（分割系）: MasterValidator・FlowGraphModelBuilder.AssignRanks の partial 分割）
**前提ドキュメント**: [implementation-plan-phase44.md](implementation-plan-phase44.md)、[decision-records.md](../decision-records.md)（仕様決定 DD）
**関連ドキュメント**: [implementation-plan-phase43.md](implementation-plan-phase43.md)・[test-specification-phase43.md](test-specification-phase43.md)（移動系の先行 Phase）

> 本書は Phase 44 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 手動確認は Phase 43 の採番に続く番号（Phase 43 が MN-198〜MN-204 を使う前提で MN-205 以降。Phase 43 未実施で本 Phase を先に行う場合は MN-198 以降を使い本書を更新する）。xUnit の新規採番はなし（verbatim 分割のみで挙動は不変）。push 前に main で再確認する。

## 1. 機械検査（コマンド）

| ID | 対象 | 内容 | 期待 |
|---|---|---|---|
| MN-205 | MasterValidator 分割の verbatim 性 | `src/EndfieldAicWeb.Domain/Validation/` で `MasterValidator.cs`・`MasterValidator.Item.cs`・`MasterValidator.Facility.cs`・`MasterValidator.Environment.cs`・`MasterValidator.GameEvent.cs`・`MasterValidator.Recipe.cs`・`MasterValidator.GameMap.cs`・`MasterValidator.Context.cs` をこの順で `cat` し、`git show main:src/EndfieldAicWeb.Domain/Validation/MasterValidator.cs` と `diff` する | 差分なし。分割は using 複写と `partial` 宣言の共有のみを伴う verbatim 移動である（`partial` 化に伴う宣言行の差分は許容し、許容した差分を結果表へ記録する） |
| MN-206 | AssignRanks 分割の verbatim 性 | `src/EndfieldAicWeb.Application/Graph/`（Phase 43 未マージ時は `src/EndfieldAicWeb.Application/`）で `FlowGraphModelBuilder.AssignRanks.cs`・`FlowGraphModelBuilder.AssignRanks.Layers.cs`・`FlowGraphModelBuilder.AssignRanks.Order.cs` をこの順で `cat` し、`git show` の元ファイルと `diff` する | 差分なし（MN-205 と同じ許容基準） |
| MN-207 | 公開 API・修飾名の不変 | 実施前後で `rg '^\s*(public|internal)\s' src/EndfieldAicWeb.Domain/Validation/MasterValidator*.cs src/EndfieldAicWeb.Application*/FlowGraphModelBuilder.AssignRanks*.cs | sort` を比較する | public・internal メンバーの宣言集合が分割前後で一致する。クラス・名前空間が不変（partial 化のみ） |
| MN-208 | メンバーの配置割り当て | `rg -l 'ValidateItem' src/EndfieldAicWeb.Domain/Validation/`・`rg -l 'OrderNodesWithinRanks|CountCrossings' src/EndfieldAicWeb.Application*/` 等で、各メソッドの所在ファイルを確認する | 計画書 §3 の割り当て表どおりのファイルにメンバーが置かれている |
| MN-209 | 既存検証の回帰 | `dotnet build`・`dotnet test`・`~/.venvs/validate/bin/python tools/validate_master.py` を実行する | 全緑。変更は verbatim 分割のみのため、それ以外の失敗は変更混入を疑う |

## 2. 目視確認

なし（verbatim 分割のみのため UI への影響はない）。

## 3. 受け入れ条件との対応

- MN-205・MN-206 が verbatim 分割の不変条件をカバーする
- MN-207 が公開 API・修飾名の不変を、MN-208 が割り当てどおりの配置をカバーする
- MN-209 が回帰をカバーする
