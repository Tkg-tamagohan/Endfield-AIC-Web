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
│   ├── EndfieldAicWeb.Admin/           # マスタ管理ツール（Blazor WASM、非公開）
│   └── EndfieldAicWeb.SharedUi/        # 計算ページ UI の共有 Razor Class Library（仕様決定 BB）
├── tests/
│   ├── EndfieldAicWeb.Domain.Tests/    # 計算・検証の単体テスト（旧 Core.Tests 移植＋新規）
│   ├── EndfieldAicWeb.Infrastructure.Tests/ # JSON I/O・スキーマ検証のテスト
│   ├── EndfieldAicWeb.Application.Tests/    # ユースケース層の単体テスト
│   └── EndfieldAicWeb.Admin.Tests/          # 管理ツール固有処理（アイコン正規化等）のテスト
├── tools/
│   └── validate_master.py              # マスタ JSON のスキーマ検証（CI・ローカル共通）
├── data/
│   ├── master.json                     # マスタ JSON（SchemaVersion=1 新系統、DataVersion 付き。仕様決定 C/D）
│   ├── master.schema.json              # マスタ JSON の構造定義
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
旧リポジトリの `EndfieldAicPlanner.Core`（Models・Calculation・Validation）を基礎に新モデルへ適合させる（仕様決定 C）。

**移植しないもの**（仕様決定 C/K/Q/W/Y/S）: EF Core・SQLite・Layout 系（Layout/PlacedObject/CollisionClass/PlacementGeometry）・WPF（App/Admin）・`RecordOrigin`・発電モデル（PowerCalculator の発電反復・`PowerSupplyRange`・発電レシピ規則）・合成設備の保持枠・ポート（`InternalSlots`/`InputPorts`/`OutputPorts`/`ReactorUnitPacker`）・`CollisionClass`・旧 JSON 相互互換。

## 2. データモデル実装詳細（requirements §5 準拠）

| エンティティ | フィールド | 備考 |
|---|---|---|
| 共通属性 | Id, Name, Description, IconKey(null 可), VersionAdded, VersionRemoved(null 可) | 全マスタエンティティに付与（仕様決定 N）。 |
| Item | 共通属性, Category, IsGatherable, TransportKind, GameEventId(null=常設) | Category は表示用タグ、`IsGatherable=true` が需要展開の終端（採取扱い。マップ選択時は採取上限が適用、仕様決定 AC/AD）。`TransportKind=None` は仮想アイテム（レシピ入力不可・生産リスト候補外）。非有効イベント配下は生産・外部調達とも不可（仕様決定 X）。 |
| Environment | 共通属性, ProviderFacilityId, ConsumeItemId, ConsumeRatePerMinute, CoverableMachines, GameEventId(null=常設) | 環境を供給する設備（散布機）・継続消費アイテム・消費速度（個/分）を持つ（仕様決定 H、単位は AF）。供給設備 1 台がカバーできる機械台数を `CoverableMachines`（正の整数）で持つ（仕様決定 BP）。カバー範囲（面積）は持たない（W）。 |
| Recipe | 共通属性, Inputs, Outputs, Facilities(RecipeFacility[]), GameEventId(null=常設) | `CycleTime`・`FacilityId` はレシピ本体からペアへ移動。Outputs は `ItemId＋Quantity＋SortOrder`（規約は 0 から連番を付け、SortOrder 最小の行が主産物）。0 件を許容し、出力なしレシピは処理レシピ（BZ）。 |
| Facility | 共通属性, Width, Height, PowerConsumption | 縦横は「設備面積最小」最適化（F）のために保持。発電識別・保持枠・ポート・衝突クラスは持たない（G/W/Y）。 |
| RecipeFacility | RecipeId, FacilityId, CycleTime, EnvironmentId(null=不要), FixedConsumption(null 可) | レシピ×設備の紐付け。一意性は全要素の組で判定（仕様決定 P）。`FixedConsumption` は `(ItemId, 個/分)`（単位は AF）。 |
| GameEvent | Id, Name, ActiveFrom(null 可), ActiveTo(null 可) | 両 null は常設（仕様決定 T）。 |
| GameMap | 共通属性, GatherRates, GameEventId(null=常設) | マップごとの採取上限。`GatherRate` は `{ ItemId, IsUnlimited, RatePerMinute }`（IsUnlimited=true なら上限なし、false なら有限の正値の個/分）。採取素材のみを指し、同一マップ内で ItemId 重複不可（仕様決定 AC）。イベント限定マップは現状存在しないが同型で用意。 |
| マスタ文書 | SchemaVersion(=1), DataVersion, Items, Facilities, Environments, GameEvents, Recipes, Maps, Icons | スキーマは新系統で v1 に振り直す（C）。Icons は `Key/File/Sha256/Bytes` のマニフェスト（R で旧規約継承）。 |

## 3. 計算エンジン仕様（Domain.Calculation）

**入力**: `ProductionTarget[] { ItemId, 個/分 }`（複数目標可）、`ContextFilter { ActiveGameEventIds, MapId }`、アイテム単位のペア上書き `PairOverride[] { ItemId → 選択ペア }`、環境ごとの散布機台数上書き `EnvironmentCountOverride[] { EnvironmentId → 台数 }`（指定台数 × `CoverableMachines` がその環境を要する機械数の上限。仕様決定 BR）、採取素材ごとのレート上書き `GatherRateOverride[] { ItemId → 個/分 }`（仕様決定 AE）。

