# Phase 3 実装詳細計画

**対象フェーズ**: Phase 3（Infrastructure: マスタ JSON I/O＋アイコンマニフェスト＋検証テスト）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)
**関連ドキュメント**: [test-specification-phase3.md](test-specification-phase3.md)（本 Phase のテスト仕様）

> 本書は Phase 3 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### 作るもの

- `EndfieldAicWeb.Infrastructure` の 2 サブフォルダ
  - `Transfer/`：マスタ JSON の読み込み（構文・構造・意味の三段検証）とエクスポート
    - `MasterJsonDto.cs`：JSON トップレベル構造とエンティティの DTO。デシリアライズ用の nullable フィールドとし、欠落フィールドを構造検証で検出できるようにする
    - `MasterJsonReader.cs`：構文解析・構造検証・実体化・`MasterValidator` 呼び出しの共有経路（internal）
    - `MasterJsonLoader.cs`：公開 API。`MasterJsonLoadResult { Document, Errors }` を返し、違反は例外ではなくエラー一覧で返す
    - `MasterExporter.cs`：`MasterDocument` を全置換形式の JSON 文字列へ書き出す。書き出し前に同一規則で検証し、違反時は `MasterValidationException` で拒否する
  - `Icons/`：アイコンマニフェストの照合検証とキー解決
    - `IIconFileProvider.cs`：ファイル内容の取得を抽象化するインターフェース（WASM では HTTP 取得、テスト・ツールではファイルシステムに差し替え可能にする）
    - `FileSystemIconProvider.cs`：`IIconFileProvider` のファイルシステム実装
    - `IconFiles.cs`：Sha256 計算とマニフェスト記述との照合（internal）
    - `IconManifestVerifier.cs`：マニフェスト各エントリと実ファイルの Bytes/Sha256 照合検証。違反はエラー一覧で返す
    - `IconResolver.cs`：`IconKey → ファイルパス` の解決。マニフェスト収録キーは照合一致のみ採用し、未設定・欠落・不一致はフォールバック（null）とする
- `EndfieldAicWeb.Infrastructure.Tests` の検証テスト（[test-specification-phase3.md](test-specification-phase3.md) の項目 ID どおり）

### 作らないもの

- 管理ツール UI・計算アプリ UI からの呼び出し（Phase 4・6）
- アイコン画像の取り込み・正規化・エクスポート物への `icons/` 出力（Phase 7 のアイコンパイプライン）
- `MasterDocument` → `MasterDataSnapshot` 変換など計算実行の入口（Application 層、Phase 4）
- JSON Schema（`data/master.schema.json`）自体による機械検証の導入。スキーマはエディタ支援・外部検証用であり、読み込み側は同一規則を C# で実装する

## 2. 読み込みの三段構造

旧 `MasterJsonReader`/`MasterJsonLoader` の「構文 → 構造 → 意味」の流れを継承するが、旧版と異なり DB 置換・例外型は持たない。

1. **構文解析**：`System.Text.Json` で DTO へデシリアライズする。構文エラー・型不一致はエラー一覧に集約する（例外で呼び出し側を止めない）。スキーマ `required` に対応する全プロパティを DTO の `required` メンバーとし、キー欠落（null 可キーの欠落を含む）はここで拒否する。
2. **構造検証**：`SchemaVersion == 1`・`DataVersion` 非空・必須配列の存在・null 要素・必須フィールド・enum 値・Icons 節の構造を検査する。スキーマ未定義プロパティは DTO の `[JsonExtensionData]` で捕捉しここで拒否する（スキーマ `additionalProperties:false`/`unevaluatedProperties:false` 準拠）。ここを通過したドキュメントのみ安全に実体化できる。エラーは `MasterValidationError` に統一して返す。
3. **意味検証**：実体化したエンティティへ `MasterValidator.ValidateAll` を適用する（値域・参照整合性・ペア一意性・仮想アイテム規則など、Phase 2 と同一規則）。

`MasterJsonLoadResult` の規約は次のとおり。

- `Errors` は三段すべての違反を集約した読み取り専用一覧。
- `Document` は構造検証を通過して実体化できた場合にのみ非 null とする。意味検証の違反が残る場合も返すため、利用側は `Success`（`Errors` が空）を確認してから使う。

`RecipeFacility.RecipeId` は JSON 上に持たず、実体化時に所属レシピの `Id` を与える。

