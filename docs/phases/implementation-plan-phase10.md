# Phase 10 実装詳細計画

**対象フェーズ**: Phase 10（マップモデル: GameMap＋スキーマ＋管理ツールのマップ編集）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)（§4.7、§5.9、§5.10）、[decision-records.md](../decision-records.md)（仕様決定 AC）

> 本書は Phase 10 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### やること

仕様決定 AC は requirements.md（§4.7、§5.9、§5.10）へ先行反映済みである。
本 Phase は、そのデータモデルをコード、JSON 入出力、スキーマ、同梱データ、管理ツールへ実装する。

- `GameMap` エンティティと `GatherRate` 行の新設（Domain）
- `MasterDocument.Maps`、`MasterDataSnapshot.Maps` の追加
- `MasterValidator` へのマップ検証の追加
- JSON 入出力（読み込みの構造検証と実体化、エクスポート）と `data/master.schema.json` の `Maps` 節
- 管理ツールのマップ編集ページ（一覧、共通属性、所属イベント、採取レート行の編集）、ナビ、ホームの件数表示
- アイテム削除時とイベント削除時の参照検出へのマップの追加
- `data/master.json` への Maps サンプルの追加、`tools/validate_master.py` の `ENTITY_SECTIONS` への `Maps` の追加
- 本書と、[test-specification-phase10.md](test-specification-phase10.md)

### やらないこと

- 計算への接続（`ContextFilter.MapId`、採取上限、`GatherRateOverride`、警告 `GatherCapExceeded`）。Phase 11 で扱う。本 Phase では計算結果は変わらない
- 公開アプリと管理ツールの計算プレビューへのマップ選択 UI の追加。Phase 12 と Phase 13 で扱う
- `SchemaVersion` の変更。`Maps` の追加は実データ投入前であり、移行需要がないため 1 のままとする（仕様決定 AF と同じ扱い）

## 2. データモデル

### 2.1 Domain

```csharp
// Domain/Models/GameMap.cs
public class GameMap : MasterEntity
{
    public List<GatherRate> GatherRates { get; set; } = [];
    public string? GameEventId { get; set; }
}

// Domain/Models/GatherRate.cs
public class GatherRate
{
    public required string ItemId { get; set; }
    public bool IsUnlimited { get; set; }
    public double? RatePerMinute { get; set; }   // 個/分。IsUnlimited=true のとき null
}
```

- `MasterDocument.Maps`（`List<GameMap>`、既定 `[]`）を `Recipes` と `Icons` の間に追加する。
- `MasterDataSnapshot.Maps`（`required IReadOnlyList<GameMap>`）と `MapsById`（`IReadOnlyDictionary<string, GameMap>`）を既存コレクションと同じ不変化の作法で追加する。
- `MasterSnapshotFactory.Create` は `Maps = document.Maps` を渡す。

### 2.2 JSON とスキーマ

ルートに `Maps` を必須配列として追加する（他のエンティティ節と同じく required、空配列可）。
マップ要素は共通属性に `GameEventId` と `GatherRates` を加え、`GatherRates` の各行は次の 3 キーをすべて必須とする。

```json
{
  "Id": "map-sample", "Name": "サンプル採取地", "Description": "", "IconKey": null,
  "VersionAdded": "0.1.0", "VersionRemoved": null, "GameEventId": null,
  "GatherRates": [
    { "ItemId": "item-ore", "IsUnlimited": false, "RatePerMinute": 240 },
    { "ItemId": "item-fuel", "IsUnlimited": true, "RatePerMinute": null }
  ]
}
```

- スキーマ: `gatherRate` 定義は `additionalProperties: false`、`ItemId` は `minLength: 1`、`IsUnlimited` は boolean、`RatePerMinute` は `number`（`exclusiveMinimum: 0`）または `null`。`IsUnlimited` の値に応じて `if`/`then` で `RatePerMinute` の型を絞る（false なら number、true なら null）
- 読み込み: 既存のレシピ入出力と同じ作法で、要素 null、必須キー欠落、未知プロパティを構造違反として拒否する
- エクスポート: 既存エンティティと同じく、null 要素と null の `Description` を拒否し、`Maps` を書き出す。`IconExportPlanner` のエンティティ列挙にマップを含め、マップの `IconKey` もアイコン整合とマニフェスト構築の対象にする

### 2.3 検証規則（MasterValidator）

エンティティ種別名は `"GameMap"`、コレクション名は `"Maps"` とする。

単体規則（`ValidateGameMap`）は次のとおり。

- 共通属性（Id、Name 必須、IconKey 文字種、VersionAdded/VersionRemoved）
- `GatherRates[i].ItemId` は必須
- 同一マップ内の `ItemId` 重複は不可（Field は `GatherRates`）
- `IsUnlimited=false` の行は `RatePerMinute` が null でない 0 より大きい有限値であること（Field は `GatherRates[i].RatePerMinute`）
- `IsUnlimited=true` の行は `RatePerMinute` が null であること（同上。§4 暫定解釈）

文書全体の規則（`ValidateAll` に `maps` 引数を追加）は次のとおり。