**出力**: `ProductionPlan`

- `ItemRequirement[] { ItemId, 毎分要求量, 供給内訳(レシピ/副産物/採取素材), 未充足量 }`
- `FacilityRequirement[] { FacilityId, 実数台数, 切上げ台数 }`： 散布機を含む
- `RecipeRun[] { RecipeId, FacilityId, CyclesPerMinute }`： ペア単位で保持
- `EnvironmentRequirement[] { EnvironmentId, 散布機台数, 必要台数（自動値）, 消費アイテム流量 }`
- `TotalPowerConsumption`： Σ(PowerConsumption × 切上げ台数)、散布機分を含む。発電側は計算しない（Q/Y）
- `Surplus[] { ItemId, 毎分余剰量 }`
- `FlowAdjustment[] { RecipeId, InputItemId, 要求流量(個/分), 推奨制限(個/分) }`： 「調整済」表示に使う（O。単位は仕様決定 AM）
- `Warning[]`： 循環依存・レシピ未登録・イベント非有効による未充足・採取上限超過で代替不可・収束失敗 等

**アルゴリズム概要**。

1. 目標から net demand マップを構築し、展開（選択ペアで仮実行し入力を需要へ加算）と引き戻し（後供給の副産物で過剰化した稼働の取り消し）を固定点まで反復する。骨格は旧 `ProductionCalculator` の Session を移植する。
2. 需要アイテムごとの選択は「レシピ → ペア」の二段とする。レシピはコンテキスト適格候補から `VersionAdded` 最新、同率は実効出力レート降順、さらに実効入力レート昇順（`Inputs` 全量の 1 サイクル合計 ÷ 適格ペアの最小 `CycleTime`）、最後に `Id` 昇順を選び（F・BA・CL・CP）、そのレシピのペアから `CycleTime` 最小を選ぶ（F）。同一 (RecipeId, FacilityId) で属性の異なるペア行が複数ある場合も `CycleTime` 最小を既定とし、同率は `FixedConsumption` なし/小 → `EnvironmentId=null` の順（U・BT）。ペア上書きはペア行単位で適用し、不適格な上書きは警告して既定へフォールバックする。
3. 適格判定: レシピの `GameEventId` が非有効なら候補外。ペアの `EnvironmentId` が指す環境の `GameEventId` が非有効ならそのペアも候補外。アイテム自体の `GameEventId` が非有効なら生産・外部調達とも不可とし、需要は未充足＋警告とする（X）。選択したマップが非有効イベント所属の場合も同様に、全採取素材を採取不可（上限 0）として警告する（AD、X と同型）。この場合の採取レート上書きは適用しない。
4. 循環依存は展開スタック上の再要求で検出し、検出パスとループゲイン（パス上のレシピ比率の積）を記録する。枝が生きている（枝アイテムがまだ供給を要する）循環がすべて正味増なら残差を解放して再展開し、外部投入なしの定常解へ収束させる。解けない循環（正味減を含む・非収束）は最終の未充足へ警告を出す（AQ）。副産物は他素材需要へ充当し、充当残は余剰として出力する。1 アイテムの需要を複数設備へ分割しない（R で旧 BE 継承）。
5. 設備台数はレシピのペアごとに `需要レート ÷ (60/CycleTime × 出力数量)` の実数を求め、設備単位に合算して切上げ台数を併記する。
6. 環境計上（I・BP〜BR）: 上書きされた散布機台数は `台数 × CoverableMachines` をその環境を要する機械数の上限として手順 1 の需要展開へ適用し、上限を超える機械分は稼働させず未充足とする（BR）。稼働が確定したペアの `EnvironmentId` ごとに機械数合計（実数）を集計し、散布機台数を確定する。既定は機械数合計 ÷ `CoverableMachines` の切上げ（BQ）、ユーザー上書きを優先する。カバー不足で稼働が停止した要求を持つ環境も環境要件の行に含め、その必要台数（自動値）は実績と停止分の機械数合計から見積もる。散布機は設備要件・消費電力に計上し、`ConsumeRatePerMinute × 台数` を環境の消費アイテム需要へ追加する（単位は AF）。
7. 固定消費（J/V）: 確定した各ペアの `FixedConsumption` について `個/分 × 切上げ台数` を需要へ追加する（単位は AF）。調整済モードでも基準は切上台数のままとする（V）。
8. 環境消費と固定消費の需要追加は台数確定後に行うため、これらの需要自体が新たなレシピ稼働（→台数変化）を生みうる。展開→台数確定→追加需要 の一巡を収束するまで反復する（上限は旧発電反復と同じく 10 回とし、収束しない場合は警告を返す）。技術的改善として旧電力収束ループの構造を流用する。
9. 採取素材は「個/分」のまま残す（R で旧 C 継承、AB で改称）。採取上限（AC/AD）: 選択マップの有効採取レート（ユーザー上書きを優先、未定義の行は「無限」か「上限値」、行のない採取素材は 0、マップ未選択は無制限）までを採取とし、超過分は当該アイテムを産出するレシピへ展開する。代替レシピがなければ未充足＋警告とする。
10. 余剰の処理（BZ〜CC）: 収束反復の中で、最終的な余剰（生産−需要。処理で計上済みの需要を除いた帳簿で評価）のうち処理レシピ（`Outputs` が 0 件のレシピ）の入力に登場するアイテムへ処理ランを追加する。処理量は余剰全量、計算目標のアイテムは対象外、処理需要は反復ごとに現在余剰へ追随して更新する。処理レシピ・ペアの既定選択は F・BA・CL・CP・U・BT の準用（実効出力レートの代わりに対象アイテムの実効処理レートを第 2 キーとし、実効入力レートは対象アイテムと補助入力を含む全入力で算出して第 3 キーとする）。ランのサイクル数は処理対象の入力すべての残り余剰を上限とし（残り余剰のない他入力は補助入力として通常の需要）、同一処理レシピは 1 ランにまとめる。処理ランは選択ペアを保持して設備台数・消費電力・固定消費・環境要件へ通常ランと同じ規則で計上し、処理による消費を需要として計上する（最終余剰は余剰一覧から消える）。
11. 出力は未調整・調整済の両方を表示可能な形で返す。2 状態の切替は UI の表示切替であり、計算結果は共用する（O）。期間換算（M）は表示層で `個/分` に係数を掛けて行い、設備の消費電力合計は換算しない。

