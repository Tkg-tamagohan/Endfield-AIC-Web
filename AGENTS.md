# AGENTS.md

Endfield-AIC-Web での作業に適用するリポジトリ固有の規約。
組織共通ルールは shared-skills プラグインの AGENTS.md に従う。

## 移植元リポジトリの扱い

- 本リポジトリは旧デスクトップ版 `Tkg-tamagohan/Endfield-AIC-Planner`（WPF、凍結済み）の Web 移植である。
- 移植は完了しており、仕様の正は本リポジトリの `docs/requirements.md` と `docs/decision-records.md` のみとする（仕様決定 CV）。
- 旧リポジトリ由来の決定は効力を持つもののみ `docs/decision-records.md` の番号へ統合済みで、新旧番号の対応は `docs/phases/implementation-plan-phase41.md` §4 の対応表に集約する（仕様決定 CT）。
- 旧番号での検索はこの対応表を読み替えの鍵に使う。
- 旧リポジトリは GitHub Archive 処置とする（仕様決定 CW）。
- 挙動確認や文書作成で旧リポジトリの docs や data、スキルを参照しない。
- 文書フォーマット（実装計画、決定記録、テスト仕様書の構成）は本リポジトリ `docs/` 配下の先例に従う。

## フェーズ実装は計画書とテスト仕様書から始める

- `docs/implementation-plan.md` のフェーズに取りかかるときは、コードより先に `docs/phases/implementation-plan-phaseN.md` と `docs/phases/test-specification-phaseN.md` を作成する。
- 計画書には適合方針・暫定解釈を、テスト仕様書には ID 採番のテストケースを記録し、それを根拠に実装する。
- 仕様の正は `docs/requirements.md` と `docs/decision-records.md`。
- Phase 番号・仕様決定 ID・テスト ID の採番前に `python3 tools/scan_ids.py` で現行最大と次候補を機械確認する（`--with-prs` でオープン PR の使用分も列挙）。文書内の「現行最大」記述は `--check <file>` で実測と照合できる。

## 構成変更ではスキルの参照を照合する

- プロジェクトの追加・RCL への移設・`wwwroot` 配信パスの変更など、プロジェクト構成や配信経路を変える作業では、`.devin/skills/` と個人プラグインの Endfield 関連スキル（`testing-blazor-apps-flow-graph`・`fixture-injection-blazor-testing`・`webgpu-blazor-ui-testing`）が参照するパス・手順を grep で照合し、陳腐化した記述を同じ変更で追従させる。

## PR のレビュー観点は REVIEW.md に集約する

- このリポジトリの PR をレビューするときは、リポジトリ固有の不変条件と既知の罠の確認観点を `REVIEW.md` に従って確認する。
- `REVIEW.md` は AI レビューア向けの文書であり、作業者向けの規約は本ファイルに残す。
