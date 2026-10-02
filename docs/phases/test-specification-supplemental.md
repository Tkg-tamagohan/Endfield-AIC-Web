# テスト仕様 追補書

**対象**: Phase 番号を持たない変更（機能改善・リファクタ・レビュー対応）で追加された自動テスト項目
**前提ドキュメント**: [requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)

> 本書は各 Phase テスト仕様書の追補として、Phase 外で採番されたテスト ID を登録する。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。

## 1. テスト ID の運用ルール

- Phase 外の作業で追加するテスト ID は本書へ登録する。
- コード側マーカーの新規追加は `[Fact(DisplayName = "ID: …")]` 形式を推奨する（`dotnet test` の出力に ID が出る）。既存の `PREFIXNN_` メソッド名や `// ID:` コメント形式は許容する。
- 既存 ID の派生ケースは `-NNb` 形式（OPT-03b など）で採番してよい。
- 新規採番は既存の Phase 文書および本書の ID と重複しない接頭辞・番号を使う。EVT・EXP のようにすでに重複している ID は注記で区別するに留め、改番は行わない。
- MN 番号は Phase 内スコープである。phase4・6・7・8 はそれぞれ MN-01 から採番しており、同一番号が Phase ごとに別内容を指す（Phase 12 以降は連番運用）。

## 2. 登録項目

Phase 仕様書の既存 prefix を継ぐ続番を §2-1、Phase 外で新設された prefix を §2-2、既存 ID の派生付番を §2-3 に登録する。

### 2-1. 既存 prefix の続番

| ID | 内容 | 実装場所 | 導入経緯 |
|---|---|---|---|
| SNP-03 | 構築後に元のリストを変更しても、スナップショットのコレクションと索引は構築時点の内容を保持する | tests/EndfieldAicWeb.Domain.Tests/SnapshotImmutabilityTests.cs | PR #12（索引の構築時確定）。phase4 §SNP の続番 |
| XPT-12 | dataVersion 引数は文書の値の代わりに出力され、文書を変更しない | tests/EndfieldAicWeb.Infrastructure.Tests/MasterExporterTests.cs | PR #13（Admin 周辺の純粋ロジック整理）。phase3 §XPT の続番 |
| XPT-13 | icons 引数は文書の Icons の代わりに検証・出力される | 同上 | 同上 |
| XPT-14 | dataVersion 引数が空白なら例外で拒否される | 同上 | 同上 |
| ICO-13 | CountMatching は実体が一致するエントリだけを数える | tests/EndfieldAicWeb.Infrastructure.Tests/IconManifestTests.cs | PR #13。phase3 §ICO の続番 |
| EXP-11 | 提案キーが使用中なら連番を付けて一意にする | tests/EndfieldAicWeb.Infrastructure.Tests/IconPipelineTests.cs | PR #13。phase7 §EXP（アイコンエクスポート計画）の続番 |
| EXP-12 | 連番付きでも 64 文字以内の有効キーになる | 同上 | 同上 |
| EXP-13 | 対象エンティティ自身のキーは衝突判定から外れる | 同上 | 同上 |
| ZIP-06 | 空セグメントを含むアイコン名は正規パスに畳んで返す | tests/EndfieldAicWeb.Infrastructure.Tests/IconPipelineTests.cs | PR #10 の Devin Review 対応。phase7 §ZIP の続番 |
| ADM-09 | 種別をまたいで同じ提案キーになる場合は一意のキーを割り当てる | tests/EndfieldAicWeb.Admin.Tests/AdminIconTests.cs | PR #10 の Devin Review 対応。phase7 §ADM の続番 |
| ADM-10 | マニフェストと一致しない zip 内画像は未取得扱いでエクスポートを拒否する | 同上 | 同上 |
| ADM-11 | JSON 読み込みでは温存ストアを新マニフェストで再照合する | 同上 | 同上 |
| ADM-12 | null バージョンのエクスポートは必須違反で拒否され、文書を汚さない | 同上 | PR #13。同上 |

### 2-2. Phase 外で新設された prefix

| ID | 内容 | 実装場所 | 導入経緯 |
|---|---|---|---|
| SCAF-01 | Domain アセンブリを読み込める | tests/EndfieldAicWeb.Domain.Tests/ScaffoldTests.cs | Phase 1 のソリューション雛形（Phase 1 のテスト仕様書は未作成） |
| SCAF-02 | master.json がスキーマ v1 のルートキーを持つ | tests/EndfieldAicWeb.Infrastructure.Tests/MasterJsonScaffoldTests.cs | 同上 |
| TIN-01 | 有効行は ProductionTarget になり、全空行は無視される | tests/EndfieldAicWeb.Application.Tests/CalculationInputBuilderTests.cs | PR #11（計算入力の共有純粋関数化）。phase4 MN-09 の自動化相当 |
| TIN-02 | アイテム未選択で数量入りの行はエラー（アイテムを選んでください） | 同上 | 同上 |
| TIN-03 | スナップショットに存在しないアイテム Id はエラー | 同上 | 同上 |
| TIN-04 | 数量が非数値・0・負のいずれかならエラー（メッセージにアイテム名を含む） | 同上 | 同上 |
| TIN-05 | 有効行が 0 件（全行空）ならエラー | 同上 | 同上 |
| PVD-01 | 全設備が整数台数なら未調整表示が既定（両ビューの見え方が同じため） | tests/EndfieldAicWeb.Application.Tests/PlanViewDefaultsTests.cs | PR #11。仕様決定 O・I の既定ビュー規則 |
| PVD-02 | 切上げ過剰が出る計画では調整済が既定（仕様決定 O） | 同上 | 同上 |
| PVD-03 | 散布機台数の上限はその環境を使う稼働中レシピ数（仕様決定 I） | 同上 | 同上 |
| PVD-04 | 同一環境を使うレシピが複数あればその数が上限になる | 同上 | 同上 |
| PVD-05 | 計画に登場しない環境の上限は 0 | 同上 | 同上 |
| ERC-01 | 0〜上限の整数文字列はそのまま保持する（境界の 0 と上限ちょうどを含む） | tests/EndfieldAicWeb.Application.Tests/CalculationInputBuilderTests.cs（EnvCountReconcileTests） | PR #36（仕様決定 AH の散布機台数整合） |
| ERC-02 | 新しい上限を超えた保持値は空欄へ戻す（クランプしない） | 同上 | 同上 |
| ERC-03 | 非整数・負数・空欄・空白のみ・null は空欄（自動値）のまま、または空欄へ戻す | 同上 | 同上 |

### 2-3. 既存 ID の派生付番

| ID | 内容 | 実装場所 | 導入経緯 |
|---|---|---|---|
| OPT-03b | イベントを有効にすると限定レシピ・環境ペアが候補に復帰する | tests/EndfieldAicWeb.Application.Tests/PairOptionTests.cs | phase4 §OPT-03 の派生（イベント有効側の確認） |
| VWU-03b | 散布機とレシピが同じ設備を共用する場合でも、散布機台数は切上げ分から控除される | tests/EndfieldAicWeb.Application.Tests/UnadjustedViewTests.cs | phase4 §VWU-03 の派生 |