## 4. Phase 別タスク

### Phase 1: 基盤（PR: ソリューション雛形＋マスタ JSON スキーマ＋CI）

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
- [x] 要件 §7 のワークフローどおり実データを投入し、エクスポート物を本リポジトリへコミット → CI 検証 → Pages 配信まで通す
  - ワークフローの実証は済み（DataVersion 0.2.x のエクスポート物をコミット済み）。実データの継続投入は要件 §7 の運用作業として扱い、本 Phase の完了条件とはしない
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

- [x] 計算入力に `ContextFilter.MapId` と `GatherRateOverride[]` を追加し、`ProductionCalculator` の採取素材終端処理を変更する（仕様決定 AD）。有効採取レート（上書き → マップ行 → 未定義は 0、無限行は上限なし、マップ未選択は無制限）までを採取とし、超過分を当該アイテムを産出するレシピへ展開、代替レシピなしなら未充足＋警告コード `GatherCapExceeded` とする。非有効イベント所属のマップが選択状態で残った場合は全採取素材を採取不可（上限 0）として警告し、採取レート上書きは適用しない
- [x] 採取素材を上限まで採取し超過をレシピへ展開する採取優先へ変更する（従来のレシピ優先からの仕様変更。`FIX-03` 系の期待値を更新）
- [x] `CalculationService` の引数に採取上書きを追加し、入力ビルダ（`CalculationInputBuilder` への採取レート入力パース）を Application に追加する
- [x] Domain テストに ID 採番の新規ケースを追加する（上限内は採取、超過はレシピ、代替なしは未充足＋警告、ユーザー上書き、マップ未選択、未定義アイテム、無限行、非有効マップ選択時の採取不可と上書き無効化、連鎖展開・副産物・循環との相互作用）
- **受け入れ条件**: `dotnet test` 全緑。採取上限・代替展開・警告が仕様どおりに動く。

### Phase 12: 公開アプリ UI（PR: マップ選択＋採取レート入力）

- [x] 入力パネルにマップ選択を追加する（未選択=無制限）。候補は有効イベントで絞り込み、非有効イベント所属のマップは候補から外す（イベント切替時の扱いは公開版・Admin 共通とする）
- [x] 結果パネルに採取素材ごとの利用可能レート入力行を追加する（散布機台数入力と同型: 空欄=マップ既定値、再計算後も入力を保持。仕様決定 AE）
- [x] 供給内訳の表示を採取素材へ追従する（表示名「採取素材」→「採取」等の整理、上限の可視化は必要に応じて）
- [x] ブラウザプレビューでのユーザー確認を挟む（Phase 4 と同様）
- **受け入れ条件**: プレビューでのユーザー確認を経て、マップ選択・採取レート指定を含む主要フローが動作する。

### Phase 13: Admin 計算プレビューの公開版追従（PR: プレビュー機能の同等化）

- [x] `PreviewPage` に公開版の機能を移植する: ペア代替選択ドロップダウン、散布機台数入力、単位切替（毎分/毎秒/期間）、期間入力（仕様決定 AG）
- [x] Phase 12 で追加したマップ選択・採取レート入力を Admin 側にも実装する
- [x] razor ページの共有化（RCL 化）は見送り、`remaining-issues.md` に残課題として記録する
- **受け入れ条件**: 公開版と同等の機能が管理ツールの計算プレビューで動作する。

### Phase 14: 管理ツールのアイテム選択簡易化（PR: カテゴリ補完＋レシピの絞り込み）

- [x] アイテム編集のカテゴリ欄を `<input>`＋`<datalist>` の補完にし、既存カテゴリを候補から選べるようにする（自由入力は維持）
- [x] レシピ編集の入力、出力、固定消費のアイテム選択を、各欄に独立したカテゴリ絞り込みで絞れるようにする
- [x] 絞り込みと候補列挙の判定を Application の純粋関数（`ItemCatalog`）に置き、xUnit でカバーする
- [x] ブラウザプレビューでのユーザー確認を挟む（Phase 4 と同様）
- **受け入れ条件**: カテゴリを候補から選べて自由入力も残り、レシピのアイテム選択をカテゴリで絞り込める。マスタ構成は不変。

