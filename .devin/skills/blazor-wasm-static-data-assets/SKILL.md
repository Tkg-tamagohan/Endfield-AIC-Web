---
name: blazor-wasm-static-data-assets
description: Endfield-AIC-Web の Blazor WASM プロジェクトでリポジトリ内データを静的アセットとして配信する際の構成と落とし穴。wwwroot への配置、StaticWebAssets の二重登録競合、dotnet publish での同梱を扱う。master.json やアイコンなど data/ 配下を App/Admin に配る変更をするときに使用する。
---

# Blazor WASM での静的データ配信

リポジトリルートの `data/`（master.json、アイコン）を WASM プロジェクトから配信する構成は、三つの落とし穴を踏んだ末に現在の形になっている。
この経路に触れる変更では、各落とし穴に対応するかを確認する。

## 落とし穴

- **`Content` リンクだけでは wwwroot に置かれない**。プロジェクト外のファイルを `Content Include` でリンクしても出力に含まれず、配信側は 404 になる。
- **物理コピー＋Content リンクの二重登録はビルドエラー**。同じ論理パスが「kind All」の StaticWebAssets 競合になる。
- **Copy ターゲットだけでは publish に同梱されない**。ビルド時コピーは `dotnet run` では配信されるが、クリーンな `dotnet publish` の出力から漏れる。publish 用には `AfterTargets="Publish"` のコピーターゲットも必要。

## 現行構成の確認先

- `src/*/EndfieldAicWeb.*.csproj` の `CopyMasterJson`（開発用）と `CopyMasterJsonToPublish`（publish 用）。
- `src/*/wwwroot/data/` はこのターゲットが `data/` からコピーする生成物で、gitignore 済み。直接編集しない。

## 補足

- Razor テキスト中のリテラル `@` は `@@` にエスケープする。`@Foo` は式として評価され、文字としては描画されない。
