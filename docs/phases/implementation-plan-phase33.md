# Phase 33 実装詳細計画

**対象フェーズ**: Phase 33（警告・エラー表示の識別子を「名前（Id）」表記へ統一）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)（仕様決定 CF・CG。関連: BE・BN）
**関連ドキュメント**: [test-specification-phase33.md](test-specification-phase33.md)（本 Phase のテスト仕様）、[implementation-plan-phase30.md](implementation-plan-phase30.md)（輸送容量警告を撤去する先行作業で、本 Phase の書き換え対象と重なる）

> 本書は Phase 33 の作業項目を、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 文書・実装・テストは 1 つの PR にまとめて main へマージする。
> Phase 番号は依頼どおり 33 とする。Phase 31（仕様決定 BW〜BY）と Phase 32（処理レシピ、仕様決定 BZ〜CE）はいずれも計画のみ main 入り済みで実装は未マージである。本書の仕様決定 CF・CG は CE の次の採番であり、テスト ID も Phase 32 の使用済み分（VAL-29・30・MN-130〜134）を避けて採番した。

## 1. スコープ

### やること

- ページに表示される警告・エラー系の文面で、エンティティ参照を `名前（Id）` 表記に統一する（仕様決定 CF）
  - 計算警告（`CalculationWarning.Message`、Domain の警告生成 4 ファイル全箇所）
  - 計算入力の例外メッセージ（`ProductionCalculator` の `ArgumentException` ×2、入力エラー欄へ捕捉される）
  - 入力エラー（`CalculationInputBuilder` のエラー文 ×4。現状は名前のみ、Id を付記する）
  - マスタ検証エラー（`MasterValidator` の文面のエンティティ参照部、および検証エラー一覧の `EntityId` 表示。公開アプリ・管理画面の両方）
- エンティティ参照がマスタ上で解決できない場合は Id のみを表示するフォールバックとする（`SnapshotLookup` の `Name : id` と同型）
- 参照解決のため `MasterDataSnapshot` に `GameEventsById` 索引を追加し、Domain に共有の名前解決ヘルパーを設ける（エラー警告系リファクタ）
- 変更した文面の回帰テストを追加し、既存テストの文面アサーションを新表記へ追従させる

### やらないこと

- `MasterValidationError`・`CalculationWarning` のレコード構造（フィールド構成・WarningCode）は変えない。変えるのは文面と一覧の表示文字列だけ
- IconKey・フィールドパス（`Inputs[0].ItemId` 等）・数値・SchemaVersion 等、エンティティ Id でない値は対象外
- ペア選択候補ラベル・供給内訳など警告・エラー系でない表示は対象外（既に名前表示。BN の説明併記はそのまま）
- `tools/validate_master.py`・CI ログ・例外のスタック情報など、ページ表示でない出力は対象外
- 警告文の用語自体は変えない（「採取素材」は仕様決定 BD どおりメッセージ側は据え置き）
- 警告の発火条件・コード・件数は変えない（WarningBag の重複抑止は (Code, Message) 組で判定しており、文面変更で同一警告が別文言になるだけで件数契約は不変）

## 2. 変更一覧

### Domain

