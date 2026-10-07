# Phase 41 テスト仕様

**対象フェーズ**: Phase 41（旧リポジトリ依存の解消）
**前提ドキュメント**: [implementation-plan-phase41.md](implementation-plan-phase41.md)、[decision-records.md](../decision-records.md)（仕様決定 CT〜CY）
**関連ドキュメント**: [test-specification-phase40.md](test-specification-phase40.md)（MN 採番の先行）

> 本書は Phase 41 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 手動確認は MN-184 以降（main の現行最大は MN-183）。文書とコメントのみの変更で xUnit の追加はない。push 前に main で再確認する。

## 1. 機械検査（コマンド）

| ID | 対象 | 内容 | 期待 |
|---|---|---|---|
| MN-184 | 「旧 X」参照の残存 | `rg -n "旧 ?[A-Z]{1,2}\b" docs/ src/ tests/ tools/ AGENTS.md README.md -g '!docs/phases/implementation-plan-phase41.md'` を実行する | 決定 ID 形式の参照が、計画書 §4 で「統合しない」とした廃止済み決定への意図的な言及（「撤去済みの〜」等の自完結表現）以外で残らない。「旧 JSON」「旧 WPF」等の ID でない用法は対象外。「旧 X」という表現自体を説明する記述（仕様決定 CT の決定文・本書・対応表の説明文）は参照ではないため許容する。計画書は §4 対応表で旧 ID を出典として保持するため検査対象から除外する |
| MN-185 | 旧リポジトリリンクの残存 | `rg -n "Endfield-AIC-Planner|EndfieldAicPlanner" docs/ src/ tests/ tools/ AGENTS.md README.md .devin/ .github/` を実行する | 正の所在としての参照と作業手順上の参照が残らない。README や計画書のうち、移植の経緯を説明する歴史的記述としての言及、および requirements.md §11 の出典確認用リンク・対応表の出典記載（仕様決定 CT）の言及は許容する |
| MN-186 | 対応表の網羅性 | 置換作業の前に main 相当のブランチで `rg -n "旧 ?[A-Z]{1,2}\b" docs/ src/ tests/ tools/ AGENTS.md README.md` を実行してベースラインの参照一覧を記録し、その一覧と `docs/phases/implementation-plan-phase41.md` §4 の対応表を照合する | ベースラインの「旧 X」参照がすべて対応表のいずれかの行で扱われている（置換後の残存だけでは消えた参照を検出できないため、置換前の一覧を基準にする） |
| MN-187 | 既存検証の回帰 | `dotnet build`・`dotnet test`・`~/.venvs/validate/bin/python tools/validate_master.py` を実行する | 全緑。文書・コメントのみの変更のため失敗する場合は変更混入を疑う |

## 2. 目視確認

| ID | 対象 | 内容 | 期待 |
|---|---|---|---|
| MN-188 | AGENTS.md | 改訂後の記述を読む | Planner の docs/data/参照実装を参照する規定と文書フォーマット継承の記述がなく、仕様の正が本リポジトリの requirements.md と decision-records.md に閉じている |
| MN-189 | 仕様の自己完結 | requirements.md を「旧 X」参照なしに通読する | 「（旧 X 踏襲）」「旧 X 相当」等の外部依存の由来注記がなく、各規則が本文または Web 側仕様決定 ID で読み切れる |
| MN-190 | Planner 側処置 | Planner の README と GitHub リポジトリ状態を確認する | README に凍結と後継案内がマージ済みで、リポジトリがアーカイブ済み（read-only）になっている |

## 3. 受け入れ条件との対応

- MN-184〜186 が「旧 X」参照の解消と網羅性をカバーする
- MN-187 が文書のみの変更であることの確認をカバーする
- MN-188・189 が仕様の正の自リポジトリ完結（仕様決定 CV・CT）をカバーする
- MN-190 が旧リポジトリの最終処置（仕様決定 CW）をカバーする
