# テスト仕様 追補書

**対象**: Phase 番号を持たない変更（機能改善・リファクタ・レビュー対応）で追加された自動テスト項目
**前提ドキュメント**: [requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)

> 本書は各 Phase テスト仕様書の追補として、Phase 外で採番されたテスト ID を登録する。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。

## 1. テスト ID の運用ルール

- Phase 外の作業（Devin Review 対応を含む）で追加するテスト ID は本書へ登録する。
- コード側マーカーの新規追加は `[Fact(DisplayName = "ID: …")]` 形式を推奨する（`dotnet test` の出力に ID が出る）。既存の `PREFIXNN_` メソッド名や `// ID:` コメント形式は許容する。
- 既存 ID の派生ケースは `-NNb` 形式（OPT-03b など）で採番してよい。
- 新規採番は既存の Phase 文書および本書の ID と重複しない接頭辞・番号を使う。EVT・EXP のようにすでに重複している ID は注記で区別するに留め、改番は行わない。
- MN 番号は Phase 内スコープである。phase4・6・7・8 はそれぞれ MN-01 から採番しており、同一番号が Phase ごとに別内容を指す（Phase 12 以降は連番運用）。
- 採番前の現行最大・次候補の確認は `tools/scan_ids.py` で機械化できる（`--with-prs` でオープン PR の使用分も列挙）。
- 採番メモの記録値は採番時点のスナップショットとして残し、後から書き換えない。`scan_ids.py --check` は新しい計画書・仕様書の作成時点での確認に使う。
- グループコメント（範囲一覧・クラス要約）のみが根拠の ID は個別テストの削除を検出できない。`tools/check_test_ids.py` はこの検出限界を I2 として情報化する。

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
| DSP-23 | 非有効イベント所属の余剰は処理対象にならない | tests/EndfieldAicWeb.Domain.Tests/DisposalTests.cs | PR #88 の Devin Review 対応。phase32 §DSP の続番 |
| DSP-24 | 計算目標のアイテムは処理対象にならず補助入力として需要計上 | 同上 | 同上 |
| DSP-25 | 非有効イベント所属の処理入力は未充足と警告 | 同上 | PR #88 の Devin Review 対応（2 ラウンド目）。同上 |
| FG-55 | 未調整ビューで表示量が 0 の処理はアイテムを紫化しない | tests/EndfieldAicWeb.Application.Tests/FlowGraphModelBuilderTests.cs | PR #88 の Devin Review 対応。phase32 §FG の続番 |
| RCP-13 | 提案未計算の仮採番は最初の提案で置き換わる | tests/EndfieldAicWeb.Application.Tests/RecipeAutoFillTests.cs | PR #86 の Devin Review 対応。phase29 §RCP の続番 |
| VAL-34 | null 許容参照値の空文字列は null に正規化される | tests/EndfieldAicWeb.Infrastructure.Tests/MasterJsonLoaderTests.cs | PR #85 の Devin Review 対応。VAL の続番 |
| ADM-09 | 読み込み時正規化があった文書は IsDirty になる（CH） | tests/EndfieldAicWeb.Admin.Tests/AdminDocumentServiceTests.cs | PR #90（仕様決定 CH・CI）。アイコン系 ADM-09 とは同番号の別対象 |
| ADM-10 | 読み込み時正規化がない文書は IsDirty にならない（CH） | 同上 | 同上 |
| ADM-11 | 同一文書のエクスポート完了でダーティが落ちる（CH の解除経路） | 同上 | 同上 |
| ADM-12 | エクスポート開始後に別文書を読み込んでも完了記録は新文書のダーティを上書きしない | 同上 | PR #90 のレビュー対応。同上 |
| ADM-13 | エクスポート開始後に失敗した読み込みを挟んでも完了記録はダーティを落とす | 同上 | PR #90 のレビュー対応。同上 |
| DSP-27 | 処理ランと再利用消費は同じアイテムノードに同居する | tests/EndfieldAicWeb.Application.Tests/FlowGraphModelBuilderTests.Disposal.cs | phase32 帰属・W3 解消 |
| DSP-28 | 兼用設備の台数分表示は占有スロットのユニットのみを出し、処理ランはユニット割当対象外 | 同上 | phase32 帰属・W3 解消 |
| ADM-19 | レシピと主出力がともに IconKey 空文字なら実効キーは null を返す（プレースホルダ表示。仕様決定 CX） | tests/EndfieldAicWeb.Admin.Tests/AdminIconTests.cs | PR #120 の Devin Review 対応 |