| ファイル | 変更 |
|---|---|
| `Calculation/CalculationInputs.cs` | `MasterDataSnapshot` に `GameEventsById` 索引を追加する（GameEvents init で構築。既存索引と同型の ReadOnlyDictionary） |
| `Validation/EntityDisplay.cs`（新規） | 名前解決ヘルパー。`Format(name?, id)`（名前が空・未解決なら `id`、解決すれば `名前（Id）`）と、`MasterDataSnapshot`・エンティティ一覧から作る解決メソッド群（`Item/Facility/Environment/Recipe/GameMap/GameEvent`）を持つ。`MasterDocument` 由来の解決のため、コンストラクタは各種 `IReadOnlyDictionary` またはリストを受ける形にする |
| `Calculation/GatherCapResolver.cs` | 警告 4 箇所の `{…Id}` 埋め込みを解決済み `名前（Id）` へ。存在しないマップ指定はフォールバックで Id のみ |
| `Calculation/PairSelector.cs` | 警告 2 箇所（InvalidPairOverride のアイテム・レシピ・設備、InvalidVersionString のレシピ）を名前表記へ |
| `Calculation/ProductionPlanAggregator.cs` | 輸送容量警告のアイテム参照を名前表記へ（Phase 30 で撤去予定だが、マージ順にかかわらず同規則を当てる） |
| `Calculation/CalculationSession.cs` | 警告 8 箇所（GatherCapExceeded・CycleDetected のパス各要素・EnvironmentCoverageExceeded・EventItemUnavailable のイベント・PairConflict・NoRecipeAvailable ×2・InvalidEnvironmentOverride ×2）を名前表記へ。循環パスは各要素を名前解決してから ` → ` で連結する |
| `Calculation/ProductionCalculator.cs` | `ArgumentException` 2 箇所の `{target.ItemId}` を名前表記へ（存在しない目標はフォールバックで Id のみ） |
| `Validation/MasterValidator.cs` | `ValidateAll` 内部で名前解決器を構築し、文面にエンティティ参照を埋め込む箇所（参照存在・重複・採取素材でない等の Id 埋め込み）を `名前（Id）` へ。単一エンティティの公開 `ValidateX(entity, errors)` は文脈を持たないため、解決器を任意引数で受け取る形にし、省略時は従来どおり Id のみ出力（ValidateAll 経路では常に解決器が渡る） |

### Application / SharedUi

| ファイル | 変更 |
|---|---|
| `Application/CalculationInputBuilder.cs` | 入力エラー 4 箇所を `名前（Id）` へ（`{item.Name}` → `{item.Name}（{item.Id}）` 同型。EnvName・ItemName 行も同様） |
| `Application/SnapshotLookup.cs` | 文面ヘルパーは変えない（警告・エラー系でない表示の既存名前解決として据え置き） |

### App / Admin（表示側）

| ファイル | 変更 |
|---|---|
| `App/Pages/Home.razor` | 読み込み失敗リストの `@err.EntityId` を `名前（Id）` 解決へ。`MasterDataService.Document` が取得済みのときは名前解決、無ければ Id のみ（壊れたマスタでも表示が成立する） |
| `Admin/Components/ValidationErrorList.razor` | `EntityKind EntityId` 表示を `EntityKind 名前（Id）` へ。解決は呼出し側から文書（または解決器）を受け取るパラメータを追加して行う。遷移は従来どおり生の EntityId を使う |
| `Admin/Pages/Home.razor` | 読み込み違反リストの `EntityId` 表示を同規則へ。`ValidationErrorList` 呼出しにも文書を渡す |
| `Admin/Pages/PreviewPage.razor` | `ValidationErrorList` 呼出しに文書を渡す |

### テスト

| ファイル | 変更 |
|---|---|
| `tests/EndfieldAicWeb.Domain.Tests/WarningTests.cs` | WRN-09〜: `名前（Id）` 表記・フォールバック・循環パス名前化の回帰ケース |
| `tests/EndfieldAicWeb.Domain.Tests/ValidationTests.cs` | VAL-31〜: 検証エラー文面の `名前（Id）` 表記とフォールバック |
| `tests/EndfieldAicWeb.Application.Tests/CalculationInputBuilderTests.cs` | CIB-01〜（新接頭辞）: 入力エラー文面が `名前（Id）` を含むこと |
| `tests/EndfieldAicWeb.Domain.Tests/CalculationFixtures.cs` | 必要なら Item/Facility 等の `Name` を Id と別値にできる指定を追加（名前解決を検査できるようにする） |

### 文書

| ファイル | 変更 |
|---|---|
| `docs/decision-records.md` | CF・CG（本計画で追加済み） |
| `docs/requirements.md` | §4.2 に表示規則の箇条を追加（計画 PR で反映済み） |
| `docs/implementation-plan.md` | Phase 33 行を追加（計画 PR で反映済み）。実装 PR でチェックを `[x]` にする |

## 3. 変更詳細