### Phase 15: 生産フローグラフの WebGPU 表示（PR: 結果画面の有向グラフ）

- [x] `FlowGraphModelBuilder`（Application）で `ProductionPlan` からノード（アイテム・設備・採取）とエッジ（レシピ入出力・固定消費・環境消費・採取）を組み立て、最長パスで層割りする。xUnit でカバーする
- [x] `wwwroot/js/flow-graph.js` を新設し、vanilla WebGPU＋WGSL でエッジ帯と流量比例の粒子を描画する。パン・ズーム・DOM ノード配置を持ち、非対応環境は `null` 返却でフォールバックする
- [x] `Components/FlowGraph.razor` と `Home.razor` に組み込み、ツールバーの切替・ノードクリックからリスト行へのスクロールを実装する（仕様決定 AI・AJ・AK・AL）
- [x] ブラウザプレビューでのユーザー確認を挟む（Phase 4 と同様）
- **受け入れ条件**: 対応ブラウザでグラフが描画されて粒子が流れ、非対応環境はリスト表示のみにフォールバックする。詳細は `phases/implementation-plan-phase15.md` と `phases/test-specification-phase15.md`。

### Phase 16: 輸送容量と推奨流量制限の単位改訂（PR: 個/分への統一）

- [x] 輸送容量を個/分へ改訂する（ベルト 30 個/分・パイプ 60 個/分、仕様決定 AM）。容量定数・警告判定・警告文・`FlowGraphModelBuilder` の超過判定を追従する
- [x] `FlowAdjustment` の要求流量・推奨制限を個/分へ改訂する（仕様決定 AM）。フィールド名・UI 表示行を追従する
- [x] 改訂で新たに容量警告が発火する既存テストを仕様に沿って見直し、`dotnet test` 全緑を確認する
- **受け入れ条件**: 警告文・推奨流量制限の表示が個/分表記になり、容量超過判定が 30/分・60/分 で発火する。詳細は `phases/implementation-plan-phase16.md` と `phases/test-specification-phase16.md`。

### Phase 17: グラフの輸送容量チェック絞り込みと設備台数分表示（PR: 判定対象と台数分展開）

- [x] グラフの容量超過判定を、設備への入力エッジ（レシピ入力・固定消費・環境消費）単位の流量と容量の比較へ絞り込む（仕様決定 AN）。超過エッジと両端ノードを赤化する
- [x] 設備ノードを実設置台数ぶん個別に描く切替をグラフに追加する（仕様決定 AO）。ユニットへラン占有を逐次充填して流量を分割し、散布機は環境ごとの専用ユニットとする
- [x] `dotnet test` 全緑を確認する
- **受け入れ条件**: 設備入力だけが容量超過で赤化し、「設備を台数分表示」の切替でユニットノードとユニット単位の判定へ切り替わる。詳細は `phases/implementation-plan-phase17.md` と `phases/test-specification-phase17.md`。

### Phase 18: 正味増循環の定常解（PR: 種↔作物循環を解く計算拡張）

- [x] 循環検出時の即時警告をやめ、検出パスとループゲイン（パス上のレシピ比率の積）を記録する。ループゲイン 1 未満の正味増循環では、打ち切った残差を均衡化ループ内で解放・再展開して外部投入なしの定常解を求める（仕様決定 AQ）
- [x] `CycleDetected` 警告を、循環未充足が残ったアイテムに対して均衡化後の確定時に遅延発行する。ループゲイン 1 以上・残差が縮まない循環は従来どおり未充足＋警告を維持する（仕様決定 AQ）。初期在庫は計算の対象外とする（仕様決定 AR）
- [x] `dotnet test` 全緑を確認する
- **受け入れ条件**: 同梱マスタの炭塊・息壌が種↔作物循環を経由して未充足なしで充足し、ゲイン 1 以上の循環は従来の挙動を維持する。詳細は `phases/implementation-plan-phase18.md` と `phases/test-specification-phase18.md`。

### Phase 19: アイテム選択のカテゴリ絞り込み全展開（PR: カテゴリ絞り込み＋母集団制限）

- [x] すべての素材入力・アイテム検索窓にカテゴリ絞り込みを実装し、カテゴリ欄とアイテム欄を横並びに配置する（仕様決定 AS）。レシピ編集は欄ごと共有の絞り込みから行ごとの絞り込みへ置き換え、アイテム一覧の検索窓にもカテゴリ絞り込みを追加する
- [x] アイテム選択肢の母集団を制限する（仕様決定 AT）。生産リスト（公開アプリ・管理ツール計算プレビュー）はレシピ出力のあるアイテムのみ、環境消費・固定消費は気体・液体のみとする
- [x] 母集団制限と絞り込みの判定を Application の純粋関数（`ItemCatalog`・`ItemSearch`）に置き、xUnit でカバーする
- [x] ブラウザプレビューでのユーザー確認を挟む（Phase 4 と同様）
- **受け入れ条件**: すべての選択窓でカテゴリ絞り込みと横並び配置が使え、母集団制限が各欄に適用される。マスタ構成は不変。詳細は `phases/implementation-plan-phase19.md` と `phases/test-specification-phase19.md`。

