# Phase 2 テスト仕様書

**対象**: Phase 2 成果物（Domain のモデル・計算エンジン・検証）
**前提ドキュメント**: [requirements.md](requirements.md)、[decision-records.md](decision-records.md)、[implementation-plan.md](implementation-plan.md)、[implementation-plan-phase2.md](implementation-plan-phase2.md)

> 本書は Phase 2 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の数値を根拠に作成する。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。`tests/EndfieldAicWeb.Domain.Tests` に配置 |
| テストデータ | JSON を経由せず、メモリ内の `MasterDataSnapshot` をフィクスチャビルダーで構築する |
| 実行コマンド | `dotnet test` |
| 実行環境 | Linux（Domain は OS 非依存の純粋ロジック）。CI（ubuntu-latest）でも実行される |

数値の比較は浮動小数点誤差を考慮し、`Assert.Equal(expected, actual, precision)`（`precision = 6` 程度）で行う。

## 2. ゴールデンフィクスチャ定義

旧 `Core.Tests` の系統を新モデルへ翻訳し、ペア選択・環境・固定消費・イベント・収束の新規フィクスチャを追加する。
各フィクスチャは独立した `MasterDataSnapshot` として構築し、他フィクスチャとデータを共有しない。

環境・固定消費に関係しないフィクスチャ（F-01〜F-06、F-08）では全設備の `PowerConsumption = 0` とし、電力計算を結果に混入させない。
全フィクスチャ共通で、記載のない共通属性は `Description=""`・`IconKey=null`・`VersionAdded="1.0.0"`・`VersionRemoved=null`・`GameEventId=null` とする。

### F-01: 直線チェーン

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-ore` | Category=基礎素材、TransportKind=Belt |
| Item | `i-part` | Category=部品、TransportKind=Belt |
| Facility | `f-asm` | Width=3、Height=3、PowerConsumption=0 |
| Recipe | `r-part` | ペア (f-asm, CycleTime=4秒)、入力 `i-ore`×2、出力 `i-part`×1 |

目標: `i-part` を 30 個/分。

### F-02: 3段依存

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-a` `i-b` `i-c` | Category=部品、TransportKind=Belt |
| Item | `i-d` | Category=基礎素材、TransportKind=Belt |
| Facility | `f-a` `f-b` `f-c` | PowerConsumption=0 |
| Recipe | `r-a` | ペア (f-a, 5秒)、入力 `i-b`×2、出力 `i-a`×1 |
| Recipe | `r-b` | ペア (f-b, 10秒)、入力 `i-c`×3、出力 `i-b`×1 |
| Recipe | `r-c` | ペア (f-c, 12秒)、入力 `i-d`×4、出力 `i-c`×2 |

目標: `i-a` を 12 個/分。

