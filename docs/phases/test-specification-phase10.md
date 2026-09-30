# Phase 10 テスト仕様書

**対象**: Phase 10 成果物（マップモデル: GameMap＋スキーマ＋管理ツールのマップ編集）
**前提ドキュメント**: [requirements.md](../requirements.md)（§4.7、§5.9、§5.10）、[decision-records.md](../decision-records.md)（仕様決定 AC）、[implementation-plan.md](../implementation-plan.md)、[implementation-plan-phase10.md](implementation-plan-phase10.md)

> 本書は Phase 10 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> 従来のテスト項目は Phase 9 以前の仕様書を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。検証規則は `tests/EndfieldAicWeb.Domain.Tests`、JSON 入出力は `tests/EndfieldAicWeb.Infrastructure.Tests`、参照検出と生成は `tests/EndfieldAicWeb.Application.Tests` に配置する |
| 実行コマンド | `dotnet test`、および `~/.venvs/validate/bin/python tools/validate_master.py` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | 管理ツールのマップ編集ページは、テストの前にブラウザプレビューでユーザーの操作確認を受ける（§3 MUI） |

## 2. フィクスチャの追従

- `TestJson` の正常系文書に `Maps` を追加する。マップ 1 件（`m-01`、常設）に、`i-ore` を上限 60 個/分、`i-gas` を無限とする 2 行を置く
- Domain と Application のフィクスチャは、マップを使わないケースでは `Maps` を空とする。既存ケースの期待値は変わらない

## 3. テスト項目一覧

### MAP: マップの検証規則（MasterValidator）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| MAP-01 | 正常なマップは違反なし | 上限行（60 個/分）と無限行を持つ常設マップ、参照先はいずれも採取素材 | 違反 0 件 |
| MAP-02 | 採取レート行なしも許容 | `GatherRates` 空のマップ | 違反 0 件 |
| MAP-03 | 共通属性の検証を適用 | Name 空のマップ | `GameMap`/`Name` の必須違反 |
| MAP-04 | ItemId 必須 | `ItemId` 空の行 | `GatherRates[0].ItemId` の必須違反 |
| MAP-05 | 同一マップ内の ItemId 重複 | 同じ採取素材の行を 2 件 | `GatherRates` の重複違反 |
| MAP-06 | 別マップ間の同一 ItemId は許容 | 2 マップがそれぞれ同じ採取素材の行を持つ | 違反 0 件 |
| MAP-07 | 上限行のレート必須 | `IsUnlimited=false`、`RatePerMinute=null` | `GatherRates[0].RatePerMinute` の違反 |
| MAP-08 | 上限行のレートは正の有限値 | `IsUnlimited=false` で `RatePerMinute` が 0、負値、NaN、∞ | いずれも `GatherRates[0].RatePerMinute` の違反 |
| MAP-09 | 無限行はレートを持たない | `IsUnlimited=true`、`RatePerMinute=60` | `GatherRates[0].RatePerMinute` の違反（暫定解釈） |
| MAP-10 | 参照先アイテムの存在 | 存在しない `ItemId` を指す行 | `GatherRates[0].ItemId` の参照違反 |
| MAP-11 | 参照先は採取素材のみ | `IsGatherable=false` のアイテムを指す行 | `GatherRates[0].ItemId` の違反 |
| MAP-12 | 所属イベントの参照 | 存在しない `GameEventId` | `GameEventId` の参照違反 |
| MAP-13 | Maps 内の Id 重複 | 同じ Id のマップ 2 件 | `Maps` の Id 重複違反 |

### MJS: JSON 入出力とスキーマ

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| MJS-01 | Maps の読み込み | §2 の正常系 JSON | 読み込み成功。マップ 1 件、行 2 件の `ItemId`、`IsUnlimited`、`RatePerMinute` が JSON どおり |
| MJS-02 | Maps キーの欠落 | ルートから `Maps` を除いた JSON | 読み込み失敗（必須キー欠落） |
| MJS-03 | 行の必須キー欠落 | `RatePerMinute` キーを除いた行 | 読み込み失敗 |
| MJS-04 | 未知プロパティの拒否 | マップ要素と行要素にそれぞれ未知キーを追加 | いずれも読み込み失敗 |
| MJS-05 | null 要素の拒否 | `Maps` 配列、`GatherRates` 配列に null 要素 | いずれも読み込み失敗 |
| MJS-06 | 意味違反の検出 | 採取素材でないアイテムを指す行 | 読み込み失敗。違反に `GameMap` の `GatherRates[0].ItemId` を含む |
| MJS-07 | 往復の保持 | 正常系を読み込んでエクスポートし再読み込み | マップと行の全フィールドが一致する。無限行の `RatePerMinute` は JSON 上 `null` として出力される |
| MJS-08 | マップのアイコン参照 | マップに IconKey を設定し、アイコン実体を用意してエクスポート | マニフェストにそのキーが含まれる（孤立扱いで落ちない） |
| MJS-09 | 同梱データの適合 | `data/master.json` | `Maps` を含めて読み込み検証を通過し（CIV-05）、`validate_master.py` がスキーマ適合を報告する |

### MRF: 参照検出と生成（Application）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| MRF-01 | アイテム参照にマップ行を含む | 採取素材をマップ行が参照する文書で `FindItemReferences` | `("GameMap", マップ Id, "GatherRates[i].ItemId")` を含む |
| MRF-02 | イベント参照にマップを含む | イベント所属のマップを持つ文書で `FindGameEventReferences` | `("GameMap", マップ Id, "GameEventId")` を含む |
| MRF-03 | 新規マップの既定値 | `EntityFactory.NewGameMap(id, version)` | Name「新規マップ」、`GatherRates` 空、`GameEventId` null。単体検証で違反 0 件 |
| MRF-04 | スナップショットへの反映 | マップを持つ文書から `MasterSnapshotFactory.Create` | `Maps` と `MapsById` にマップが入る |

### REG: 既存ケースの回帰

Phase 9 以前の仕様書で定義した全ケースを実行し、全緑であることを確認する。
マップは計算へ接続しないため、計算結果の期待値はすべて据え置く。

## 4. 手動確認（MUI）

管理ツールをブラウザプレビューで起動し、ユーザーに次を操作してもらう。
自動テストの対象外であり、結果はフィードバックとして記録する。

| ID | 内容 | 期待 |
|---|---|---|
| MUI-01 | ナビとホームの件数 | ナビに「マップ」があり、ホームに「マップ: N」が表示される |
| MUI-02 | マップの追加、編集、削除 | 追加したマップの共通属性と所属イベントを編集でき、削除できる |
| MUI-03 | 採取レート行の編集 | 行の追加、アイテム選択（採取素材のみ）、上限値入力、無限の切替、行削除ができる |
| MUI-04 | 検証とエクスポート | 重複行やレート未入力を検証で指摘し、正常な状態ではエクスポートした JSON に Maps が反映される |
| MUI-05 | 参照つき削除の警告 | マップ行が参照する採取素材をアイテムページで削除しようとすると、参照箇所にマップが表示される |

## 5. 受け入れ条件との対応

- `dotnet test` が全緑。MAP、MJS、MRF の全ケースと既存ケースが通過する
- `tools/validate_master.py` がスキーマ適合を報告する（MJS-09）
- 計算結果は変わらない（REG）