### Phase 20: 生産リスト行の改修と選択窓・既定値の調整

- [x] 公開アプリ生産リストのアイテム選択をテキスト検索コンボからネイティブ select ペアへ置き換え、レート欄が常に見える横並びレイアウトにする（仕様決定 AU）
- [x] 選択窓の空値ラベルを「カテゴリ」「アイテム」に改め（仕様決定 AX）、期間・レート入力欄を 3 桁余裕へ縮小する（仕様決定 AY）
- [x] 新規エンティティの `VersionAdded` 既定値を 1.0.0 に（仕様決定 AV）、設備ペアの新規サイクル秒を 2 に（仕様決定 AW）、生産リストの既定レートを 30 個/分に（仕様決定 AZ）
- [x] `dotnet test` 全緑を確認する

- **受け入れ条件**: 公開 App の生産リスト行が管理ツール計算プレビューと同型で表示され、各既定値が改訂どおりになる。詳細は `phases/implementation-plan-phase20.md` と `phases/test-specification-phase20.md`。

### Phase 21: 既定レシピ選択への必要設備数条件の追加（PR: 同バージョンレシピの効率順位付け）

- [x] 適格レシピの既定順を `VersionAdded` 降順 → 実効出力レート降順 → `Id` 昇順へ改める（仕様決定 BA）
- [x] `PairSelector` の候補順序付けに実効レート比較を追加し、選択と UI 候補順を同一規則に保つ
- [x] 選択規則のテスト（SEL-14〜）を追加する
- [x] requirements.md・decision-records.md を同期する
- **受け入れ条件**: `VersionAdded` 同率のレシピ間で実効出力レートの高いレシピが既定になり、同梱マスタで炭塊の既定が芽針系になる。詳細は `phases/implementation-plan-phase21.md` と `phases/test-specification-phase21.md`。

### Phase 22: 公開版と管理ツールの計算ページ UI の共有化

- [x] 共有 Razor Class Library `EndfieldAicWeb.SharedUi` を新設し、計算ページ本体を単一コンポーネントとして移設する（仕様決定 BB）
- [x] `EntityIcon`・`FlowGraph`・`flow-graph.js` を共有ライブラリへ集約し、共有スタイルは CSS isolation へ移す
- [x] 管理ツールの計算プレビューで生産フローグラフを有効化する（仕様決定 BC）
- [x] 表示文言を「天然資源」へ統一し（仕様決定 BD）、計算実行の例外を常に捕捉して入力エラー欄へ表示する（仕様決定 BE）
- [x] Phase 20 で参照を失った `ItemSearch` とそのテストを削除する
- [x] `dotnet test` 全緑を確認する
- **受け入れ条件**: 公開版と管理ツールの計算ページが同一の共有コンポーネントで描かれ、両者の既存機能と Admin 側グラフが動作する。詳細は `phases/implementation-plan-phase22.md` と `phases/test-specification-phase22.md`。

### Phase 23: グラフの目標アイテム配置改善

- [x] 層割りを「消費されないアイテムを Layer0 とする出口側起点の最長距離」へ改め、Layer0 を右端列に表示する（仕様決定 BF）
- [x] 消費される目標アイテムは大きい層へまとめる規則を適用し、消費設備の直上流に置く（仕様決定 BG）
- [x] 循環の後退エッジは層割りに使わない扱いを維持し、ループ内の目標は Layer0 に留める（仕様決定 BH）
- [x] `dotnet test` 全緑を確認する
- **受け入れ条件**: 目標アイテムが常にグラフ右端列に現れ、素材チェーンが左へ積み上がる。ループ内の目標も右端に留まり、後退エッジで循環が見える。詳細は `phases/implementation-plan-phase23.md` と `phases/test-specification-phase23.md`。

### Phase 24: グラフの縦方向表示・描画領域拡大・タッチズーム

- [x] 表示方向の横⇄縦切替を追加し既定を縦とする（仕様決定 BJ）。rank→行・order→行内横位置へ転置し、エッジはソースノード上辺中央→ターゲットノード下辺中央に接続する
- [x] 領域下端のドラッグハンドルによる高さ調整と、画面全面へ広げる最大化ボタンを追加する（仕様決定 BK）
- [x] ピンチズーム（2 点タッチ・中点アンカー）と画面上の＋・−・フィットボタンを追加し、`touch-action: none` の適用範囲を領域全体とノードへ広げる（仕様決定 BL）。`ZOOM_MIN` を 0.2 へ緩和する
- [x] `dotnet test` 全緑を確認し、ブラウザ E2E（縦配置・リサイズ・最大化・ピンチとボタンズーム）を実施する
- **受け入れ条件**: 既定で縦方向のグラフが描画され、横への切替・高さ調整・最大化・ピンチとボタンによる拡大縮小が動作する。詳細は `phases/implementation-plan-phase24.md` と `phases/test-specification-phase24.md`。

### Phase 25: 環境設備の層・同名レシピの説明併記・採取ノード撤去

