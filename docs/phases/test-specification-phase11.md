# Phase 11 テスト仕様書

**対象**: Phase 11 成果物（採取上限の計算: 上限・代替レシピ展開・警告・ユーザー上書き）
**前提ドキュメント**: [requirements.md](../requirements.md)（§4.7）、[decision-records.md](../decision-records.md)（仕様決定 AC・AD・AE）、[implementation-plan-phase11.md](implementation-plan-phase11.md)

> 本書は Phase 11 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。
> 従来のテスト項目は Phase 10 以前の仕様書を参照。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。計算ロジックは `tests/EndfieldAicWeb.Domain.Tests`、入力パースは `tests/EndfieldAicWeb.Application.Tests` に配置する |
| 実行コマンド | `dotnet test`、および `~/.venvs/validate/bin/python tools/validate_master.py` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |
| 手動確認 | なし（本 Phase は UI 変更を含まない） |

## 2. フィクスチャ

`CalculationFixtures` に採取上限検証用のフィクスチャとヘルパを追加する。

### F-15: 採取上限

| 要素 | 内容 |
|---|---|
| アイテム | `i-ore`（採取素材・Belt）、`i-stone`（採取素材・Belt）、`i-shard`（採取素材・Belt・レシピなし）、`i-part`（非採取） |
| 設備 | `f-mine`、`f-asm` |
| レシピ | `r-ore`: f-mine・4 秒・`i-stone`×1 → `i-ore`×1。`r-part`: f-asm・4 秒・`i-ore`×2 → `i-part`×1 |
| マップ | `m-cap`（常設: i-ore 上限 60、i-stone 上限 30、i-shard 上限 10）、`m-inf`（常設: i-ore 無限）、`m-none`（常設: 行なし）、`m-ev`（ev-off 所属: i-ore 上限 999）、`m-ev-shard`（ev-off 所属: i-shard 上限 999） |
| イベント | `ev-off` |

- `i-ore` は採取素材かつ `r-ore` で生産可能（上限超過→代替展開の検証用）
- `i-stone` は `r-ore` の入力で上限 30（連鎖した上限適用の検証用）
- `i-shard` はレシピを持たない採取素材（代替なしの検証用）
- `i-part` は非採取素材で `i-ore` を消費する（上限が需要の発生源を問わないことの検証に兼用可）

## 3. テスト項目一覧