### F-03: 代替レシピとペア選択

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-x` `i-y` `i-z` `i-w` `i-v` | Category=部品、TransportKind=Belt |
| Item | `i-ore-x` `i-gas-w` | Category=基礎素材、TransportKind=Belt（`i-gas-w` のみ Pipe） |
| Item | `i-fuel-w` | Category=基礎素材、TransportKind=Belt |
| Facility | `f-a` `f-b` `f-c` `f-disp` | PowerConsumption=0 |
| Environment | `env-w` | ProviderFacilityId=`f-disp`、ConsumeItemId=`i-gas-w`、ConsumeRatePerSecond=1 |
| Environment | `env-ltd` | ProviderFacilityId=`f-disp`、ConsumeItemId=`i-gas-w`、ConsumeRatePerSecond=1、GameEventId=`ev-off` |
| GameEvent | `ev-limited` `ev-off` | ActiveFrom/ActiveTo=null |
| Recipe | `r-x-old` | ペア (f-a, 6秒)、`i-ore-x`×1 → `i-x`×1、VersionAdded=`"1.0.0"` |
| Recipe | `r-x-new` | ペア (f-a, 6秒)・(f-b, 3秒)、`i-ore-x`×1 → `i-x`×2、VersionAdded=`"1.2.0"`、VersionRemoved=`"2.0.0"` |
| Recipe | `r-x-ltd` | ペア (f-a, 6秒)、`i-ore-x`×1 → `i-x`×4、VersionAdded=`"1.5.0"`、GameEventId=`ev-limited` |
| Recipe | `r-y-a` `r-y-b` | ともにペア (f-a, 6秒)、`i-ore-x`×1 → `i-y`×1、VersionAdded=`"1.0.0"`（同バージョン tie-break 用） |
| Recipe | `r-z-badver` | ペア (f-a, 6秒)、`i-ore-x`×1 → `i-z`×1、VersionAdded=`"latest"`（パース不能） |
| Recipe | `r-z` | ペア (f-a, 6秒)、`i-ore-x`×1 → `i-z`×1、VersionAdded=`"0.9.0"` |
| Recipe | `r-w` | ペア (f-a, 6秒, env=`env-w`)・(f-b, 6秒, FixedConsumption=`i-fuel-w`×0.5/s)・(f-c, 6秒)、`i-ore-x`×1 → `i-w`×1（同サイクルのタイブレーク用） |
| Recipe | `r-v-new` | ペア (f-a, 3秒, env=`env-ltd`)、`i-ore-x`×1 → `i-v`×1、VersionAdded=`"1.5.0"`（全ペア不適格ケース） |
| Recipe | `r-v-old` | ペア (f-a, 6秒)、`i-ore-x`×1 → `i-v`×1、VersionAdded=`"1.0.0"` |

### F-04: 循環依存

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-a` `i-b` `i-s` | Category=部品、TransportKind=Belt |
| Facility | `f-cyc` | PowerConsumption=0 |
| Recipe | `r-cyc-a` | ペア (f-cyc, 6秒)、`i-b`×1 → `i-a`×1 |
| Recipe | `r-cyc-b` | ペア (f-cyc, 6秒)、`i-a`×1 → `i-b`×1 |
| Recipe | `r-self` | ペア (f-cyc, 6秒)、`i-s`×1 → `i-s`×1（自己ループ） |

派生：直線チェーン併記版（F-04 + `i-ore`→`i-part` の F-01 相当）、副産物併記版（`r-x`: `i-ore`×1 → `i-x`×1+`i-a`×1）を旧版と同様に用意する。

### F-05: 副産物

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-p` `i-q` | Category=部品、TransportKind=Belt |
| Item | `i-orem` `i-oreq` | Category=基礎素材、TransportKind=Belt |
| Facility | `f-m` `f-q` | PowerConsumption=0 |
| Recipe | `r-m` | ペア (f-m, 4秒)、`i-orem`×1 → `i-p`×1+`i-q`×2、VersionAdded=`"0.9.0"` |
| Recipe | `r-q` | ペア (f-q, 6秒)、`i-oreq`×3 → `i-q`×1 |

派生：副産物が需要の一部のみ賄う版（`r-m` の `i-q` 副産が×1）を用意する。

### F-06: 切上げと流量調整

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-t` | Category=部品、TransportKind=Belt |
| Item | `i-u` | Category=基礎素材、TransportKind=Belt |
| Facility | `f-t` | PowerConsumption=0 |
| Recipe | `r-t` | ペア (f-t, 2秒)、`i-u`×4 → `i-t`×1 |

### F-08: 輸送容量

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-belt-item` `i-pipe-item` `i-none-item` | Category=部品、TransportKind=Belt/Pipe/None |
| Item | `i-belt-src` `i-pipe-src` `i-none-src` | Category=基礎素材、TransportKind=対応種別 |
| Facility | `f-tr` | PowerConsumption=0 |
| Recipe | `r-belt` `r-pipe` `r-none` | 各ペア (f-tr, 6秒)、src×1 → item×1 |

### F-10: 環境

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-ore` `i-gas` | Category=基礎素材、TransportKind=Belt/Pipe |
| Item | `i-hp` `i-std` | Category=部品、TransportKind=Belt |
| Facility | `f-asm` | PowerConsumption=50 |
| Facility | `f-disp` | PowerConsumption=20 |
| Environment | `env-gas` | ProviderFacilityId=`f-disp`、ConsumeItemId=`i-gas`、ConsumeRatePerSecond=6 |
| Recipe | `r-hp` | ペア (f-asm, 8秒)・(f-asm, 4秒, env=`env-gas`)、`i-ore`×1 → `i-hp`×1 |
| Recipe | `r-std` | ペア (f-asm, 5秒, env=`env-gas`)、`i-ore`×1 → `i-std`×1 |