- [x] 出力を持たない環境の供給設備（散布機）を、その環境を利用する設備の最小層と同じ層に置き、消費アイテムをその設備の直上流に置く。行内順は利用設備の隣へ寄せる（仕様決定 BM）
- [x] 計算ページでレシピ名を表示する全箇所に `Description` 非空時の「名前（説明）」併記を適用する（仕様決定 BN）
- [x] 共通採取ノードと Gathered エッジを廃止し、採取供給のあるアイテムノードを緑色化して採取量をメタ行に併記する（仕様決定 BO）
- [x] `dotnet test` 全緑を確認し、ブラウザ E2E（環境設備の層・同名レシピの識別・採取素材の緑化）を実施する
- **受け入れ条件**: 散布機が利用設備の層に並びガス系エッジが上向きに流れる。炭塊の 2 経路が説明で見分けられる。採取素材が緑ノードとして個別に見える。詳細は `phases/implementation-plan-phase25.md` と `phases/test-specification-phase25.md`。

### Phase 26: 散布機のカバー可能台数（PR: CoverableMachines と台数の機械数比例化）

- [x] `Environment` に `CoverableMachines`（正の整数）を追加する（仕様決定 BP）。モデル・スキーマ・JSON 入出力・`MasterValidator`・`EntityFactory`・管理ツールの環境編集に追従させ、同梱マスタの env-stable・env-acrid へ 4 を投入する
- [x] 散布機の既定台数を「環境を要する機械数合計 ÷ `CoverableMachines` の切上げ」へ改め、環境要件に必要台数（自動値）を保持する（仕様決定 BQ、I の改定）
- [x] 散布機台数の上書きを機械数のカバー上限として需要展開へ反映し、超過分を未充足＋カバー不足警告とする（仕様決定 BR）
- [x] `dotnet test` 全緑を確認し、ブラウザ E2E（台数の機械数比例・カバー不足時の未充足と警告）を実施する
- **受け入れ条件**: 息壌 150/分 で散布機が自動 2 台になり、台数を下げるとカバー不足分が未充足として出る。詳細は `phases/implementation-plan-phase26.md` と `phases/test-specification-phase26.md`。

### Phase 27: 散布機台数の入力範囲（PR: 下限=必要台数・上限=利用機械数）

- [x] 計算ページの散布機台数の入力範囲を `0〜必要台数` から `必要台数〜ceil(利用機械数)` へ改め、必要台数未満（0 台を含む）と上限超過を入力エラーとする（仕様決定 BS）。計算エンジンの上書き受理・カバー不足計算は従来どおりの防御的経路として残す
- [x] 環境要件へ利用機械数合計（実数）を出力し、入力範囲の表示（「（自動 N 台・N〜M 台）」系）とヒントを更新する
- [ ] `dotnet test` 全緑を確認し、ブラウザ E2E（範囲表示・入力エラー・台数超過時のガス消費）を実施する
- **受け入れ条件**: 息壌 150/分 で入力範囲が 2〜5 台になり、3〜5 台にするとガス消費・消費電力が台数比例で増える。詳細は `phases/implementation-plan-phase27.md` と `phases/test-specification-phase27.md`。

### Phase 28: ペア既定選択の同率キー順（PR: 固定消費キーの先行化）

- [x] 既定ペアの同率キーを `EnvironmentId=null` → `FixedConsumption` なし/小 から、`FixedConsumption` なし/小 → `EnvironmentId=null` へ入れ替える（仕様決定 BT、U の改定）
- [x] `dotnet test` 全緑を確認する。同梱マスタに複数ペア行を持つレシピはなく、単体テストが検証の主経路
- **受け入れ条件**: 同 `CycleTime` で「環境あり・固定消費なし」と「環境なし・固定消費あり」のペアが競合したとき、環境ありのペアが既定になる。詳細は `phases/implementation-plan-phase28.md` と `phases/test-specification-phase28.md`。


### Phase 29: レシピの Id・名前の自動入力（PR: 主産物からの自動提案）

- [x] 管理ツールのレシピ編集で Id・名前を主産物から自動提案し、直近の提案値と一致する間は主産物の変更に追随、手動編集で固定とする（仕様決定 BU）。提案値へ戻すボタンも設ける
- [x] レシピの新規 Id 採番を `recipe-NNN` から `recipe-<slug>`（衝突時のみ 2 桁連番）へ置き換える
- [x] `dotnet test` 全緑を確認した。ブラウザ E2E（追随・固定・提案ボタン・ペア RecipeId 伝搬）は手動確認項目 MN-122〜126 としてユーザー確認へ委ねる
- **受け入れ条件**: 新規レシピで出力アイテムを選ぶと Id・名前が主産物へ追随し、手動編集で固定・ボタンで提案値へ戻せる。詳細は `phases/implementation-plan-phase29.md` と `phases/test-specification-phase29.md`。


### Phase 30: 輸送容量超過の警告とグラフ赤化の撤去（PR: 判定機構の撤去）