### GAT: 採取上限の計算（Domain）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| GAT-01 | マップ未選択は採取無制限 | F-15、MapId=null、i-ore 100/分 | i-ore 採取 100。r-ore は稼働しない |
| GAT-02 | 上限内は採取 | m-cap、i-ore 50/分 | i-ore 採取 50。r-ore は稼働しない |
| GAT-03 | 超過分はレシピへ展開 | m-cap、i-ore 100/分 | i-ore 採取 60 + Recipe 40。r-ore が 40 サイクル/分で稼働 |
| GAT-04 | 代替レシピなしは未充足＋警告 | m-cap、i-shard 20/分（上限 10） | i-shard 採取 10、未充足 10、`GatherCapExceeded` 警告 |
| GAT-05 | 行のない採取素材は上限 0 | m-none、i-ore 50/分 | i-ore 採取 0、全量 r-ore で展開（50 サイクル/分） |
| GAT-06 | 無限行は上限なし | m-inf、i-ore 500/分 | i-ore 採取 500。警告なし |
| GAT-07 | ユーザー上書きは有効レートの置き換え | m-cap + i-ore 上書き 30、i-ore 50/分 | i-ore 採取 30 + Recipe 20 |
| GAT-08 | 上書きはマップ値超過も許可 | m-cap + i-ore 上書き 90、i-ore 100/分 | i-ore 採取 90 + Recipe 10 |
| GAT-09 | 行なしアイテムへ上書き | m-none + i-ore 上書き 20、i-ore 50/分 | i-ore 採取 20 + Recipe 30 |
| GAT-10 | 非有効イベントのマップは全採取不可＋上書き無効 | m-ev + i-ore 上書き 10、i-ore 50/分 | i-ore 採取 0、全量 r-ore 展開（50 サイクル/分）、`GatherMapUnavailable` 警告。上書きは適用されない |
| GAT-11 | 非有効イベントのマップで代替なしは未充足 | m-ev-shard、i-shard 20/分 | i-shard 採取 0、未充足 20、`GatherMapUnavailable`＋`GatherCapExceeded` 警告 |
| GAT-12 | 採取上限は需要の発生源を問わず（固定消費由来） | m-cap、固定消費で i-ore を消費するペアを持つレシピ目標 | i-ore 需要に対し採取 60 まで、超過は r-ore 展開 |
| GAT-13 | 採取上限は需要の発生源を問わず（環境消費由来） | m-cap、環境消費で i-ore を消費する環境を使うペア目標 | 同上 |
| GAT-14 | 超過レシピの入力も採取上限の対象（連鎖） | m-cap、i-ore 200/分 | i-ore 採取 60 + r-ore 展開 140。r-ore は i-stone を 140 需要するが上限 30 で採取 30、残りは i-stone の代替がないため未充足 110 + `GatherCapExceeded` |
| GAT-15 | 副産物は採取より先に残差を減らす | F-15 派生: i-ore を副産する r-side（i-stone×1 → i-part×1 + i-ore×30）、m-cap、i-part 需要と i-ore 100/分 | i-ore: 副産物 30 + 採取 60 + Recipe 10 |
| GAT-16 | 採取素材のレシピが循環する場合（Phase 18 で回帰確認として再掲。[test-specification-phase18.md](test-specification-phase18.md) §2） | F-15 派生: i-ore 上限超過の r-ore が i-x を要し i-x が i-ore を要する循環 | `CycleDetected` 警告、採取分は維持される |
| GAT-17 | 不明なマップ Id（暫定解釈） | MapId="m-ghost"、i-ore 50/分 | `InvalidGatherMap` 警告、i-ore 採取 0、全量 r-ore 展開 |
| GAT-18 | 不正な上書きは無視＋警告（暫定解釈） | m-cap、i-ore 上書き −5、存在しない i-ghost 上書き 10、非採取素材 i-part 上書き 10 | `InvalidGatherRateOverride` 警告。i-ore はマップ値 60 を使い i-ore 需要 100 → 採取 60 + Recipe 40 |
| GAT-19 | 採取素材でも所属イベントが非有効なら不可（X の維持） | i-ore を ev-off 所属にした派生、m-cap | `EventItemUnavailable` 警告、採取 0、未充足 |
| GAT-20 | 後の引き戻しで不足が解消された場合は警告を残さない | F-16（i-shard が一時不足→r-y 副産で i-x が充足され r-x が引き戻される）、m-g16、i-x→r-x 上書き | i-shard の要求行が消え、`GatherCapExceeded` は発行されない |

### GRI: 採取レート入力行のパース（Application）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| GRI-01 | 有効行は GatherRateOverride、空欄行は無視 | i-ore "30"、i-gas ""、i-shard "0" | override は i-ore=30・i-shard=0 の 2 件 |
| GRI-02 | 非数値・負・NaN はエラー | i-ore に "abc"、"−1"、"NaN" | いずれも false。メッセージにアイテム名 |
| GRI-03 | マップ値を超える値も受理 | i-ore "9999" | true、override i-ore=9999 |
| GRI-04 | 存在しない・採取素材でないアイテムはエラー | 不明 Id、i-part へ "10" | いずれも false |
| GRI-05 | 全行空欄は空の上書き列で成功 | 全行 "" | true、overrides 空 |

### SVC: サービス引数の通過（Application）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| SVC-04 | gatherOverrides と MapId が計算へ渡る | A-01 系＋マップ付きスナップショットで MapId・上書きを指定 | 採取上限どおりの結果が返る |

### REG: 既存ケースの期待値更新

採取優先への変更（仕様決定 AD）により、レシピを持つ採取素材の期待値を更新する。

| ID | 変更内容 |
|---|---|
| FIX-03 | i-fuel は採取優先により全量採取となる。`r-fuel` は稼働しない |
| CNV-01 | i-gasp は全量採取となる。`r-gasp` は稼働しない。環境消費の追加需要は収束する |
| VWU-06（新規） | 未調整ビューの余剰再計算に採取供給を含める。採取 60 + レシピ産出（切上げ後 45）で需要 100 → 余剰 5 |
| その他 | 採取素材がレシピ産出を持たない既存ケースの期待値は据え置き |

`ApplicationFixtures.Item` の `IsGatherable` は TransportKind 推定をやめ、明示引数とする。生産対象のアイテムは `false`（既定）、原材料は `true` を明示する。

## 4. 受け入れ条件との対応

- `dotnet test` が全緑。GAT・GRI・SVC の各ケースと既存ケース（REG の更新後期待値を含む）が通過する
- `tools/validate_master.py` がスキーマ適合を報告する（データ変更なし・回帰確認）
- 採取上限・代替展開・警告が仕様どおりに動く