ADM-09〜12 はアイコン系（AdminIconTests.cs、本表の既存行）と文書ダーティ管理系（AdminDocumentServiceTests.cs、仕様決定 CH・CI）で同番号の別対象である。ADM-14〜17（[test-specification-phase38.md](test-specification-phase38.md) §ADM）はダーティ管理系の続番である。ADM-18・ADM-19 はアイコン系だが全体採番の続番として採番し、[test-specification-phase41.md](test-specification-phase41.md) §自動テストにも登録済みである。

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
| PVD-03 | 散布機台数の入力範囲は（必要台数, 利用機械数の切上げ）。仕様決定 BS で範囲化に改訂 | 同上 | 同上 |
| PVD-04 | 同一環境を使うレシピが複数あれば機械数の合算が上限になる | 同上 | 同上 |
| PVD-05 | 計画に登場しない環境の範囲は (0, 0) | 同上 | 同上 |
| ERC-01 | 下限〜上限の整数文字列はそのまま保持する（下限ちょうど・上限ちょうどを含む）。仕様決定 BS で下限を必要台数へ改訂 | tests/EndfieldAicWeb.Application.Tests/CalculationInputBuilderTests.cs（EnvCountReconcileTests） | PR #36（仕様決定 AH の散布機台数整合） |
| ERC-02 | 新しい上限を超えた保持値は空欄へ戻す（クランプしない） | 同上 | 同上 |
| ERC-03 | 非整数・負数・空欄・空白のみ・null は空欄（自動値）のまま、または空欄へ戻す | 同上 | 同上 |
| ERC-04 | 下限未満の保持値（0 台を含む）は空欄へ戻す（仕様決定 BS。AH の範囲読み替え） | 同上 | Phase 27（仕様決定 BS） |
| CPH-01 | 日・時・分の各欄を合算して PeriodAmount にする（1 日 2 時 30 分 = 1590 分） | tests/EndfieldAicWeb.Application.Tests/CalculatorPanelHelperTests.cs（PeriodParseTests） | PR #65（CalculatorPanel の純粋ヘルパーを Application 層へ抽出）。登録時点で PR #65 は未マージのため、コード側実体はマージ後に main へ反映される |
| CPH-02 | 空欄・空白のみ・null の欄は 0 扱い | 同上 | 同上 |
| CPH-03 | 非数値・負・非有限の欄があれば拒否し、既定値（全 0）を返す | 同上 | 同上 |
| CPH-04 | 計画の採取対象順で行を組み、保持している入力値を適用する。保持値のない対象は空欄 | 同上（GatherRateReconcileTests） | 同上 |
| CPH-05 | 採取対象外になった保持値のうち、採取素材でない・未知のアイテム分は破棄する | 同上 | 同上 |
| CPH-06 | 採取対象外で受理されない値は破棄し、採取対象内の値はそのまま残す | 同上 | 同上 |
| CPH-07 | 空欄はマップ既定値、0 以上の有限値は上書き（マップ値超過も許容） | 同上（GatherCapTests） | 同上 |
| CPH-08 | 受理されない入力（非数値・負・非有限）はマップ既定値へ戻す | 同上 | 同上 |
| CPH-09 | マップの無限行とマップ未選択は上限なし（null） | 同上 | 同上 |
| CPH-10 | 採取充当と要求レートは計画から読み出す。登場しないアイテムは 0 | 同上 | 同上 |
| CPH-11 | 採取充当が有効上限へ達し需要が残るとき「上限到達」 | 同上 | 同上 |
| CPH-12 | 上限未満の充当・上限なし（無限行）は到達しない | 同上 | 同上 |
| CPH-13 | 毎分はそのまま 2 桁、毎秒は ÷60 の 3 桁、期間は ×合計分数の 2 桁で整形する | 同上（ResultFormatTests） | 同上 |
| CPH-14 | 個/分固定の整形（採取レートなど単位切替を適用しない表示） | 同上 | 同上 |
| CPH-15 | 個/期間の併記は期間表示かつ有効な期間入力があるときだけ付く | 同上 | 同上 |
| CPH-16 | 供給内訳は種別ごとの文になる。レシピ名はスナップショットから引く | 同上（ResultTextTests） | 同上 |
| CPH-17 | 供給内訳の流量は表示単位と接尾辞に従う | 同上 | 同上 |
| CPH-18 | ペア候補ラベルは「レシピ ／ 設備 秒・環境・固定消費・（既定）」を並べる | 同上 | 同上 |
| CPH-19 | 期間イベントは開催期間を併記し、常設は空。未登録名は Id に倒れる | 同上 | 同上 |
| CPH-20 | 採取レート欄のプレースホルダと既定値ラベル。上限なしは「無制限」 | 同上 | 同上 |
| CPH-21 | スナップショット未構築でもラベルは「無制限」系へ倒れる | 同上 | 同上 |
| CPH-22 | 登録済み Id は表示名、未登録とスナップショットなしは Id フォールバック | 同上（SnapshotLookupTests） | 同上 |
| CPH-23 | アイコンキーは設定値を返し、未設定・未登録・スナップショットなしは null | 同上 | 同上 |
| CPH-24 | イベント所属アイテムの判定。未登録・スナップショットなしは false | 同上 | 同上 |
| CPH-25 | 生産リスト行の新規レート既定値は 30（仕様決定 AZ）。他の入力行は空欄開始 | 同上（InputRowStateTests） | 同上 |

### 2-3. 既存 ID の派生付番

| ID | 内容 | 実装場所 | 導入経緯 |
|---|---|---|---|
| OPT-03b | イベントを有効にすると限定レシピ・環境ペアが候補に復帰する | tests/EndfieldAicWeb.Application.Tests/PairOptionTests.cs | phase4 §OPT-03 の派生（イベント有効側の確認） |
| VWU-03b | 散布機とレシピが同じ設備を共用する場合でも、散布機台数は切上げ分から控除される | tests/EndfieldAicWeb.Application.Tests/UnadjustedViewTests.cs | phase4 §VWU-03 の派生 |