- [x] 「設備 1 ユニットへの入力流量 > 輸送容量」の警告と、同一判定によるフローグラフの容量超過赤化（`OverCapacity` エッジと両端ノード）を撤去する（仕様決定 BV、AN の撤去）。容量評価専用の機構（`AddTransportWarnings`・`MaxMachineInputs`・`OverspillGroups`・容量定数・警告コード・赤色分岐）も併せて撤去し、台数分表示（AO）のユニット割当と `TransportKind` は据え置く
- [x] `dotnet test` 全緑を確認し、ブラウザ E2E（`recipe-cupriumCanister` 等の容量超過構成で警告・赤化が出ないこと）を実施する
- **受け入れ条件**: ベルト 30 個/分・パイプ 60 個/分を超える入力を持つ計画で、警告欄にもグラフにも容量超過の表示が出ない。詳細は `phases/implementation-plan-phase30.md` と `phases/test-specification-phase30.md`。



### Phase 31: ID 系値の空白禁止と読み込み時正規化（PR: 検証規則・スキーマ・ローダー正規化・UI トリム）

- [x] エンティティの Id と参照 Id 値に「空白文字を含まない」の規則を追加し、`MasterValidator` の検証違反と `master.schema.json` の `pattern` に反映する（仕様決定 BW）
- [x] JSON 読み込みで ID 系値の前後空白を一貫して除去する正規化を読み込み経路に追加し、除去箇所を管理ツールの読み込み画面へ通知する（仕様決定 BX）
- [x] 管理ツールの共通属性編集で Id 欄を確定時トリムする（仕様決定 BY）
- [x] `dotnet test` 全緑を確認し、管理ツールで手動確認（Id 欄トリム・空白入り JSON の読み込み通知）を実施する
- **受け入れ条件**: 空白入り ID が検証・スキーマ・エクスポートで拒否され、前後空白入りの既存データは読み込みで矯正される。詳細は `phases/implementation-plan-phase31.md` と `phases/test-specification-phase31.md`。Phase 番号は計画時の仮採番から、Phase 29・30 の確定により 31 が正式な採番となった。



### Phase 32: 出力なしレシピ（処理レシピ）と余剰の廃棄処理（PR: 処理レシピの登録・計算・グラフ表現）

- [x] レシピの `Outputs` を 0 件許容とし、出力なしレシピ＝処理レシピを登録可能にする（仕様決定 BZ）。`master.schema.json` の `Outputs.minItems` を 0 へ緩和し、`MasterValidator` の「Outputs は 1 件以上」を撤去する。管理ツールは出力行を 0 行まで削除可能にし、BU の自動提案は出力なしレシピを対象外とする（仕様決定 CE）
- [x] 収束後の最終余剰に対し処理ランを投入する（仕様決定 CA・CB）。処理需要は反復ごとに現在余剰へ追随して再評価し、処理ランの設備台数・消費電力・固定消費・環境要件は通常ランと同じ規則で計上する（仕様決定 CC）
- [x] フローグラフで処理設備のノードを持たず、処理消費のあるアイテムノードを紫色で描きメタ行に処理設備名と台数・処理量を併記する（仕様決定 CD）。素材行で処理による消費を識別できる表示にする
- [x] `dotnet test` 全緑を確認した。ブラウザ E2E（処理レシピの登録・処理ランの反映・アイテムノードの紫化）は手動確認項目 MN-130〜134 としてユーザー確認へ委ねる
- **受け入れ条件**: 汚水など余剰のある廃水系アイテムに処理ランが追加され、設備台数・消費電力に反映される。グラフに処理設備ノードが出ず、アイテムノードが紫で処理情報を持つ。詳細は `phases/implementation-plan-phase32.md` と `phases/test-specification-phase32.md`。Phase 番号は検討時の仮採番（仮Phase32）から、Phase 31 のマージにより 32 が正式な採番となった。


### Phase 33: 警告・エラー表示の識別子を「名前（Id）」表記へ統一（PR: 文面統一と名前解決の共有化）

- [x] ページに表示する警告・エラー系（計算警告・計算例外・入力エラー・検証エラー文面・検証エラー一覧の EntityId 表示）のエンティティ参照を `名前（Id）` へ統一し、未解決参照は Id のみ表示とする（仕様決定 CF）。公開アプリと管理画面の双方が対象
- [x] `MasterDataSnapshot` に `GameEventsById` 索引を追加し、Domain に名前解決ヘルパーを設けて警告・検証・入力エラーの各経路で共有する
- [x] `dotnet test` 全緑を確認し、公開アプリと管理ツールで手動確認（警告欄の名前表記・フォールバック・検証一覧）を実施する
- **受け入れ条件**: 警告・エラー表示に生の Id が残らず `名前（Id）` で表示され、存在しない参照は Id のみになる。詳細は `phases/implementation-plan-phase33.md` と `phases/test-specification-phase33.md`。Phase 番号は Phase 32 のマージを受けて 33 が正式な採番となった。


### Phase 34: フローグラフ描画の堅牢化（PR: 実行時退避・時刻精度・ピクセル比上限）

