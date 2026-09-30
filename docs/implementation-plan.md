# 実装計画（Implementation Plan）

**対象アプリ**: アークナイツ：エンドフィールド AIC 計算 Web アプリケーション（次期版）
**前提ドキュメント**: [docs/requirements.md](requirements.md)（確定版仕様）、[docs/decision-records.md](decision-records.md)（仕様決定記録）
**参考実装**: [`Tkg-tamagohan/Endfield-AIC-Planner`](https://github.com/Tkg-tamagohan/Endfield-AIC-Planner)（旧 WPF 版、凍結）

> 本書は確定した要件をフェーズ別タスクに分解した作業計画である。
> 新しいセッションが本書と前提ドキュメントだけで作業を再開できる粒度を目指す。
> 各 Phase は原則 1PR 単位で完了させ、マージ済みの部分から再開可能な順序にしている。
> 進捗は本書のチェックリスト（`[ ]` / `[x]`）で管理する。

## 0. 環境・前提条件

| 項目 | 内容 |
|------|------|
| OS/ランタイム | UI が WPF ではなく Blazor WASM になったため、旧版と異なり全プロジェクトが Linux でもビルド・テスト可能。ブラウザ上の動作確認には Chrome が使える環境が必要（Devin の VM で検証可能）。 |
| SDK | .NET 8 SDK。Blazor WebAssembly スタンドアロン（サーバー側 ASP.NET Core を持たない構成）。 |
| 主要パッケージ | `Microsoft.NET.Sdk.BlazorWebAssembly`（SDK 同等のワークロード）、`xunit`（テスト）。MVVM Toolkit 等の UI 補助は Blazor の仕組みで代替し、旧版の `CommunityToolkit.Mvvm`・EF Core・SQLite は持ち込まない。バージョンは公開から 7 日以上経過した安定版を固定で使用する（浮動レンジ禁止）。 |
| クラウド | Cloudflare アカウント。Pages（静的配信）と Access（Zero Trust 無料枠・メール OTP）を使用。Pages はエッジ分散型でリージョン指定を持たないため、リージョン選定ルールの対象外。Devin 側には `CLOUDFLARE_API_TOKEN` の org シークレットが存在する。 |
| ブランチ戦略 | `devin/<unix-ts>-<slug>` 形式の作業ブランチ → main へ PR。Phase 単位で作成。 |

## 1. ソリューション構成

```
Endfield-AIC-Web/
├── EndfieldAicWeb.sln
├── src/
│   ├── EndfieldAicWeb.Domain/          # エンティティ・計算ロジック・検証。外部依存なし（旧 Core 移植・適合）
│   │   ├── Models/                     # Item, Recipe, Facility, Environment, GameEvent, RecipeFacility 等
│   │   ├── Calculation/                # PairSelector, ProductionCalculator 等
│   │   └── Validation/                 # MasterValidator, 検証エラー型
│   ├── EndfieldAicWeb.Application/     # ユースケース（計算実行・マスタ編集・エクスポートの入口）
│   ├── EndfieldAicWeb.Infrastructure/  # マスタ JSON 読み書き・アイコンマニフェスト解決
│   ├── EndfieldAicWeb.App/             # 計算アプリ（Blazor WASM、公開）
│   └── EndfieldAicWeb.Admin/           # マスタ管理ツール（Blazor WASM、非公開）
├── tests/
│   ├── EndfieldAicWeb.Domain.Tests/    # 計算・検証の単体テスト（旧 Core.Tests 移植＋新規）
│   └── EndfieldAicWeb.Infrastructure.Tests/ # JSON I/O・スキーマ検証のテスト
├── data/
│   ├── master.json                     # 正本 JSON（SchemaVersion=1 新系統、DataVersion 付き。仕様決定 C/D）
│   └── icons/                          # アイコン画像（<Key> 対応 PNG。権利クリアなもののみ、仕様決定 R）
└── docs/
    ├── requirements.md                 # 確定版要件定義書
    ├── decision-records.md             # 仕様決定記録
    ├── implementation-plan.md          # 本書
    ├── remaining-issues.md             # レビューで先送りした残課題
    └── phases/                         # Phase 別の実装詳細計画・テスト仕様
        ├── implementation-plan-phase<N>.md
        └── test-specification-phase<N>.md
```

**依存方向**: `App → Application → Domain` / `Admin → Application → Domain`、`Infrastructure → Domain`（要件 §6.2）。
Domain は UI・保存実装から完全に分離し、WASM 上でそのまま動く外部依存ゼロの純粋クラスライブラリとする。
旧リポジトリの `EndfieldAicPlanner.Core`（Models・Calculation・Validation）を土台に新モデルへ適合させる（仕様決定 C）。

**移植しないもの**（仕様決定 C/K/Q/W/Y/S）: EF Core・SQLite・Layout 系（Layout/PlacedObject/CollisionClass/PlacementGeometry）・WPF（App/Admin）・`RecordOrigin`・発電モデル（PowerCalculator の発電反復・`PowerSupplyRange`・発電レシピ規則）・合成設備の保持枠・ポート（`InternalSlots`/`InputPorts`/`OutputPorts`/`ReactorUnitPacker`）・`CollisionClass`・旧 JSON 相互互換。

## 2. データモデル実装詳細（requirements §5 準拠）

| エンティティ | フィールド | 備考 |
|---|---|---|
| 共通属性 | Id, Name, Description, IconKey(null 可), VersionAdded, VersionRemoved(null 可) | 全マスタエンティティに付与（仕様決定 N）。 |
| Item | 共通属性, Category, IsGatherable, TransportKind, GameEventId(null=常設) | Category は表示用タグ、`IsGatherable=true` が需要展開の終端（採取扱い。マップ選択時は採取上限が適用、仕様決定 AC/AD）。`TransportKind=None` は仮想アイテム（輸送容量対象外）。非有効イベント配下は生産・外部調達とも不可（仕様決定 X）。 |
| Environment | 共通属性, ProviderFacilityId, ConsumeItemId, ConsumeRatePerMinute, GameEventId(null=常設) | 環境を供給する設備（散布機）・継続消費アイテム・消費速度（個/分）を持つ（仕様決定 H、単位は AF）。カバー範囲は持たない（W）。 |
| Recipe | 共通属性, Inputs, Outputs, Facilities(RecipeFacility[]), GameEventId(null=常設) | `CycleTime`・`FacilityId` はレシピ本体からペアへ移動。Outputs は `ItemId＋Quantity＋SortOrder`（SortOrder=0 が主産物）。 |
| Facility | 共通属性, Width, Height, PowerConsumption | 縦横は「設備面積最小」最適化（F）のために保持。発電識別・保持枠・ポート・衝突クラスは持たない（G/W/Y）。 |
| RecipeFacility | RecipeId, FacilityId, CycleTime, EnvironmentId(null=不要), FixedConsumption(null 可) | レシピ×設備の紐付け。一意性は全要素の組で判定（仕様決定 P）。`FixedConsumption` は `(ItemId, 個/分)`（単位は AF）。 |
| GameEvent | Id, Name, ActiveFrom(null 可), ActiveTo(null 可) | 両 null は常設（仕様決定 T）。 |
| GameMap | 共通属性, GatherRates, GameEventId(null=常設) | マップごとの採取上限。`GatherRate` は `{ ItemId, IsUnlimited, RatePerMinute }`（IsUnlimited=true なら上限なし、false なら有限の正値の個/分）。採取素材のみを指し、同一マップ内で ItemId 重複不可（仕様決定 AC）。イベント限定マップは現状存在しないが同型で用意。 |
| マスタ文書 | SchemaVersion(=1), DataVersion, Items, Facilities, Environments, GameEvents, Recipes, Maps, Icons | スキーマは新系統で v1 に振り直す（C）。Icons は `Key/File/Sha256/Bytes` のマニフェスト（R で旧規約継承）。 |

## 3. 計算エンジン仕様（Domain.Calculation）

**入力**: `ProductionTarget[] { ItemId, 個/分 }`（複数目標可）、`ContextFilter { ActiveGameEventIds, MapId }`、アイテム単位のペア上書き `PairOverride[] { ItemId → 選択ペア }`、環境ごとの散布機台数上書き `EnvironmentCountOverride[] { EnvironmentId → 台数 }`、採取素材ごとのレート上書き `GatherRateOverride[] { ItemId → 個/分 }`（仕様決定 AE）。

**出力**: `ProductionPlan`

- `ItemRequirement[] { ItemId, 毎分要求量, 供給内訳(レシピ/副産物/採取素材), 未充足量 }`
- `FacilityRequirement[] { FacilityId, 実数台数, 切上げ台数 }` — 散布機を含む
- `RecipeRun[] { RecipeId, FacilityId, CyclesPerMinute }` — ペア単位で保持
- `EnvironmentRequirement[] { EnvironmentId, 散布機台数, 消費アイテム流量 }`
- `TotalPowerConsumption` — Σ(PowerConsumption × 切上げ台数)、散布機分を含む。発電側は計算しない（Q/Y）
- `Surplus[] { ItemId, 毎分余剰量 }`
- `FlowAdjustment[] { RecipeId, InputItemId, 要求流量(個/s), 推奨制限(個/s) }` — 「調整済」表示に使う（O）
- `Warning[]` — 循環依存・レシピ未登録・輸送容量超過・イベント非有効による未充足・採取上限超過で代替不可・収束失敗 等

**アルゴリズム概要**:

1. 目標から net demand マップを構築し、展開（選択ペアで仮実行し入力を需要へ加算）と引き戻し（後供給の副産物で過剰化した稼働の取り消し）を固定点まで反復する。骨格は旧 `ProductionCalculator` の Session を移植する。
2. 需要アイテムごとの選択は「レシピ → ペア」の二段とする。レシピはコンテキスト適格候補から `VersionAdded` 最新（同率は Id 昇順）を選び、そのレシピのペアから `CycleTime` 最小を選ぶ（F）。同一 (RecipeId, FacilityId) で属性の異なるペア行が複数ある場合も `CycleTime` 最小を既定とし、同率は `EnvironmentId=null` → `FixedConsumption` なし/小の順（U）。ペア上書きはペア行単位で適用し、不適格な上書きは警告して既定へフォールバックする。
3. 適格判定: レシピの `GameEventId` が非有効なら候補外。ペアの `EnvironmentId` が指す環境の `GameEventId` が非有効ならそのペアも候補外。アイテム自体の `GameEventId` が非有効なら生産・外部調達とも不可とし、需要は未充足＋警告とする（X）。選択したマップが非有効イベント所属の場合も同様に、全採取素材を採取不可（上限 0）として警告する（AD、X と同型）。この場合の採取レート上書きは適用しない。
4. 循環依存は展開スタック上の再要求で検出し、警告してその需要を未充足として打ち切る。副産物は他素材需要へ充当し、充当残は余剰として出力する。1 アイテムの需要を複数設備へ分割しない（R で旧 BE 継承）。
5. 設備台数はレシピのペアごとに `需要レート ÷ (60/CycleTime × 出力数量)` の実数を求め、設備単位に合算して切上げ台数を併記する。
6. 環境計上（I）: 稼働が確定したペアの `EnvironmentId` ごとに散布機台数を確定する。既定はその環境を必要とする稼働中レシピ数（レシピにつき 1 台）、ユーザー上書きを優先する。散布機は設備要件・消費電力に計上し、`ConsumeRatePerMinute × 台数` を環境の消費アイテム需要へ追加する（単位は AF）。
7. 固定消費（J/V）: 確定した各ペアの `FixedConsumption` について `個/分 × 切上げ台数` を需要へ追加する（単位は AF）。調整済モードでも基準は切上台数のままとする（V）。
8. 環境消費と固定消費の需要追加は台数確定後に行うため、これらの需要自体が新たなレシピ稼働（→台数変化）を生みうる。展開→台数確定→追加需要 の一巡を収束するまで反復する（上限は旧発電反復と同じく 10 回とし、収束しない場合は警告を返す）。技術的改善として旧電力収束ループの構造を流用する。
9. 採取素材は「個/分」のまま残す（R で旧 C 継承、AB で改称）。採取上限（AC/AD）: 選択マップの有効採取レート（ユーザー上書きを優先、未定義の行は「無限」か「上限値」、行のない採取素材は 0、マップ未選択は無制限）までを採取とし、超過分は当該アイテムを産出するレシピへ展開する。代替レシピがなければ未充足＋警告とする。輸送容量は `Item.TransportKind` でベルト 30 個/s・パイプ 60 個/s を判定し、超過は警告（超過自体は許容）。
10. 出力は未調整・調整済の両方を表示可能な形で返す。2 状態の切替は UI の表示切替であり、計算結果は共用する（O）。期間換算（M）は表示層で `個/分` に係数を掛けて行い、設備の消費電力合計は換算しない。

## 4. Phase 別タスク

### Phase 1: 基盤（PR: ソリューション雛形＋正本 JSON スキーマ＋CI）

- [x] `EndfieldAicWeb.sln` と 7 プロジェクト（src: Domain / Application / Infrastructure / App / Admin、tests: Domain.Tests / Infrastructure.Tests）を作成。App・Admin は Blazor WebAssembly スタンドアロン（net8.0）
- [x] NuGet パッケージ導入（固定バージョン）
- [x] `data/master.json` のスキーマ定義（`SchemaVersion=1`・`DataVersion`・Items・Facilities・Environments・GameEvents・Recipes・Icons）＋開発用最小サンプルデータ
- [x] `.github/workflows/ci.yml`（ubuntu-latest: restore → build → test）。旧版と異なり WPF 依存がないため Linux ランナーで完結する
- **受け入れ条件**: `dotnet test` が全緑で CI も緑。App・Admin が `dotnet run` でブラウザ表示できる。

### Phase 2: Domain（PR: モデル＋計算＋検証の移植適合＋単体テスト）

- [x] モデル移植・適合: §2 のエンティティ定義どおりに実装。削除対象（Origin・Layout 系・発電・保持枠/ポート・衝突クラス）は持ち込まない
- [x] 計算移植・適合: §3 の仕様どおり。旧 `ProductionCalculator` の展開・引き戻し・循環検出・副産物充当・輸送容量警告を骨格として移植し、ペア選択（F/U）・環境計上（I）・固定消費（J/V）・イベント不可扱い（T/X）・収束反復を新規実装する。`RecipeSelector` → ペア選択、`PowerCalculator` → 消費合計のみ、`ReactorUnitPacker` → 廃止
- [x] 検証移植・適合: `MasterValidator` を新モデルへ適合（必須項目・値域・参照整合性・ペア一意性（P）・enum 定義値）。仮想アイテム規則は「レシピ入力に仮想アイテムを含めない」を継承し、発電設備由来の規則は廃止する
- [x] 単体テスト: 旧 `Core.Tests` のゴールデンケース（直線チェーン / 多段依存 / 代替レシピ / 循環 / 副産物 / 切上げ流量調整 / 輸送容量）を移植し、ペア選択・環境（散布機台数の既定と上書き・ガス需要追加）・固定消費・イベント非有効アイテムの新規ケースを追加する。テストケースには ID を振り、文書を根拠に作成する（実装から期待値を逆引きしない）
- **受け入れ条件**: 全テスト緑。循環・レシピ未登録・イベント不可・収束失敗が例外ではなく Warning として返る。

### Phase 3: Infrastructure（PR: マスタ JSON I/O＋アイコンマニフェスト＋検証テスト）

- [x] `master.json` 読み込み: スキーマ・値域・参照整合性・ペア一意性の検証を `MasterValidator` と同一規則で行い、違反は例外ではなくエラー一覧として集約して返す
- [x] JSON エクスポート: 全置換形式で `SchemaVersion`/`DataVersion` 付きに書き出す（管理ツールのエクスポート物。仕様決定 D/R）
- [x] `Icons` マニフェスト解決: `Key/File/Sha256/Bytes` の照合検証と、`IconKey → ファイル` の解決（未設定・欠落時はフォールバック）
- [x] 検証テスト: 不正 JSON・参照不整合・未知 `SchemaVersion`・範囲外数値・ペア重複・マニフェスト不一致が明示的に拒否されることを確認する
- **受け入れ条件**: `dotnet test` 全緑。往復（読み込み→エクスポート）で内容が保存される。

### Phase 4: 計算アプリ UI（PR: 公開 Blazor WASM）

- [x] 入力画面: 複数目標（アイテム＋個/分）の追加・削除、有効イベントのコンテキスト切替、環境ごとの散布機台数入力（既定値の表示つき、I）
- [x] ペア代替選択: アイテム単位でペア行を切り替えられる。候補は環境の有無が分かるよう併記する（U）
- [x] 結果画面: 素材（供給内訳つき）・設備台数（実数/切上げ、散布機含む）・消費電力合計・余剰・警告の表示。「未調整／調整済」の 2 状態切替（O、既定は整数倍でない場合調整済）
- [x] 期間換算: 日・時・分で指定した期間の個数換算表示（M）
- [x] レスポンシブ対応（スマートフォンを含む、K）・日本語 UI・非公式ファンツールの明記
- [x] UI の確認はモックまたは起動可能な実装をブラウザプレビューでユーザーに触ってもらい、フィードバックを反映してからテスト作成に進む（テスト・検証手順は Phase 開始時にテスト仕様書へ分離してよい）
- **受け入れ条件**: プレビューでのユーザー確認を経て、ブラウザ（デスクトップ・スマホ幅）で主要フローが動作する。

### Phase 5: Cloudflare Pages デプロイ（PR: 公開アプリの配信）

- [x] Cloudflare Pages プロジェクトを作成し、公開アプリのビルド成果物を配信する。SPA/WASM 向けにフォールバック（`_redirects` 等）と必要ヘッダを設定する
- [x] 自動デプロイを構成する（Pages の Git 連携、または GitHub Actions から `CLOUDFLARE_API_TOKEN` を使う wrangler デプロイのいずれか。方式は Phase 開始時に確定する）
- [x] `data/master.json` と `data/icons/` がビルド成果物へ同梱されて配信されることを確認する（仕様決定 B/D）
- **受け入れ条件**: 公開 URL で計算アプリが実データ同梱のまま動作する。Pages はエッジ分散のためリージョン指定は行わない。

### Phase 6: 管理ツール UI（PR: Admin Blazor WASM＋Access 公開）

- [x] JSON 読み込み（デプロイ済み URL またはファイル選択）→ エンティティ編集 → 保存時に整合性検証（要件 §7）
- [x] 計算プレビュー: 編集中データで Domain の計算を実行し、投入データの妥当性を確認できる
- [x] JSON エクスポート（全置換、`DataVersion` 更新）
- [x] 別 Pages プロジェクト（専用ドメイン）へデプロイし、Cloudflare Access（メール OTP）で管理者のみに制限する（仕様決定 E）
  - `endfield-aic-admin` に初回デプロイ済み。Access（Allow + 管理者メールのみ・One-time PIN）は API で設定済み。未認証アクセスが Access ログインへ 302 されることを確認済み
- [x] Phase 4 と同様にプレビューでのユーザー確認を挟む
- **受け入れ条件**: 編集 → 検証 → プレビュー → エクスポートの一連が動作し、Access により非管理者が遮断される。

### Phase 7: アイコン画像＋実データ投入（PR: アイコンパイプライン＋初回データ）

- [x] 管理ツールにアイコン取り込み（128×128 PNG 正規化）・プレビュー・クリアを実装し、エクスポートで `icons/` フォルダとマニフェストを出力する
  - 正規化はブラウザ Canvas、エクスポートは `master-export.zip`（`data/master.json`＋`data/icons/`）で出力
- [x] 計算アプリ側の `IconKey` 表示（マニフェスト解決・フォールバック）を仕上げる
- [ ] 要件 §7 のワークフローどおり実データを投入し、エクスポート物を本リポジトリへコミット → CI 検証 → Pages 配信まで通す
  - ゲーム実データの投入は管理者の運用作業。パイプライン自体は旧リポジトリの権利クリア済みアイコン 5 件をサンプルデータへ移植し、エクスポート → コミット → CI 検証（`dotnet test` のアイコン照合と `validate_master.py` のスキーマ検証を含む）→ Pages 配信の経路を実データで通して検証済み
- **受け入れ条件**: エクスポート物のコミットで公開アプリへ実データが配信される。アイコン欠落時もプレースホルダで表示が破綻しない。

### Phase 8: アイコン正規化の改修（PR: 原寸保持＋アニメーション対応）

- [x] 正規化規則を「出力は一辺 128px 以下の正方形とし、128 ピクセル超は縮小・以下は拡大もパディングもせず原寸で保存」へ変更する（仕様決定 AA）
- [x] アニメーション画像（GIF・APNG 入力）をフレーム単位で同一規則に適用し APNG で取り込めるようにする（`UPNG.js`（MIT）を同梱。保存は `icons/<Key>.png`・`image/png` のまま）
- [x] ブラウザプレビューでのユーザー確認を経て、requirements §5.11・decision-records（AA）を含めて PR を作成する
- **受け入れ条件**: 128 ピクセル未満の画像が拡大されずに登録される。アニメーション画像が管理ツール・公開アプリの双方で動いて表示される。

### Phase 9: 用語・単位の整理（PR: 基礎素材→採取素材の改称＋レート単位の毎分統一）

- [x] 「基礎素材」→「採取素材」の改称（仕様決定 AB）。`Item.IsBaseMaterial` → `IsGatherable`、`SupplyKind.RawMaterial` → `Gathered`、JSON フィールド名・`Category` 値・UI ラベル・テスト・フィクスチャを追従する
- [x] レート単位の毎分統一（仕様決定 AF）。`Environment.ConsumeRatePerSecond` → `ConsumeRatePerMinute`、`FixedConsumption.RatePerSecond` → `RatePerMinute`。`data/master.json` の値を換算（6 → 360）、計算内の ×60 換算を除去する。出力側も `EnvironmentRequirement.ConsumeRatePerSecondTotal` → `ConsumeRatePerMinuteTotal` に改名して個/分へ統一し、Admin 入力ラベルと App の表示を個/分へ追従する
- [x] `SchemaVersion` は 1 のままとする（仕様決定 AF）
- **受け入れ条件**: `dotnet test` 全緑、`tools/validate_master.py` 通過。挙動変更を伴わない改名・単位変換のみ。

### Phase 10: マップモデル（PR: GameMap＋スキーマ＋管理ツールのマップ編集）

- [x] `GameMap` エンティティと `GatherRate` 行の新設（仕様決定 AC）。`MasterDocument.Maps`、`MasterDataSnapshot.Maps`、`MasterValidator` のマップ検証、JSON 入出力、`data/master.schema.json` の `Maps` 節を整備する
- [x] 管理ツールにマップ編集ページ（一覧・共通属性・採取レート行の編集）を追加し、ナビと文書件数表示を追従する。アイテム削除時の参照検出に採取レート行を含める
- [x] `data/master.json` に Maps サンプルを追加し、`tools/validate_master.py` の `ENTITY_SECTIONS` に `Maps` を加える
- **受け入れ条件**: `dotnet test` 全緑、`tools/validate_master.py` 通過。計算結果は変わらない（マップはまだ計算へ未接続）。

### Phase 11: 採取上限の計算（PR: 上限・代替レシピ展開・警告・ユーザー上書き）

- [ ] 計算入力に `ContextFilter.MapId` と `GatherRateOverride[]` を追加し、`ProductionCalculator` の採取素材終端処理を変更する（仕様決定 AD）。有効採取レート（上書き → マップ行 → 未定義は 0、無限行は上限なし、マップ未選択は無制限）までを採取とし、超過分を当該アイテムを産出するレシピへ展開、代替レシピなしなら未充足＋警告コード `GatherCapExceeded` とする。非有効イベント所属のマップが選択状態で残った場合は全採取素材を採取不可（上限 0）として警告し、採取レート上書きは適用しない
- [ ] 採取素材を上限まで採取し超過をレシピへ展開する採取優先へ変更する（従来のレシピ優先からの仕様変更。`FIX-03` 系の期待値を更新）
- [ ] `CalculationService` の引数に採取上書きを追加し、入力ビルダ（`CalculationInputBuilder` への採取レート入力パース）を Application に追加する
- [ ] Domain テストに ID 採番の新規ケースを追加する（上限内は採取、超過はレシピ、代替なしは未充足＋警告、ユーザー上書き、マップ未選択、未定義アイテム、無限行、非有効マップ選択時の採取不可と上書き無効化、連鎖展開・副産物・循環との相互作用）
- **受け入れ条件**: `dotnet test` 全緑。採取上限・代替展開・警告が仕様どおりに動く。

### Phase 12: 公開アプリ UI（PR: マップ選択＋採取レート入力）

- [ ] 入力パネルにマップ選択を追加する（未選択=無制限）。候補は有効イベントで絞り込み、非有効イベント所属のマップは候補から外す（イベント切替時の扱いは公開版・Admin 共通とする）
- [ ] 結果パネルに採取素材ごとの利用可能レート入力行を追加する（散布機台数入力と同型: 空欄=マップ既定値、再計算後も入力を保持。仕様決定 AE）
- [ ] 供給内訳の表示を採取素材へ追従する（表示名「採取素材」→「採取」等の整理、上限の可視化は必要に応じて）
- [ ] ブラウザプレビューでのユーザー確認を挟む（Phase 4 と同様）
- **受け入れ条件**: プレビューでのユーザー確認を経て、マップ選択・採取レート指定を含む主要フローが動作する。

### Phase 13: Admin 計算プレビューの公開版追従（PR: プレビュー機能の同等化）

- [ ] `PreviewPage` に公開版の機能を移植する: ペア代替選択ドロップダウン、散布機台数入力、単位切替（毎分/毎秒/期間）、期間入力（仕様決定 AG）
- [ ] Phase 12 で追加したマップ選択・採取レート入力を Admin 側にも実装する
- [ ] razor ページの共有化（RCL 化）は見送り、`remaining-issues.md` に残課題として記録する
- **受け入れ条件**: 公開版と同等の機能が管理ツールの計算プレビューで動作する。

## 5. 実装メモ・規約

- **NuGet**: 公開から 7 日以上経過した安定版のみ。`latest`/範囲指定禁止。新規ライブラリは導入前にライセンスを確認する。
- **テスト方針**: Domain の計算・検証を最も厚くする。テストケースは文書（requirements/decision-records）を根拠に作成し、ID を振って結果を表で報告する。各 Phase の詳細計画とテスト仕様は、Phase 開始時に `phases/implementation-plan-phase<N>.md`・`phases/test-specification-phase<N>.md` として切り出してよい（旧リポジトリと同じ慣行）。
- **UI の確認**: ユーザー操作を伴う画面は、テスト作成の前にブラウザプレビューでユーザーに実際に触ってもらい、フィードバックを反映する。
- **コミット**: Phase 内でも論理単位で分割する。UI 文字列・マスタデータは日本語のみ。
- **IP 配慮**: ゲーム画像素材は同梱しない。配布アイコンは自作・権利クリアなものに限る（仕様決定 R）。
- **Cloudflare**: Pages・Access はエッジ分散型でリージョン指定を持たない（リージョン選定ルール対象外）。無料枠を超過した場合は有料化せず機能制限を検討する（要件 §8）。

## 6. 再開手順（セッション引き継ぎ用）

1. 本書と `docs/requirements.md`、`docs/decision-records.md` を読む。
2. main のマージ済み PR を確認し、本書のチェックリスト残から次の未完了 Phase を特定する。
3. 新規ブランチ `devin/$(date +%s)-<phase-slug>` を作成し、当該 Phase のタスクを実施する。
4. PR を作成する。UI を含む Phase では、テスト前にブラウザプレビューでのユーザー確認を挟む。
5. Cloudflare 側の操作（Pages/Access の初期設定など、アカウント画面での作業）が必要な Phase では、必要な権限・シークレットを作業前に確認する。