### F-11: 固定消費

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-ore` `i-fuel` | Category=基礎素材、TransportKind=Belt |
| Item | `i-fc` | Category=部品、TransportKind=Belt |
| Facility | `f-fc` `f-fuel` | PowerConsumption=0 |
| Recipe | `r-fc` | ペア (f-fc, 30秒, FixedConsumption=`i-fuel`×0.1/s)、`i-ore`×1 → `i-fc`×1 |
| Recipe | `r-fuel` | ペア (f-fuel, 3秒)、`i-ore`×2 → `i-fuel`×1（燃料が自産できる派生用） |

### F-12: イベント限定アイテム

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-ltd` | Category=部品、TransportKind=Belt、GameEventId=`ev-ltd` |
| Item | `i-ltd-raw` | Category=基礎素材、TransportKind=Belt、GameEventId=`ev-ltd`（レシピなし） |
| Item | `i-ore` `i-fin` | Category=基礎素材/部品、TransportKind=Belt |
| Facility | `f-asm` | PowerConsumption=0 |
| GameEvent | `ev-ltd` | ActiveFrom/ActiveTo=null |
| Recipe | `r-ltd` | ペア (f-asm, 6秒)、`i-ore`×1 → `i-ltd`×1（レシピ自体は常設） |
| Recipe | `r-fin` | ペア (f-asm, 6秒)、`i-ltd`×2 → `i-fin`×1 |

### F-13: 収束

| 種別 | Id | パラメータ |
|---|---|---|
| Item | `i-ore` `i-gasp` | Category=基礎素材、TransportKind=Belt/Pipe |
| Item | `i-xp` `i-fuelself` | Category=部品、TransportKind=Belt |
| Facility | `f-xp` `f-mix` `f-disp` `f-self` | PowerConsumption=0 |
| Environment | `env-gasp` | ProviderFacilityId=`f-disp`、ConsumeItemId=`i-gasp`、ConsumeRatePerSecond=6 |
| Recipe | `r-xp` | ペア (f-xp, 4秒, env=`env-gasp`)、`i-ore`×1 → `i-xp`×1 |
| Recipe | `r-gasp` | ペア (f-mix, 6秒)、`i-ore`×1 → `i-gasp`×10（環境消費が生産へ展開する収束ケース） |
| Recipe | `r-self` | ペア (f-self, 60秒, FixedConsumption=`i-fuelself`×2/s)、`i-ore`×1 → `i-fuelself`×1（1台あたり生産 1個/分 < 消費 120個/分 で発散する作為的ケース） |

## 3. テスト項目一覧

### EXP: 需要展開と設備台数

| ID | 内容 | フィクスチャ・入力 | 期待 |
|---|---|---|---|
| EXP-01 | 直線チェーンの展開 | F-01、`i-part` 30/分 | `i-ore` 需要 60/分・供給 RawMaterial、`i-part` 供給 Recipe、レシピ稼働 30サイクル/分、f-asm 実数 2・切上 2、警告なし |
| EXP-02 | 多段依存の展開 | F-02、`i-a` 12/分 | `i-b` 24、`i-c` 72、`i-d` 144 個/分。f-a 1、f-b 4、f-c 実数 7.2・切上 8 |
| EXP-03 | 同一アイテムの複数目標は合算 | F-01、`i-part` 20+10/分 | 需要 30/分、稼働 30 サイクル/分 |
| EXP-04 | 設備台数の実数と切上 | F-02、`i-a` 12/分 | f-c 実数 7.2・切上 8 |
| EXP-05 | 目標なしは空結果 | F-01、目標なし | 全一覧が空、`TotalPowerConsumption`=0 |