- [x] 描画中の WebGPU デバイスロスト・未捕捉 GPU エラー・描画例外を検知し、一度だけ .NET へ通知してグラフ節を畳みリスト表示へ戻す（仕様決定 CJ）。失敗後のハンドル操作は no-op とする
- [x] `u.time` が 1024 秒を超えた時点で粒子位相へ整数分を畳み込み、時刻基準をリセットする
- [x] canvas バッファのピクセル比を `min(devicePixelRatio, 2)` に上限付けする（仕様決定 CK）
- [x] `dotnet test` 全緑を確認し、ブラウザで手動確認（失敗注入・時刻リセット・DPR エミュレーション）を実施する
- **受け入れ条件**: 描画失敗時にグラフが凍結せずリスト表示へ戻り、長時間表示でも粒子の位相精度が劣化しない。詳細は `phases/implementation-plan-phase34.md` と `phases/test-specification-phase34.md`。

### Phase 35: 既定レシピ選択の入力効率キー（PR: 実効入力レートキーの追加）

- [x] 需要アイテムの既定レシピ選択に実効入力レート昇順を第 3 キーとして挿入する（`VersionAdded` → 実効出力レート → 実効入力レート → `Id`、仕様決定 CL・CP）。入力合計は `Inputs` 全量で `FixedConsumption` を含めず、適格ペア 0 件は最下位とする
- [x] 処理レシピ選択（仕様決定 CB）にも同じキーを同型適用する。ペア候補列挙の順序も選択規則へ追随させる
- [x] `dotnet test` 全緑を確認し、ブラウザで手動確認（重息壌ガス既定の 02 化・候補列挙順・炭塊回帰）を実施する
- **受け入れ条件**: 同 `VersionAdded`・同実効出力レートのレシピ競合で入力の少ない側が既定になり、同梱マスタでは重息壌ガスの既定が `recipe-heavyXiragen02` になる。詳細は `phases/implementation-plan-phase35.md` と `phases/test-specification-phase35.md`。


### Phase 36: グラフ描画領域の自動縦幅拡張とグラフツールバー集約（PR: 領域高の自動適用・台数分切替の移設）

- [ ] 描画領域の高さを、グラフ世界矩形の高さがズーム 1.0 で収まる値へ自動で拡張する。下限は既定値（420px・モバイル幅 300px）、上限はビューポート高さとし、縦・横表示の双方へ適用する（仕様決定 CM）。領域高の自動適用ごとにフィットを掛け直し、等倍で全内容が見える表示へ戻す
- [ ] 手動リサイズのドラッグ確定で手動モードへ移行して自動拡張を停止し、リサイズハンドルのダブルクリックで手動指定を解除して自動へ復帰する（仕様決定 CM）。最大化中は自動拡張を休止し、復帰時に再評価する
- [ ] 「設備を台数分表示」切替を結果ツールバーからフローグラフのツールバーへ移設し、最大化オーバレイ内でも切替可能にする（仕様決定 CN）
- [ ] `dotnet test` 全緑を確認し、ブラウザで手動確認（自動拡張・上限・手動優先・自動復帰・最大化中の切替）を実施する
- **受け入れ条件**: 深い計画で領域が自動的に伸びて等倍で全ノードが読め、手動指定が優先され、ハンドルのダブルクリックで自動へ戻り、最大化中にも台数分表示を切替できる。詳細は `phases/implementation-plan-phase36.md` と `phases/test-specification-phase36.md`。Phase 番号は Phase 35（PR #93・未マージ）の採番を避けて 36 とする


### Phase 37: 縦表示後退エッジのループ張り出し量の決定論化（PR: 側面ループの発散解消）

- [ ] `edgeGeometry` の縦後退エッジについて、「間のノード矩形を全て避ける」張り出し量の反復探索を廃止し、縦スパン内ノード矩形の側面最外縁＋余白へ曲線が到達する決定論的な張り出し量へ置き換える（仕様決定 CO）
- [ ] 張り出し側は必要量の小さい側を選び、同量のときはソースノードの中心が行外縁に近い側とする（仕様決定 CO）
- [ ] 間のノードカードとの交差を「ノード背面への通過」として許容する旨を仕様へ反映する（仕様決定 CO・MN-98 の改定）
- [ ] `dotnet test` 全緑を確認し、ブラウザで手動確認（MN-158〜）を実施する
- **受け入れ条件**: `recipe-heavyXiragen01` の計画で後退エッジのループがノード群の外縁＋余白の範囲に収まり、画面外へ巨大にはみ出す弧が消える。詳細は `phases/implementation-plan-phase37.md` と `phases/test-specification-phase37.md`。Phase 番号は Phase 35（実装 PR #95）・Phase 36（PR #94・マージ済み）の採番を避けて 37 とする


## 5. 実装メモ・規約

- **NuGet**: 公開から 7 日以上経過した安定版のみ。`latest`/範囲指定禁止。新規ライブラリは導入前にライセンスを確認する。
- **テスト方針**: Domain の計算・検証を最も厚くする。テストケースは文書（requirements/decision-records）を根拠に作成し、ID を振って結果を表で報告する。各 Phase の詳細計画とテスト仕様は、Phase 開始時に `phases/implementation-plan-phase<N>.md`・`phases/test-specification-phase<N>.md` として切り出してよい（旧リポジトリと同じ慣行）。
- **Phase 別文書**: 一部の Phase は個別計画書・テスト仕様書を持たない。Phase 1 は `phases/` への文書集約以前に完了したため詳細計画書・テスト仕様書ともに未作成、Phase 5 はテスト仕様書を作成せずに実施した。
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
