# Phase 16 テスト仕様書

**対象**: Phase 16 成果物（輸送容量と推奨流量制限の単位改訂）
**前提ドキュメント**: [implementation-plan-phase16.md](implementation-plan-phase16.md)、[decision-records.md](../decision-records.md)（仕様決定 AM）

> 本書は Phase 16 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> 従来のテスト項目は Phase 15 以前の仕様書を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit（既存の `tests/EndfieldAicWeb.Domain.Tests`・`tests/EndfieldAicWeb.Application.Tests` を更新） |
| 実行コマンド | `dotnet test`、および `~/.venvs/validate/bin/python tools/validate_master.py`（マスタ未変更でも回帰確認として実行） |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | 新規 UI を伴わないためブラウザプレビューのユーザー確認は挟まない（Phase 9 と同様）。表示文字列の変更は §3 の観点で確認する |

## 2. 自動テスト項目

### 既存テストの期待値改訂

既存 ID を保ったまま、仕様決定 AM に沿って期待値を更新する。詳細な期待値は各仕様書の改訂済み行を参照。

| ID | 内容 | 期待 |
|---|---|---|
| TRN-01 | ベルト超過は警告（レーン数付き） | F-08、`i-belt-item` 45/分 → `TransportCapacityExceeded`（2 レーン）（[test-specification-phase2.md](test-specification-phase2.md) §TRN） |
| TRN-02 | パイプ超過は警告 | F-08、`i-pipe-item` 90/分 → 警告（2 レーン）（同上） |
| TRN-03 | TransportKind=None は対象外 | F-08、`i-none-item` 500/分 → 容量警告なし。上流 `i-belt-src` の流量が容量未満に収まる値を選ぶ（同上） |
| TRN-04 | 上限ちょうどは警告なし | F-08、`i-belt-item` 30/分 → 警告なし（同上） |
| FLW-01/02/04 | 推奨制限の期待値 | 個/分値（1240・1280.5・540。同 §FLW。FLW-02 は非整数値で丸めなしを検査） |
| FLW-05 | ランごとの推奨制限 | 個/分値 5（同 §FLW） |
| FG-13 | 輸送容量超過の簡易判定 | `max(RequiredPerMinute, 生産量, 採取量)` が新容量を超えるアイテムでフラグ（[test-specification-phase15.md](test-specification-phase15.md) §FG）。入力は旧 30 個/s（=1800/分）では発火せず新 30 個/分で発火する値（`i-t` 60/分）とし、/60 残存を検出できるようにする |

### 新規テスト

| ID | 内容 | 期待 |
|---|---|---|
| TRN-05 | 警告文は個/分表記 | F-08、`i-belt-item` 45/分 → 警告文に「個/分」を含み「個/s」を含まない |

### 改訂の波及（Assert.Empty 見直し）

容量の実効閾値が 1/60 に下がるため、容量警告を直接対象としない既存テストでも `TransportCapacityExceeded` が発火しうる。
`Assert.Empty(plan.Warnings)` 等で失敗したテストは、テスト意図が「容量警告以外の警告がないこと」に該当するものに限り、容量警告を許容する形へ見直す。
意図が容量警告と無関係に確定できないものは見直し対象にせず、原因を個別に確認する。

## 3. 手動確認項目

| ID | 内容 | 手順 | 期待 |
|---|---|---|---|
| MN-46 | 推奨流量制限行の単位表記 | App で調整済表示のある計画（目標レートが出力整数倍でないもの）を表示する | 「を X/分 に制限」と個/分表記で出る |
| MN-47 | 容量警告文の単位表記 | App でベルト 30 個/分を超える計画を表示する | 警告文に「個/分」と出る（「個/s」は出ない） |

## 4. 確認項目の結果記録

実装 PR の本文に、`dotnet test` の結果と §2・§3 の各項目の合否を表で記録する。
