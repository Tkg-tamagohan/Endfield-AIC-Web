# Phase 9 テスト仕様書

**対象**: Phase 9 成果物（用語・単位の整理: 基礎素材→採取素材の改称＋レート単位の毎分統一）
**前提ドキュメント**: [requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 AB・AF）、[implementation-plan.md](../implementation-plan.md)、[implementation-plan-phase9.md](implementation-plan-phase9.md)

> 本書は Phase 9 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> 従来のテスト項目は Phase 8 以前の仕様書を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。Domain 計算は `tests/EndfieldAicWeb.Domain.Tests`、JSON I/O は `tests/EndfieldAicWeb.Infrastructure.Tests`、Application・Admin は対応する既存プロジェクトに配置する |
| 実行コマンド | `dotnet test`、および `~/.venvs/validate/bin/python tools/validate_master.py` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | 本 Phase は改称・単位変換のみで新規 UI を伴わないため、ブラウザプレビュー確認は挟まない |

## 2. フィクスチャの追従

`CalculationFixtures`・`ApplicationFixtures`・`TestJson` はモデル改称へ追従し、レート値は個/分の等値へ換算する。

- `Item.IsBaseMaterial` → `IsGatherable`。引数名 `isBaseMaterial` → `isGatherable`。`Category` 文字列の「基礎素材」→「採取素材」
- `Environment.ConsumeRatePerSecond` → `ConsumeRatePerMinute`。引数 `ratePerSecond` → `ratePerMinute`。値は ×60（1 → 60、6 → 360、2 → 120、4 → 240）
- `FixedConsumption.RatePerSecond` → `RatePerMinute`。値は ×60（0.1 → 6、0.5 → 30、2 → 120）
- `SupplyKind.RawMaterial` → `Gathered`、`EnvironmentRequirement.ConsumeRatePerSecondTotal` → `ConsumeRatePerMinuteTotal`

等値換算のため、既存ケース（ENV-01・FIX-01〜04・CNV-01 等）の期待値は変わらない。

## 3. テスト項目一覧

### REN: 改称の I/O 契約

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| REN-01 | 旧フィールド名の JSON は拒否 | `IsBaseMaterial`・`ConsumeRatePerSecond`・`RatePerSecond` を持つ JSON | 新名フィールドの必須違反＋旧名キーの未知プロパティ拒否で読み込み失敗 |
| REN-02 | エクスポートは新名のみ出力 | エンティティをエクスポート | `IsGatherable`・`ConsumeRatePerMinute`・`RatePerMinute` が出て、旧名キーが出ない |
| REN-03 | 同梱データは新名で適合 | `data/master.json` | `IsGatherable`・`ConsumeRatePerMinute`・`RatePerMinute`・`Category="採取素材"` の構造でスキーマ v1 に適合（CIV-05 と `validate_master.py` で担保） |

### REG: 既存ケースの回帰

Phase 8 以前の仕様書で定義した全ケースを実行し、改称後も全緑であることを確認する。
計算結果を示す期待値は等値換算のため据え置きである。
環境の消費合計を参照する期待値だけは、参照先が `ConsumeRatePerSecondTotal`（個/s）から `ConsumeRatePerMinuteTotal`（個/分）へ変わるため、フィクスチャの等値換算に合わせて ×60 した値を取る（散布機 1 台・消費 360 個/分なら期待値は 360）。

## 4. 受け入れ条件との対応

- `dotnet test` が全緑。REN-01〜03 を含め、既存ケースの期待値は Phase 2〜8 の仕様書どおり
- `tools/validate_master.py` がスキーマ適合を報告する（REN-03）
- 挙動変更を伴わない改名・単位変換のみで、要求量・台数・警告の計算結果は変わらない
