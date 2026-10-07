# Phase 7 実装詳細計画

**対象フェーズ**: Phase 7（アイコン画像＋実データ投入: アイコンパイプライン＋初回データ）
**前提ドキュメント**: [implementation-plan.md](../implementation-plan.md)、[requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)
**関連ドキュメント**: [test-specification-phase7.md](test-specification-phase7.md)（本 Phase のテスト仕様）

> 本書は Phase 7 のチェックリストを、作業者が追加の判断なしに実行できる粒度へ分解したものである。
> 成果物は原則として 1 つの PR にまとめて main へマージする。

## 1. スコープ

### 作るもの

- `EndfieldAicWeb.Admin` のアイコン管理
  - 各エンティティ編集フォームのアイコン欄（共通属性の `IconKey` 行を拡張）: 画像取り込み、プレビュー、クリア、キー入力
  - 画像取り込み時の 128×128 PNG 正規化（ブラウザ Canvas、追加の NuGet 依存なし。旧版の中央正方形切り出し＋縮小を踏襲、仕様決定 AA）
  - アイコンのブラウザ内保持（メモリ上のストア）と、URL・同梱・ファイル各読み込み経路からの取得
  - エクスポートを `master-export.zip`（`data/master.json`＋`data/icons/*.png`）へ変更し、マニフェストを参照キーのみ・ハッシュ再計算で出力する
- `EndfieldAicWeb.App` の `IconKey` 表示
  - マニフェストに基づくアイコン取得・照合・フォールバック解決と、`data:` URI での表示
  - 一覧・選択 UI（素材・設備・環境・余剰の行、アイテム選択、レシピ名表示）へのアイコン適用（仕様決定 AL の「まず一覧・選択 UI」相当）
  - レシピは `IconKey` 未設定時に主出力アイテム（`SortOrder` 最小）のアイコンへフォールバック（仕様決定 CX）
- `tools/validate_master.py` へのマニフェスト↔ファイル照合の追加（`data/icons/` の実在・Bytes・Sha256 の一致を CI で検査）
- 旧リポジトリの権利クリア済みアイコン（自作画像）のうち、現行サンプルデータのエンティティに対応するものの移植
- 本書と [test-specification-phase7.md](test-specification-phase7.md)

### 作らないもの

- 実ゲームデータ自体の投入。ゲーム内数値の正確な転記は管理者（ユーザー）が管理ツールで行う運用作業であり、本 PR はその手順を成立させるパイプライン側を完成させる。サンプルデータへの移植アイコン付与で、エクスポート物のコミットから Pages 配信までの経路を実際に通して検証する
- ゲーム画像素材の同梱（仕様決定 R の権利クリア方針。権利クリアな自作画像のみ）
- アイコンの表示形状の決定（角丸・円形クリップ等。仕様決定 AL の後続決定扱い）
- マニフェストに載らないキーへのローカル差し込みのための恒久配線。ファイル名解決のフォールバック自体は IconResolver が持つため、バンドルに存在するファイルは拾う。ブラウザからの任意差し込み（ローカルファイルの永続キャッシュ）は対象外
- 管理ツールの zip 以外でのフォルダ出力。ブラウザからローカルフォルダへ直接書き出せないため、エクスポートは zip に一本化する
- bUnit・E2E 自動化（Phase 6 と同じく、ブラウザ動作は手動確認としロジックは単体テストで担保）

## 2. 仕様の確定事項

上位文書と仕様決定（R・AA・N・AL・CX・CY のアイコン系規則）から確定済みの内容は次のとおり。

- 全マスタエンティティが `IconKey`（null 可）を持つ（仕様決定 N）。キー文字種は `^[A-Za-z0-9_-]{1,64}$` で、読み込み・保存・解決の各層で同一規則を適用する（仕様決定 AA・R、実装済みの `IconKeyRules`）。
- 画像実体は `data/icons/<IconKey>.png` としてリポジトリ管理し、マスタ JSON の `Icons` 節が `Key`・`File`（`icons/<Key>.png` 固定）・`Sha256`・`Bytes` を持つ（仕様決定 R・CY。`IconResolver`・`IconManifestVerifier`・`ValidateIconManifestValues` は Phase 3 で実装済み）。
- 管理ツールの取り込みは PNG・正方形へ正規化して 128×128 とする（仕様決定 AA）。`IconKey` が空なら `icon-<Id>` を補完する。
- エクスポート時は `IconKey` → マニフェスト → ファイル実在の整合を検証し、不整合を拒否する（仕様決定 AA・R）。マニフェストの `Sha256`/`Bytes` は実ファイルから再計算して出力し、どのエンティティからも参照されない孤立エントリは出力しない。
- マニフェスト収録キーの表示はハッシュ一致ファイルのみ採用し、収録外キーはファイル名一致で解決する（`IconResolver` の既存挙動）。未設定・`icon-placeholder`・未解決はすべてプレースホルダ表示へ落とす。
- レシピのアイコン解決順は `Recipe.IconKey` > 主出力アイテムの `IconKey` > プレースホルダ（仕様決定 N・CX）。
- `Icons` の変更も `DataVersion` 更新に乗る（仕様決定 R）。