### SEL: レシピとペアの選択（F/U）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| SEL-01 | 既定は VersionAdded 最新のレシピ | F-03、`i-x` 60/分 | `r-x-new` が選ばれ、ペアは CycleTime 最小の (f-b, 3秒)。f-b 実数 1.5・切上 2 |
| SEL-02 | イベント非有効レシピは候補外 | F-03、`i-x` 60/分（イベント無効） | `r-x-new`。`ev-limited` 有効時は `r-x-ltd` |
| SEL-03 | 同 VersionAdded は Id 昇順 | F-03、`i-y` 60/分 | `r-y-a` |
| SEL-04 | パース不能な VersionAdded は最古扱い＋警告 | F-03、`i-z` 60/分 | `InvalidVersionString` 警告、`r-z` 採用 |
| SEL-05 | ペア既定は CycleTime 最小 | F-03、`i-x` 60/分 | `r-x-new` のペア (f-b, 3秒) |
| SEL-06 | 同 CycleTime は env=null → FixedConsumption なし/小（U） | F-03、`i-w` 60/分 | ペア (f-c, 6秒, env=null, FixedConsumption=null)。環境・燃料需要は発生しない |
| SEL-07 | ペア上書きが適用される | F-03、`i-x` 60/分、`PairOverride(i-x → r-x-new の f-a, 6秒)` | ペア (f-a, 6秒) で稼働、f-b は使われない |
| SEL-08 | 不適格なペア上書きは警告＋既定 | F-03、`i-x`、存在しないペア・イベント無効レシピのペア・環境不適格ペアを指定 | `InvalidPairOverride` 警告、既定ペアで稼働 |
| SEL-09 | 最新レシピの全ペアが環境不適格なら次点レシピへ | F-03、`i-v` 60/分（`ev-off` 無効） | `r-v-old` が選ばれる（実装計画 §3 の暫定解釈） |
| SEL-10 | 同一レシピの別ペア衝突は先勝ち＋警告（旧 BN） | 同一レシピが `i-m`・`i-n` を出力し、各需要が別ペアを指す上書き | 先に確定したペアで稼働、`PairConflict` 警告 |
| SEL-11 | 引き戻しで休眠したペアの再稼働も稼働中ペアへ正規化 | F-14、`i-y` 10 + `i-m` 100 + `i-w` 10/分、`i-z` → ペア B 上書き | `PairConflict`、r-yz は f-b ペアのみ稼働、収束済み |

### CYC: 循環依存

| ID | 内容 | 期待 |
|---|---|---|
| CYC-01 | 相互循環は警告し残差を未充足へ | F-04、`i-a` 10/分 → `CycleDetected`、`i-a` 需要 20・未充足 10、`i-b` 需要 10・未充足 0 |
| CYC-02 | 自己ループ | F-04、`i-s` 10/分 → `CycleDetected`、未充足 10 |
| CYC-03 | 循環以外の需要は通常計算 | F-04 派生、`i-a` 10 + `i-part` 30 → `i-part` 未充足 0、f-asm 実数 2 |

### BYP: 副産物の充当と余剰

| ID | 内容 | 期待 |
|---|---|---|
| BYP-01 | 需要のない副産物は余剰 | F-05、`i-p` 15/分 → `i-q` 余剰 30/分、需要行なし |
| BYP-02 | 副産物が需要の一部を賄い残りは自レシピ | F-05、`i-p` 15 + `i-q` 50 → `i-q` 供給 Byproduct 30 + Recipe 20、f-q 実数 2 |
| BYP-03 | 副産物が需要を超えると自レシピ不稼働 | F-05、`i-p` 15 + `i-q` 20 → `i-q` 供給は Byproduct 30 のみ、余剰 10、`r-q` 不稼働 |
| BYP-04 | 同一レシピを複数需要が選択 | F-05、`i-q` の上書きを `r-m` のペアへ、`i-p` 15 + `i-q` 45 → `r-m` 22.5 サイクル/分、`i-p` 余剰 7.5 |
| BYP-05 | 目標順序で結果が変わらない | F-05、`i-q` 20 → `i-p` 15 の順でも BYP-03 と同じ帳簿 |
| BYP-06 | 部分副産物で先行稼働・外部調達が縮小 | F-05 派生、`i-q` 20 → `i-p` 15 → `r-q` 5 サイクル/分、`i-oreq` Raw 15 |
| BYP-07 | 循環未充足へ後から副産物が届くと未充足が縮小 | F-04 派生、`i-a` 10 + `i-x` 5 → `i-a` 需要 20・未充足 5 |

### FLW: 流量調整（O）

| ID | 内容 | 期待 |
|---|---|---|
| FLW-01 | 推奨制限は要求流量の実数値 | F-06、`i-t` 310/分 → `r-t`/`i-u` の制限 62/3 個/s、f-t 実数 31/3・切上 11 |
| FLW-02 | 推奨制限は丸めない | F-06、`i-t` 320/分 → 制限 64/3 個/s |
| FLW-03 | 整数台数なら調整行なし | F-06、`i-t` 300/分 → 調整なし、f-t 実数 10・切上 10 |
| FLW-04 | 実数流量をそのまま出力 | F-06、`i-t` 135/分 → 制限 9 個/s、f-t 実数 4.5・切上 5 |

