# Phase 43 テスト仕様

**対象フェーズ**: Phase 43（Domain/Application の構造整理（移動系））
**前提ドキュメント**: [implementation-plan-phase43.md](implementation-plan-phase43.md)、[decision-records.md](../decision-records.md)（仕様決定 DC）
**関連ドキュメント**: [test-specification-phase44.md](test-specification-phase44.md)（分割系の後続 Phase）

> 本書は Phase 43 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 手動確認は MN-198 以降（main の現行最大は MN-197）。xUnit の新規採番はなし（移動のみで挙動は不変）。push 前に main で再確認する。

## 1. 機械検査（コマンド）

| ID | 対象 | 内容 | 期待 |
|---|---|---|---|
| MN-198 | 名前空間とフォルダの一致 | `rg '^namespace' src/EndfieldAicWeb.Application --no-filename | sort -u` と `rg -l '^namespace EndfieldAicWeb\.Application;$' src tests`、および `rg -l '^namespace EndfieldAicWeb\.Domain;$' src tests` を実行する | Application 配下の名前空間は `EndfieldAicWeb.Application.{Calculation,PlanView,Graph,MasterEditing}` の 4 系統のみで、各ファイルの namespace が配置フォルダと一致する。フラットな `EndfieldAicWeb.Application`・`EndfieldAicWeb.Domain`（SemVersion の旧名前空間）を宣言するファイルが残らない |
| MN-199 | 移動差分の限定 | `git diff -M main...HEAD -- src tests` を確認し、改名ファイルの差分と、変更ファイルの差分行を検査する | 移動ファイルは rename として検出され、内容差分は名前空間宣言・using の追加/整理・文書コメントのみ。移動していないファイルの差分は using の追従のみで、それ以外の行変更がない（`git diff main...HEAD -- <file> | grep -E '^[+-]' | grep -vE '^(\+\+\+|---)|^[+-]\s*(using|namespace|//|///|$)'` が各ファイルで空になることで機械確認する。改行コードや末尾差分だけの例外は個別に記録する） |
| MN-200 | テストクラス修飾名の不変 | `rg '^namespace' tests --no-filename | sort -u` を実施前後で比較する | テストプロジェクトの名前空間集合が不変（テスト ID 帳簿の前提を壊さない） |
| MN-201 | 旧名前空間参照の残存 | `rg 'using EndfieldAicWeb\.Application;' src tests`・`rg 'using EndfieldAicWeb\.Domain;' src tests`・`rg 'EndfieldAicWeb\.Application\.' .devin/skills`・`rg 'src/EndfieldAicWeb\.(Application|Domain)/' AGENTS.md REVIEW.md docs/requirements.md docs/implementation-plan.md` を実行する | 全て 0 件。フラット名前空間を引く using と、生きている文書・スキルの移動対象パス参照が残らない（過去の `docs/phases/` 文書は作業記録のため検査対象外） |
| MN-202 | 既存検証の回帰 | `dotnet build`・`dotnet test`・`~/.venvs/validate/bin/python tools/validate_master.py` を実行する | 全緑。変更はファイル移動と using 追従のみのため、それ以外の失敗は変更混入を疑う |

## 2. 目視確認

| ID | 対象 | 内容 | 期待 |
|---|---|---|---|
| MN-203 | 公開アプリの煙突 | `dotnet run --project src/EndfieldAicWeb.App --no-launch-profile --urls http://127.0.0.1:5180` で起動し、既定の生産リストで計算を実行する | 計画・生産フローグラフが従来どおり表示される |
| MN-204 | 管理ツールの煙突 | `dotnet run --project src/EndfieldAicWeb.Admin --no-launch-profile --urls http://127.0.0.1:5181` で起動し、同梱マスタ JSON を読み込んで計算プレビューを開く | 読み込み・検証・プレビュー計算が従来どおり動作する |

## 3. 受け入れ条件との対応

- MN-198 がフォルダ構成と名前空間一致（仕様決定 DC）をカバーする
- MN-199・MN-200 が「内容変更なし」の不変条件をカバーする
- MN-201 が参照追従と文書・スキル同期の残漏れをカバーする
- MN-202 が回帰を、MN-203・MN-204 が実アプリの煙突確認をカバーする