### 本 Phase の実装上の決定

- 画像正規化はブラウザの Canvas（`createImageBitmap` → 中央正方形切り出し → 128×128 描画 → PNG 化）で行う。追加の NuGet 依存を入れない方針は旧版と同じ（仕様決定 AA）。
- エクスポート物は `master-export.zip` 1 ファイルにまとめる。内部構造は `data/master.json` と `data/icons/<Key>.png` とし、リポジトリルートで展開すればそのまま `data/` へ反映できる形にする。
- 読み込み経路ごとのアイコン取得: URL・同梱読み込みでは JSON のあるディレクトリ（`data/`）からマニフェスト記載ファイルを取得する。ローカル `.json` 選択では JSON のみを置き換え、アイコンは保持中のストアを使い続ける（旧版の「JSON を開く」の挙動と同じ）。エクスポート物の往復を成立させるため、ファイル選択は `.zip`（本 Phase の出力形式）も受け付け、内部の `data/master.json` と `data/icons/` を読む。
- マニフェスト記載ファイルのうち Sha256/Bytes が一致しないものは取得できなかったものとして扱い、プレビューはプレースホルダ、エクスポートは「アイコン未取得」違反で拒否する（旧版で画像欠落時にエクスポートが拒否された挙動に相当）。
- `ValidateIconManifestValues` は構造規則を見る既存のままとし、参照→マニフェスト→ファイル実在の整合はエクスポート経路でのみ検査する（読み込み時にファイル実在を要件にしない点も旧版と同じ）。

## 3. 設計詳細

### 3.1 Infrastructure の追加要素

| 型 | 責務 |
|---|---|
| `InMemoryIconFileProvider` | `IIconFileProvider` のメモリ実装。マニフェスト相対パスをキーに PNG バイト列を保持する。読み書き両用とし、App・Admin の両方で使う |
| `IconExportPlanner` | エクスポート用マニフェストの組立。全エンティティの非 null・非プレースホルダ `IconKey` を走査し、参照キーごとにファイルから `Sha256`/`Bytes` を再計算した `IconEntry` を返す。未登録キー・ファイル欠落はエラーとして集約し、孤立エントリは出力に含めない。取り込み用にキー＋バイト列から `IconEntry` を作る共通処理もここに置く |
| `IconArchive` | `master-export.zip` の生成（`data/master.json`＋`data/icons/`）と読み取り（zip → JSON＋ファイル群）。`System.IO.Compression` のみ使用し追加依存なし |
| `IconFiles` | 既存の内部静的クラスを public 化し、Admin 側の取得済みファイル照合に再利用する |

`IconResolver`・`IconManifestVerifier`・`IconKeyRules`・`FileSystemIconProvider` は既存のまま使う。

### 3.2 Admin の構成

- `AdminDocumentService` にアイコンストア（`InMemoryIconFileProvider`）を持たせる。`Document.Icons` はマニフェストの編集単位、ストアは画像バイト列の実体であり、両者はキー経由の `icons/<Key>.png` で対応する。
- URL・同梱読み込みの成功後に、マニフェスト記載ファイルを JSON と同じディレクトリから `HttpClient` で取得する。取得数・失敗数をホームの文書情報に併記する（例: アイコン 3/5 件）。取得失敗は読み込み自体を妨げない（プレースホルダに落ちるだけ）。
- アイコン欄の UI は `CommonFieldsEditor` の `IconKey` 行を差し替える形で全エンティティに共通実装する。要素はプレビュー（解決できれば画像、できなければプレースホルダ）、「画像を選択」（`InputFile`）、「クリア」ボタン、`IconKey` テキスト入力。レシピでは主出力アイテムへのフォールバックを考慮した実効アイコンをプレビューに使う。
- 「画像を選択」は `.png` `.jpg` 等の画像入力を JS 側で 128×128 PNG へ正規化し、返ってきた PNG バイト列をストアへ格納、マニフェストへキーで upsert する。`IconKey` が空または文字種に合わない場合は `icon-<Id>`（無効文字は `-` へ置換、64 文字に丸める）で補完してから登録する。
- 「クリア」は `IconKey` を null にするだけとし、マニフェストエントリは残す（孤立エントリはエクスポート時に出力対象外になるため、実害はない）。
- エクスポートは「エクスポート（zip）」ボタン 1 つとし、実行時に `IconExportPlanner` でマニフェストを再構成して `document.Icons` へ反映してから `MasterExporter.Export` する。成功したら `IconArchive` で zip を組み立ててダウンロードする。違反があれば従来どおり一覧表示して書き出さない。
- JS は `js/icons.js` を新設する（正規化 `normalizeIconPng`、バイナリダウンロード `downloadBlobFile`）。既存 `download.js` のテキスト版は残す。

### 3.3 App の構成

