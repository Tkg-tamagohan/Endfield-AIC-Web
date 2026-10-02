# AGENTS.md

Endfield-AIC-Web での作業に適用するリポジトリ固有の規約。
組織共通ルールは shared-skills プラグインの AGENTS.md に従う。

## 移植元 Planner の先行資産を参照する

- 本リポジトリは旧デスクトップ版 `Tkg-tamagohan/Endfield-AIC-Planner`（WPF、凍結済み）の Web 移植である。
- 要件・仕様決定・実データ（`data/default_master.json`）・アイコン（`data/icons/`、`icon-item-<slug>` / `icon-fac-<slug>` 規約）・運用文書が Planner 側に先行して存在する。
- 移植・挙動確認・文書作成の前に Planner リポジトリの `docs/`・`data/` を参照する。参照実装は Planner の Domain/Calculation/Validation 層。
- 文書フォーマット（実装計画・決定記録・テスト仕様書の構成）は Planner の同名文書に倣う。

## フェーズ実装は計画書とテスト仕様書から始める

- `docs/implementation-plan.md` のフェーズに取りかかるときは、コードより先に `docs/phases/implementation-plan-phaseN.md` と `docs/phases/test-specification-phaseN.md` を作成する。
- 計画書には適合方針・暫定解釈を、テスト仕様書には ID 採番のテストケースを記録し、それを根拠に実装する。
- 仕様の正は `docs/requirements.md` と `docs/decision-records.md`。

## 構成変更ではスキルの参照を照合する

- プロジェクトの追加・RCL への移設・`wwwroot` 配信パスの変更など、プロジェクト構成や配信経路を変える作業では、`.devin/skills/` と個人プラグインの Endfield 関連スキル（`testing-blazor-apps-flow-graph`・`fixture-injection-blazor-testing`・`webgpu-blazor-ui-testing`）が参照するパス・手順を grep で照合し、陳腐化した記述を同じ変更で追従させる。