### 3-1. 表記規則

エンティティ参照は `名前（Id）` とする（例: `鉄板（item-iron）`）。
参照先がマスタに存在しない、または Name が空の場合は Id のみを表示する（`SnapshotLookup` の `Name : id` と同型のフォールバック）。
文面中の種別語（アイテム・レシピ・環境・マップ・イベント）は残す。レシピ名は主産物名と同じになりうる（仕様決定 BU）ため、種別語が「アイテム X のレシピ X」の識別に効く。
循環依存のパスは各要素を名前解決してから ` → ` で連結する。

### 3-2. 名前解決の共有化（リファクタ）

警告生成は Domain 内で行われ、既存の `SnapshotLookup`（Application 層）は参照できない。
Domain に `EntityDisplay` ヘルパーを新設し、Domain（警告・例外・検証）・Application（入力エラー）・表示側（razor のエラー一覧）が同じ規則を使う。

- `Format(string? name, string id)`: name が null・空・空白のみなら `id`、それ以外は `名前（Id）`
- 解決器: `MasterDataSnapshot`（計算経路）と、各エンティティの `IReadOnlyList` または文書（検証・管理ツール経路）から構築する。`GameEventsById` はスナップショットに新設する索引で、イベント名はこれで解決する

### 3-3. 検証エラーの扱い

- `MasterValidator` の文面にあるエンティティ参照（参照先 Id・重複 Id・「採取素材ではありません: {id}」等）は `名前（Id）` へ置き換える。参照先が存在しない旨を報告する文面では、参照値は入力値のまま（= Id のみ）出る。これはフォールバック規則と同じ結果になる
- `ValidateAll` は全リストを受けるため解決器を構築できる。単一エンティティの公開 `ValidateX(entity, errors)` は文脈がないため解決器を任意引数で受け、省略時は Id のみ出力する（本番経路はすべて `ValidateAll` 経由のため、画面に出る文面は常に名前表記になる）
- 検証エラー一覧の `EntityKind EntityId` 部分は表示側で `EntityKind 名前（Id）` に解決する。読み込み失敗時（公開アプリ）・スキーマ不適合時は文書が無いことがあるため、解決できない EntityId はそのまま表示する
- `MasterJsonReader` の要素検証文面（`{location}.Name は必須です` 等）と Icon 節の文面（Key・File・Sha256）はエンティティ Id を埋め込まないため変更しない

### 3-4. 既存テストとの整合

`名前（Id）` は Id を部分文字列として含むため、`Message.Contains("i-none")` 等の既存アサーションは大半が成立し続ける。
成立しなくなるのは「Id そのまま」を仮定するアサーション（例: 完全一致・前方一致）のみで、実装時に走査して新表記へ追従させる。

## 4. 影響の確認

- ユーザーページの警告欄・入力エラー欄・計算例外欄の全エンティティ参照が `名前（Id）` になる
- 管理ツールの検証一覧・読み込み違反・プレビュー検証の `EntityId` 表示と文面が同規則になる
- `GameEventsById` は追加索引のみで既存契約を変えない（参照追加）
- WarningBag の重複抑止は (Code, Message) で判定しており、文言変更だけでは同一警告の件数契約は変わらない

## 5. 受け入れ条件

- 同梱マスタで警告が発火する構成（例: ペア上書き不正・採取上限超過・イベント非有効）を計算し、警告欄に生の Id が出ず `名前（Id）` で表示される
- 存在しないマップ・環境を指す上書きでは Id がそのまま表示される
- 管理ツールで検証エラー（参照不存在等）を発生させ、一覧と文面の両方が `名前（Id）` 規則に従う（存在しない参照は Id のみ）
- `dotnet test` 全緑

## 6. 残課題

- 重複名の識別は `名前（Id）` の Id 部に委ねる（仕様決定 CG）。識別が不十分と判明した場合の併記改善は別途仕様決定とする
- 読み込み失敗時に部分文書があっても Document が公開されない経路では名前解決が効かず Id のみ表示になる。部分文書の解決を求める場合は別途検討する