- `Services/IconCatalog` を新設する。`MasterDataService.LoadAsync` 成功後に、マニフェスト記載ファイルを `data/` から一括取得し、`Sha256`/`Bytes` 一致のみをインメモリプロバイダへ格納する。収録外だがエンティティの `IconKey` で参照されているキーについても `data/icons/<Key>.png` を取得試行し、見つかった分だけをファイル名解決へ回す（仕様決定 CY の収録外キー解決。404 は正常フォールバック）。
- `IconCatalog` は `IconResolver` で `IconKey` → ファイルパスを解決し、バイト列を `data:image/png;base64,…` URI に変換してキー単位でキャッシュする。未解決は null とし、表示側は既存のプレースホルダ（`.icon-slot` の `?`）へ落とす。
- 表示部品として `EntityIcon.razor` を用意し、`IconKey`（またはレシピフォールバック済みキー）と代替テキストを受けて画像またはプレースホルダを描画する。
- 適用箇所は `Home.razor` のうちエンティティ名を出す箇所（素材行・設備行・環境行・余剰行・アイテム選択候補・供給内訳/流量制限のレシピ名）。`<option>` 内には画像を置けないためペア選択ドロップダウンは文字のままとする。
- 環境・イベント・レシピの `IconKey` も共通属性として存在するため、表示できる箇所には同じ部品で出す。レシピ名表示はフォールバック解決後のキーを渡す。

### 3.4 サンプルデータへのアイコン移植

- 旧リポジトリ `data/icons/` の権利クリア済み画像（128×128 PNG、自作）のうち、現行サンプルのエンティティ Id に対応する `icon-item-ore`・`icon-item-fuel`・`icon-item-part`・`icon-item-power`・`icon-fac-assembler` の 5 件を `data/icons/` へコピーする。
- 対応エンティティ（`item-ore`・`item-fuel`・`item-part`・`item-power`・`fac-assembler`）の `IconKey` と `Icons` マニフェストを `data/master.json` に設定し、`DataVersion` を `0.2.0` へ上げる。
- この差分は管理ツールのエクスポート物と byte 一致する形を確認する（実運用手順と同じ出力形式の検証になる）。
- 対応する旧アイコンがないエンティティ（`item-gas`・`item-part-hp`・`item-limited`・`fac-dispenser`・`env-gas`・`ev-first`・各レシピ）は `IconKey` 未設定のままとし、プレースホルダ表示の確認材料とする。

### 3.5 CI の照合強化

- `tools/validate_master.py` に `Icons` 節と `data/icons/` の照合を追加する。各マニフェストエントリについて `File` の実在、`Bytes`・`Sha256` の一致を検査し、不一致は失敗とする。
- エンティティから参照されていないマニフェストエントリ（孤立）と、マニフェストに載らない `data/icons/` 内ファイルは警告出力のみとする（差し込み運用を妨げない）。
- JSON スキーマ側の `Icons` 構造検査は既存のままとする。

## 4. 画面変更の確認手順（ui-mock-first）

1. `dotnet build`・`dotnet test` を通す。
2. Admin を `dotnet run --project src/EndfieldAicWeb.Admin --no-launch-profile --urls http://127.0.0.1:5181` で起動し、自分で次を確認する。
   - 同梱マスタの読み込みでアイコン取得数が出る
   - アイテム編集で画像を選択すると 128×128 PNG 正規化済みのプレビューが出る
   - クリアで `IconKey` が空になりプレビューがプレースホルダへ戻る
   - エクスポートで `master-export.zip` がダウンロードされ、展開すると `data/master.json`＋`data/icons/` になる
   - エクスポート zip をファイル選択から読み込み直せる（往復）
3. App を `dotnet run --project src/EndfieldAicWeb.App --no-launch-profile --urls http://127.0.0.1:5180` で起動し、素材行・設備行・アイテム選択でアイコンが出ること、`IconKey` 未設定のエンティティが `?` プレースホルダで破綻しないことを自分で確認する。
4. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する（ui-mock-first。テスト作成はこの後）。
5. 本書 §3.4 のサンプルデータ差分をエクスポート物と突合し、CI（`validate_master.py` 含む）と Pages 配信まで通す。

## 5. 作業順序

1. 本書を作成する。
2. Infrastructure に `InMemoryIconFileProvider`・`IconExportPlanner`・`IconArchive` を実装し、`IconFiles` を public 化する。
3. Admin にアイコンストア・読み込み経路別の取得・編集 UI・正規化 JS・zip エクスポートを実装する。
4. App に `IconCatalog`・`EntityIcon` を実装し、`Home.razor` の各表示箇所へ組み込む。
5. `validate_master.py` にアイコン照合を追加する。
6. ローカル起動で §4 の自分確認を実施する。
7. ブラウザプレビューでユーザーに触ってもらい、フィードバックを反映する。
8. [test-specification-phase7.md](test-specification-phase7.md) を作成し、テストを実装して `dotnet test` 全緑にする。
9. サンプルデータへのアイコン移植を入れ、`implementation-plan.md` の Phase 7 チェックリストを実施済み範囲で更新して PR を作成する。
