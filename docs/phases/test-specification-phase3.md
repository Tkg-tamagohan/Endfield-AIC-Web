# Phase 3 テスト仕様書

**対象**: Phase 3 成果物（Infrastructure のマスタ JSON 読み込み・エクスポート・アイコンマニフェスト解決）
**前提ドキュメント**: [requirements.md](../requirements.md)、[decision-records.md](../decision-records.md)、[implementation-plan.md](../implementation-plan.md)、[implementation-plan-phase3.md](implementation-plan-phase3.md)

> 本書は Phase 3 の受け入れ条件を検証するためのテスト項目と仕様を定める。
> 項目 ID は `分類-連番` で採番し、要件との対応をトレースできるようにする。
> テストケースは実装ではなく本書の記述を根拠に作成する。

## 1. テスト環境と実行方法

| 項目 | 内容 |
|---|---|
| 自動テスト基盤 | xUnit。`tests/EndfieldAicWeb.Infrastructure.Tests` に配置 |
| テストデータ | 有効な最小 JSON 文字列（下記 J-01）をひな形にし、改変して各ケースを作る。`data/master.json` は出力ディレクトリへコピー済みのものをそのまま使う |
| アイコン検証用ファイル | バイト列をコード上で生成し、`IIconFileProvider` のインメモリ実装で与える。内容が実 PNG である必要はない（照合対象はバイト列とハッシュ） |
| 実行コマンド | `dotnet test` |
| 実行環境 | Linux。CI（ubuntu-latest）でも実行される |

## 2. JSON フィクスチャ定義

### J-01: 有効な最小 JSON

`SchemaVersion=1`、`DataVersion="1.0.0"`、全配列を持つ最小構成。

| 種別 | Id | 内容 |
|---|---|---|
| Item | `i-ore` | 採取素材、TransportKind=Belt |
| Item | `i-part` | Category=部品、TransportKind=Belt |
| Item | `i-gas` | 採取素材、TransportKind=Pipe |
| Item | `i-power` | Category=エネルギー、TransportKind=None |
| Facility | `f-asm` | Width=3、Height=3、PowerConsumption=50 |
| Facility | `f-disp` | Width=2、Height=2、PowerConsumption=20 |
| Environment | `env-gas` | ProviderFacilityId=`f-disp`、ConsumeItemId=`i-gas`、ConsumeRatePerMinute=360 |
| GameEvent | `ev-first` | ActiveFrom/ActiveTo=null |
| Recipe | `r-part` | 入力 `i-ore`×2、出力 `i-part`×1（SortOrder=0）、ペア (f-asm, 4秒, env=null, FixedConsumption=null)・(f-asm, 3秒, env=`env-gas`, FixedConsumption=`i-gas`×30/分) |
| Icons | `icon-ore` | Key=`icon-ore`、File=`icons/icon-ore.png`、Sha256・Bytes はフィクスチャバイト列から算出 |

各ケースはこのひな形を改変して作る。改変方法は JSON テキストのプロパティ操作（削除・置換・追記）とする。

### J-02: `data/master.json`

リポジトリ同梱の開発用サンプル。往復テストの正本として使う。

## 3. テスト項目一覧

### SYN: JSON 構文

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| SYN-01 | 空文字・空白のみ | `""`、`"   "` | エラー一覧に集約される（例外を投げない。`Success`=false） |
| SYN-02 | 構文が壊れた JSON | `{ "Items": [` | 同上 |
| SYN-03 | トップレベルが配列 | `[]` | 同上 |
| SYN-04 | 型不一致 | `"SchemaVersion": "one"`、`"Width": "abc"` | 同上 |

### STR: 構造検証（スキーマ規則）

| ID | 内容 | 入力（J-01 の改変） | 期待 |
|---|---|---|---|
| STR-01 | SchemaVersion 欠落 | キー削除 | エラー一覧 |
| STR-02 | 未知 SchemaVersion | `2`、`0`、`999` | それぞれ拒否 |
| STR-03 | DataVersion 欠落・空文字 | キー削除・`""` | それぞれエラー |
| STR-04 | 必須配列の欠落 | `Items` 削除・`Icons` 削除 | それぞれエラー |
| STR-05 | 配列に null 要素 | `Items: [null]` | エラー |
| STR-06 | Id・Name の欠落・空 | Item の `Id` 削除・`Name=""` | それぞれエラー |
| STR-07 | enum 定義値外 | `TransportKind: "Rocket"`・`"belt"`・`"1"` | それぞれエラー |
| STR-08 | 必須フィールド欠落 | `Width` なし・`CycleTime` なし・`IsGatherable` なし・`ConsumeRatePerMinute` なし・`SortOrder` なし | それぞれエラー |
| STR-09 | SortOrder が負 | `SortOrder: -1` | エラー |
| STR-10 | FixedConsumption 要素欠落 | `ItemId` なし・`RatePerMinute` なし | それぞれエラー |
| STR-11 | Icons 節の構造違反 | Key 重複・Key 文字種違反・Key 末尾改行・`File` が `icons/<Key>.png` 以外・`Sha256` が 64 桁でない・大文字 hex・末尾改行・`Bytes` が 0・Key 欠落 | それぞれエラー |
| STR-12 | 正当な最小 JSON | J-01 | `Success`=true、`Document` 非 null、`Errors` 0 件 |
| STR-13 | スキーマ必須の nullable キー欠落 | `VersionRemoved`・`IconKey`・`GameEventId`・`EnvironmentId` 削除 | それぞれエラー |
| STR-14 | Description が null | Item の `Description=null` | エラー |
| STR-15 | スキーマ外プロパティ | ルート・Item に `Notes` 追加 | それぞれエラー |

