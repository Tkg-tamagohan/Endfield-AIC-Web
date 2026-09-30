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