- `Maps` 内の Id 重複は不可
- `GameEventId` が存在するイベントを参照すること
- `GatherRates[i].ItemId` が存在するアイテムを参照すること
- 参照先アイテムが `IsGatherable=true` であること（採取素材のみを指す。仕様決定 AC）

`ValidateAll` の呼び出し元（`MasterJsonReader`、`MasterExporter`、Admin の `AdminDocumentService` 2 箇所、既存テスト 2 箇所）はすべて `maps` を渡すよう追従する。

## 3. 管理ツール

### 3.1 マップ編集ページ（`/maps`）

既存の `EventsPage`、`RecipesPage` と同じ 2 ペイン構成とする。

- 左: `ListPane` による一覧（表示名、Id）。新規追加と削除
- 右: `CommonFieldsEditor`、所属イベント（`RefSelect`、空は「（常設）」）、採取レート表
- 採取レート表の列: アイテム（`RefSelect`。候補は `IsGatherable=true` のアイテムのみ）、無限（チェックボックス）、上限（個/分）（`NumberInput`。無限の行は入力欄の代わりに「—」を表示）、行削除ボタン
- 「＋ 採取レートを追加」: そのマップにまだ行のない採取素材のうち先頭のものを選び、`IsUnlimited=false`、`RatePerMinute=60` で追加する。該当がなければ `ItemId` 空の行を追加し、検証で必須違反として残す
- 無限をオンにすると `RatePerMinute` を null にし、オフにすると 60 を入れる
- 表の下の注記: 「行のない採取素材は、このマップでは採取できません（上限 0）。候補は採取素材のアイテムだけです。」
- 新規マップは `EntityFactory.NewGameMap`（Name「新規マップ」、`GatherRates` 空、常設）で作り、Id は `SuggestId(..., "map")` で採番する
- マップ削除と行削除では、`Store.ClearInvalidOwner(s)` で未確定の不正入力を破棄する（既存ページと同じ作法）

### 3.2 ナビ、件数、参照検出

- `MainLayout` のナビで「イベント」と「レシピ」の間に「マップ」を追加する
- ホームの文書件数表示で「イベント」の後に「マップ: N」を追加する
- `MasterReferenceFinder.FindItemReferences` に `("GameMap", mapId, "GatherRates[i].ItemId")` を、`FindGameEventReferences` に `("GameMap", mapId, "GameEventId")` を加える。アイテムページとイベントページの削除確認は既存どおりこの結果を表示する

## 4. 暫定解釈

- **無限行の `RatePerMinute`**: requirements §5.9 は「`IsUnlimited=false` のとき必須で有限の正値」とだけ定め、`IsUnlimited=true` の行の値を定めていない。本 Phase では「無限の行は `RatePerMinute` を null とする」と解釈し、値を持つ場合は検証違反とする。無限なのに数値が残っていると、上限値の意味を読み手が取り違えるためである
- **`Maps` の必須化**: 既存のエンティティ節と同じく、ルートの `Maps` キーを必須とする。`Maps` を持たない旧 JSON は、必須キー欠落として読み込み時に拒否する（移行需要がないため後方互換は持たない）
- **サンプルデータ**: `data/master.json` のサンプルマップは 1 件とし、原鉱石を上限 240 個/分、固形燃料を無限とする。活性ガスには行を置かず、「行のない採取素材は採取不可」の例を兼ねる。値は動作確認用の仮の値であり、ゲーム実データではない
- **新規行の既定値**: 採取レート行の新規追加時とオフ切替時の 60 個/分は入力の初期値であり、仕様上の意味を持たない
- **`DataVersion`**: サンプルの追加では据え置く（Phase 9 と同じ扱い。データ版は管理ツールのエクスポートで更新する）

## 5. 作業順序

1. 本書と [test-specification-phase10.md](test-specification-phase10.md) を作成する。
2. §2.1 の Domain モデルとスナップショットを追加する。
3. §2.3 の検証規則を追加し、`ValidateAll` の呼び出し元を追従する。
4. §2.2 の JSON 入出力とスキーマを追加する。
5. `data/master.json` へサンプルを追加し、`tools/validate_master.py` の `ENTITY_SECTIONS` に `Maps` を加える。
6. §3 の管理ツールを実装する。
7. ブラウザプレビューでユーザーにマップ編集ページを操作してもらい、フィードバックを反映する（UI の確認をテストに先行させる規約）。
8. テスト仕様書の各ケースを実装し、`dotnet test` 全緑と `tools/validate_master.py` の通過を確認する。
9. `implementation-plan.md` の Phase 10 を `[x]` へ更新して PR を作成する。

## 6. 受け入れ条件

- `dotnet test` が全緑。`~/.venvs/validate/bin/python tools/validate_master.py` がスキーマ適合を報告する
- 計算結果は変わらない（マップはまだ計算へ接続しない）。既存ケースの期待値は据え置き
- 管理ツールでマップの追加、編集、削除、採取レート行の追加、編集、削除ができ、エクスポートした JSON に反映される