### 構造検証で扱う規則

| 対象 | 規則 |
|---|---|
| ルート | `SchemaVersion` が 1 であること（未知版は拒否）、`DataVersion` が非空文字列であること |
| 各配列 | `Items`・`Facilities`・`Environments`・`GameEvents`・`Recipes`・`Icons` が存在すること（スキーマ `required` 準拠） |
| 配列要素 | null 要素を許さない |
| 共通属性 | `Id`・`Name` は非空必須。`Description` は必須（空文字可、null は拒否。スキーマ type=string 準拠） |
| Item | `Category`・`TransportKind`（enum として解析可能）・`IsGatherable` 必須 |
| Facility | `Width`・`Height`・`PowerConsumption` 必須（実体化で `null` を剥がすため） |
| Environment | `ProviderFacilityId`・`ConsumeItemId`・`ConsumeRatePerMinute` 必須 |
| Recipe | `Inputs`・`Outputs`・`Facilities` 必須（配列の存在。空や値域は意味検証へ） |
| RecipeInput/Output | `ItemId`・`Quantity` 必須。`SortOrder` 必須かつ 0 以上 |
| RecipeFacility | `FacilityId`・`CycleTime` 必須。`EnvironmentId`・`FixedConsumption` は null 許容 |
| FixedConsumption | `ItemId`・`RatePerMinute` 必須 |
| Icons 節 | `Key` 必須・一意・文字種制約（`IconKeyRules`）、`File` は `icons/<Key>.png` 固定形式、`Sha256` は 64 桁小文字 hex、`Bytes` は 1 以上（スキーマ準拠） |

上記以外の値域（数値の正負・バージョン順序・イベント期間）と参照整合性・ペア一意性・ID 一意性・`IconKey` 文字種は `MasterValidator` が担い、二重に報告しない。

## 3. エクスポートの方針

- 入力は `MasterDocument`。`SchemaVersion` が 1 であることと `DataVersion` 非空を確認したうえで、`MasterValidator.ValidateAll` と Icons 節の構造規則を同一適用し、違反時は `MasterValidationException` で拒否する。null コレクション・null 要素・`Description` null・レシピ内配列の null も構造違反として同様に拒否する（`MasterValidator` が参照不可能なためエクスポート側で先に検出する）。
- 出力はインデント付き・`UnsafeRelaxedJsonEscaping`（日本語をエスケープしない）の全置換 JSON。全フィールドを null 含めて出力する（スキーマ `required` 準拠）。
- `Icons` 節は `MasterDocument.Icons` をそのまま書き出す。実ファイルからの Sha256/Bytes 再計算はアイコンパイプライン（Phase 7）の責務とする。
- レシピのペアは `RecipeId` を出力しない（JSON 構造上は持たない）。
- 往復（読み込み → エクスポート）で内容が保存されることをテストで担保する。

## 4. アイコンマニフェストの方針

旧 `IconCatalog` の解決規則を新モデル（`IconEntry`＝`Key/File/Sha256/Bytes`）へ適合する。Blazor WASM ではファイル I/O が HTTP 取得に変わるため、ファイル内容の取得を `IIconFileProvider` に抽象化する。

- `IconManifestVerifier.Verify(manifest, provider)`：各エントリの `File` 実体が存在し、`Bytes`・`Sha256` が一致するかを検査し、違反をエラー一覧で返す（「マニフェスト不一致は明示的に拒否する」の担い手）。
- `IconResolver.Resolve(iconKey)` の返り値は次のとおり。
  - `iconKey` が null・空・予約キー・文字種違反 → null（フォールバック表示）。
  - マニフェスト収録キー：`File` の実体が `Bytes`・`Sha256` に一致する場合のみ `File` を返す。欠落・不一致は null。
  - 収録外キー：`icons/<Key>.png` が存在すればそのパスを返す（仕様決定 CY の収録外キー解決を継承）。存在しなければ null。

## 5. 作業順序

1. `Transfer/` の DTO・Reader・Loader・Exporter を実装する。
2. `Icons/` のファイルプロバイダ・照合・解決を実装する。
3. [test-specification-phase3.md](test-specification-phase3.md) の項目どおりに検証テストを作成する。
4. `dotnet test` を全緑にし、`implementation-plan.md` の Phase 3 チェックリストを更新する。