### TRN: 輸送容量

| ID | 内容 | 期待 |
|---|---|---|
| TRN-01 | ベルト超過は警告（レーン数付き） | F-08、`i-belt-item` 1900/分 → `TransportCapacityExceeded`（2 レーン） |
| TRN-02 | パイプ超過は警告 | F-08、`i-pipe-item` 3700/分 → 警告（2 レーン） |
| TRN-03 | TransportKind=None は対象外 | F-08、`i-none-item` 5000/分 → 容量警告なし |
| TRN-04 | 上限ちょうどは警告なし | F-08、`i-belt-item` 1800/分 → 警告なし |

### ENV: 環境（I）

| ID | 内容 | 期待 |
|---|---|---|
| ENV-01 | 環境必要ペアで散布機・ガス・電力を計上 | F-10、`i-hp` 30/分 → ペア (f-asm,4秒,env) 採用、f-asm 2台、f-disp 1台、`i-gas` 需要 360/分（Raw）、`TotalPowerConsumption` 120 |
| ENV-02 | 散布機既定台数は環境を要する稼働中レシピ数 | F-10、`i-hp` 30 + `i-std` 12/分 → f-disp 2台、`i-gas` 720/分 |
| ENV-03 | 散布機台数の上書き | F-10、`i-hp` 30/分、`EnvironmentCountOverride(env-gas, 3)` → f-disp 3台、`i-gas` 1080/分、電力 160 |
| ENV-04 | ペア上書きで環境なし運用へ切替 | F-10、`i-hp` 30/分、ペア (f-asm,8秒) 上書き → f-disp 0（出力なし）、`i-gas` 需要なし、f-asm 実数 4 |
| ENV-05 | 環境が非有効イベントならペアは候補外 | F-10 変形（`env-gas` に `GameEventId`=`ev-off`）、`i-hp` 30/分 → ペア (f-asm,8秒) 採用、f-disp 出力なし |
| ENV-06 | 負の散布機台数上書きは警告＋既定台数 | F-10、`EnvironmentCountOverride(env-gas, -2)` → `InvalidEnvironmentOverride`、f-disp 1 台、`i-gas` 360/分 |

### FIX: 固定消費（J/V）

| ID | 内容 | 期待 |
|---|---|---|
| FIX-01 | 固定消費が切上台数比例で需要へ | F-11、`i-fc` 10/分 → f-fc 実数 5・切上 5、`i-fuel` 需要 30/分（0.1/s×5台） |
| FIX-02 | 基準は実数でなく切上台数（V） | F-11、`i-fc` 5.1/分 → f-fc 実数 2.55・切上 3、`i-fuel` 需要 18/分（0.1/s×3台） |
| FIX-03 | 固定消費素材の生産が展開され収束する | F-11 に r-fuel を加えた変形、`i-fc` 10/分 → `i-fuel` 供給は Recipe `r-fuel`、収束して全充足 |
| FIX-04 | 提供設備とレシピ設備が兼用なら散布機込みの切上台数が乗数 | F-11 変形（`env-fcx` の ProviderFacilityId=`f-fc`、r-fcx ペア (f-fc, 30秒, env, FixedConsumption=`i-fuel`×0.5/s)）、`i-fcx` 10/分 → f-fc 切上 6、`i-fuel` 需要 180/分 |

### EVT: イベント限定アイテム（T/X）

| ID | 内容 | 期待 |
|---|---|---|
| EVT-01 | イベント非有効アイテムの目標は未充足＋警告 | F-12、`i-ltd` 10/分（`ev-ltd` 無効） → `EventItemUnavailable`、未充足 10 |
| EVT-02 | 中間素材としても不可（生産も調達も不可） | F-12、`i-fin` 10/分（無効） → `i-ltd` 未充足 20、`i-fin` は帳簿上生産 10・未充足 0（需要の未充足はイベント不可アイテム側へ計上）、`EventItemUnavailable` |
| EVT-03 | イベント有効なら通常どおり生産 | F-12、`i-ltd` 10/分（`ev-ltd` 有効） → 全充足 |
| EVT-04 | 基礎素材でもイベント非有効なら外部調達不可 | F-12、`i-ltd-raw` 10/分（無効） → 未充足 10、`EventItemUnavailable` |
| EVT-05 | 副産物でイベント不可アイテムが生産されても需要は未充足 | F-12 変形（常設レシピ `r-side` が `i-side`×1+`i-ltd`×1 を生産）、`i-side` 10+`i-ltd` 10/分（無効） → `i-ltd` 未充足 10・供給内訳なし・余剰 10、`EventItemUnavailable` |

