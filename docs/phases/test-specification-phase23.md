# Phase 23 テスト仕様

**対象フェーズ**: Phase 23（グラフの目標アイテム配置改善）
**前提ドキュメント**: [implementation-plan-phase23.md](implementation-plan-phase23.md)、[decision-records.md](../decision-records.md)（仕様決定 BF〜BH）
**関連ドキュメント**: [test-specification-phase15.md](test-specification-phase15.md)（FG 先行群）

> 本書は Phase 23 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。

## 1. モデル検査（`FlowGraphModelBuilderTests`）

| ID | 対象 | 条件 | 期待 |
|---|---|---|---|
| FG-08（改訂） | 層割りは出口側起点の最長距離（BF） | i-u→i-mid→i-t の直列、目標 i-t | gather が最左列、fac:f-b が fac:f-a より右、item:i-t が最大ランク（右端列） |
| FG-09（改訂） | 循環経路でも停止し、ループ内の目標は Layer0（BH） | i-a↔i-b の 2 設備循環、目標 i-a | 全ノードにランクが付き、item:i-a が最大ランク（右端列） |
| FG-10（改訂） | 未充足のみのアイテムも Layer0（BF） | 生産経路のない i-need、目標 i-need | item:i-need が最大ランク（単独ノードのためランク 0）で UnmetPerMinute=5 |
| FG-27 | 消費される目標は消費設備の直上流（BG） | 目標 i-t が別目標 i-z の素材でもある計画 | item:i-t は右端列に置かれず、fac:f-z の 1 つ左の列に置かれる |
| FG-28 | 未消費の副産物は Layer0（BF） | FG-05 と同形（副産物 i-s、目標 i-p） | item:i-s が item:i-p と同じ最大ランク（右端列） |
| FG-29 | 鎖の短い目標も右端に固定（BF） | 深さの異なる 2 目標（i-x は 1 段、i-t は 3 段） | item:i-x・item:i-t がともに最大ランク。item:i-x が生産設備より右 |
| FG-30 | 台数分ユニットは設備と同じ層規則（BF・AO） | A-02、expandFacilities | facunit:f-t#* がすべて同一ランクで item:i-t の 1 つ左 |
| FG-31 | 出力のない設備（散布機）は消費アイテムの直下流（BF） | FG-19 と同形（散布機が i-gas を消費） | fac:f-disp が item:i-gas の 1 列右。Layer0（目標と同列）に置かれない |
| FG-32 | 複数の出口を持つ循環は深い出口側へ緩和する（BH） | i-a↔i-b 循環＋i-a の浅い出口（i-z）と i-b の深い出口（i-m→i-w） | item:i-a が fac:f-b の左（供給エッジは後退しない）。後退エッジは item:i-b→fac:f-a のみ |

## 2. 手動確認項目

| ID | 内容 | 手順 | 期待 |
|---|---|---|---|
| MN-81 | 目標アイテムの配置 | 深さの異なる複数目標でグラフを表示する | 目標が右端列に揃い、素材チェーンが左へ積み上がる。未消費の副産物も右端列にまとまる |
| MN-82 | ループ内の目標 | 芽針↔芽針の種の循環を含む計画を表示する | 目標の芽針が右端列に留まり、採種機へ戻る後退エッジが見える |

## 3. 確認項目の結果記録

実装 PR の本文に、`dotnet test` の結果と §1〜§2 の各項目の合否を表で記録する。