### SEM: 意味検証（MasterValidator 同一規則）

| ID | 内容 | 入力（J-01 の改変） | 期待 |
|---|---|---|---|
| SEM-01 | 参照不整合の集約 | 入力 ItemId・ペア FacilityId・ペア EnvironmentId・FixedConsumption.ItemId・環境 ProviderFacilityId・環境 ConsumeItemId・各 GameEventId を未知 Id へ | すべてエラー一覧に集約される |
| SEM-02 | ペア重複（P） | 同一レシピ内に全要素同一のペア 2 行 | エラー |
| SEM-03 | 範囲外数値 | `CycleTime: 0`・`Width: -1`・`Quantity: 0`・`ConsumeRatePerMinute: 0`・`RatePerMinute: -0.5`・`PowerConsumption: -1` | それぞれエラー |
| SEM-04 | ID の重複 | Items に同一 Id 2 件 | エラー |
| SEM-05 | 仮想アイテム規則 | `i-power` をレシピ入力に追加 | エラー |
| SEM-06 | 構造は正しいが意味違反を含む文書 | SEM-02 のペア重複文書 | `Success`=false、`Errors` に集約。例外は投げない |

### XPT: エクスポート

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| XPT-01 | 正当なドキュメントの書き出し | J-01 相当の `MasterDocument` | `SchemaVersion`=1・`DataVersion`・全配列キーを含む JSON |
| XPT-02 | null フィールドの出力 | 同上 | `IconKey`・`VersionRemoved`・`GameEventId`・`EnvironmentId`・`FixedConsumption` が null 値のまま出力される |
| XPT-03 | ペアに RecipeId を書かない | 同上 | 出力 JSON の `Facilities` 要素に `RecipeId` キーがない |
| XPT-04 | 無効ドキュメントの拒否 | 参照欠落など検証違反を含む `MasterDocument` | `MasterValidationException`（違反一覧を保持） |
| XPT-05 | SchemaVersion が 1 以外 | `SchemaVersion=2` のドキュメント | 例外で拒否 |
| XPT-06 | DataVersion が空 | `DataVersion=""` のドキュメント | 例外で拒否 |
| XPT-07 | 日本語の書き出し | 日本語名を含むドキュメント | `\uXXXX` エスケープではなく日本語文字のまま出力される |
| XPT-08 | 不正 Icons エントリの拒否 | `Sha256` が 64 桁でないエントリを含むドキュメント | 例外で拒否 |
| XPT-09 | レシピ内の null 要素 | `Inputs`/`Outputs`/`Facilities` に null 要素または null 配列 | `MasterValidationException`（NRE で落ちない） |
| XPT-10 | Description=null | `Items[0].Description=null` のドキュメント | 例外で拒否 |
| XPT-11 | SortOrder が負 | `Outputs[0].SortOrder=-1` のドキュメント | 例外で拒否（再読み込み不能な出力を防ぐ） |

### RND: 往復（読み込み → エクスポート）

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| RND-01 | 往復で JSON 出力が同一 | J-02 | 読み込み → エクスポート → 読み込み → エクスポートで 2 出力が文字列一致 |
| RND-02 | 往復で内容が保存される | J-02 | 再読み込み結果の件数・代表フィールド・ペアの `EnvironmentId`/`FixedConsumption`・Icons が一致 |
| RND-03 | 同梱 master.json が正として読める | J-02 | `Success`=true、`Errors` 0 件 |

### ICO: アイコンマニフェスト

| ID | 内容 | 入力 | 期待 |
|---|---|---|---|
| ICO-01 | 正当なマニフェストと実ファイル | `icon-ore` のバイト列と一致する Sha256/Bytes のエントリ | `Verify` エラーなし |
| ICO-02 | Bytes 不一致 | Bytes を +1 改変 | `Verify` エラー |
| ICO-03 | Sha256 不一致 | 別バイト列のハッシュを記載 | `Verify` エラー |
| ICO-04 | ファイル欠落 | プロバイダにファイルを置かない | `Verify` エラー |
| ICO-05 | 収録キーの解決 | 照合一致のマニフェスト＋実ファイルで `Resolve("icon-ore")` | `icons/icon-ore.png` を返す |
| ICO-06 | 収録キーで不一致・欠落 | Sha256 不一致・ファイルなし | `Resolve` は null（フォールバック） |
| ICO-07 | 収録外キーの解決 | マニフェストなし・`icons/<Key>.png` が存在 | `icons/<Key>.png` を返す |
| ICO-08 | 解決不可のフォールバック | null・空・予約キー `icon-placeholder`・文字種違反キー・ファイルなし | すべて null |
| ICO-09 | ファイルシステム経由の解決 | 実ディレクトリに `icons/<Key>.png` を配置 | `FileSystemIconProvider` 経由で解決・欠落時 null |
| ICO-10 | ルート外パスの拒否 | `../secret.txt`・`icons/../../secret.txt`・`..\secret.txt` | `ReadAllBytes` は null |
| ICO-11 | ルート末尾が区切り文字 | `rootDirectory` に末尾 `/` 付きで `icons/<Key>.png` 配置 | 解決できる |
| ICO-12 | シンボリックリンクによるルート外参照 | icons 内にルート外ファイルへの symlink | `ReadAllBytes` は null |

## 4. 受け入れ条件との対応

- 全項目緑であること。
- SYN・STR・SEM の各項目で、違反が例外ではなくエラー一覧として返ることを確認する（Phase 3 の「違反は例外ではなくエラー一覧として集約して返す」に対応）。
- RND-01〜03 で「往復（読み込み→エクスポート）で内容が保存される」を確認する。