### CNV: 収束反復

| ID | 内容 | 期待 |
|---|---|---|
| CNV-01 | 環境消費が生産レシピへ展開して収束 | F-13、`i-xp` 30/分 → `i-gasp` 需要 360/分が `r-gasp` で生産（供給 Recipe）、f-mix 稼働、f-disp 1台 |
| CNV-02 | 収束しない場合は警告して結果を返す | F-13、`i-fuelself` 1/分 → `ConvergenceNotReached` 警告（例外ではない）。未収束でも最後に適用した需要が帳簿へ反映され、要求量は供給＋未充足と一致する |

### WRN: 警告と入力検証

| ID | 内容 | 期待 |
|---|---|---|
| WRN-01 | レシピなし部品は未充足＋警告（例外ではない） | `i-x` のみのスナップショット、`i-x` 10/分 → `NoRecipeAvailable`、未充足 10 |
| WRN-02 | レシピなし基礎素材は外部調達（警告なし） | `i-ore` のみ、10/分 → RawMaterial 10、警告なし |
| WRN-03 | 複数警告が同時に返る | F-04 + レシピなしアイテム、`i-a` 10 + `i-miss` 5 → `CycleDetected` と `NoRecipeAvailable` |
| WRN-04 | 不正な目標は ArgumentException | レート 0/負/非有限、アイテム未登録 → 例外 |
| WRN-05 | 未知環境への台数上書きは警告 | `EnvironmentCountOverride(env-none, 2)` → `InvalidEnvironmentOverride` |

### VAL: マスタ検証

| ID | 内容 | 期待 |
|---|---|---|
| VAL-01 | 正当なマスタはエラーなし | 最小構成の有効データ → エラー 0 件 |
| VAL-02 | 必須項目の欠落 | Id/Name/Category 等が空 → エラー |
| VAL-03 | enum 定義値外 | `TransportKind` に未定義値 → エラー |
| VAL-04 | 参照整合性 | レシピ入出力 ItemId・ペアの FacilityId/EnvironmentId/FixedConsumption.ItemId・環境の ProviderFacilityId/ConsumeItemId・各 GameEventId の未解決参照 → すべてエラー集約 |
| VAL-05 | ペア一意性（P） | 同一レシピ内に全要素同一のペアが 2 行 → エラー。CycleTime 等が 1 要素でも異なれば許容 |
| VAL-06 | ペアの RecipeId 整合 | ペアの `RecipeId` が所属レシピと不一致 → エラー |
| VAL-07 | 仮想アイテム規則 | `TransportKind.None` のアイテムをレシピ入力に含む → エラー（出力側は規則対象外） |
| VAL-08 | バージョン値域 | `VersionAdded` がパース不能、`VersionRemoved`<=`VersionAdded` → エラー |
| VAL-09 | イベント期間の値域 | `ActiveFrom`>=`ActiveTo` → エラー |
| VAL-10 | IconKey 文字種 | 英数字・ハイフン・アンダースコア 1〜64 文字以外 → エラー。null/空/予約キーは許容 |
| VAL-11 | ID の一意性 | 同一コレクション内で Id 重複 → エラー |
| VAL-12 | 数値域 | `CycleTime`/`Quantity`/`ConsumeRatePerSecond`/`RatePerSecond` が 0 以下・`Width`/`Height` 0 以下・`PowerConsumption` 負 → エラー |
| VAL-13 | ペア 0 件のレシピはエラー | `Facilities` が空のレシピ → エラー（スキーマ `minItems: 1` と同規則） |
| VAL-14 | 空 ItemId の入力でも検証は例外にならない | `Inputs` に空 `ItemId` の行を含むレシピ → エラー一覧として返る（例外を投げない）。行の数量エラーも ItemId 欠落と独立に集計される |

## 4. 受け入れ条件との対応

- 全項目緑であること。
- CYC・WRN・EVT・CNV の各項目で、例外ではなく Warning として返ることを確認する（Phase 2 受け入れ条件）。
