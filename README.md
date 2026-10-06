# Endfield-AIC-Web

「アークナイツ：エンドフィールド」の工業要素（AIC）向けに、目標アイテムの生産に必要な素材・設備・電力を逆算する Web アプリケーション。
旧デスクトップ版（[Tkg-tamagohan/Endfield-AIC-Planner](https://github.com/Tkg-tamagohan/Endfield-AIC-Planner)）を参考に、新ドメインモデルで作り直す。

Blazor WebAssembly ＋ Cloudflare Pages で構成し、Cloudflare 無料枠内で運用する。
公開中の計算アプリ: https://endfield-aic.pages.dev （`main` への push で `.github/workflows/deploy-pages.yml` が自動デプロイ。詳細は [Phase 5 実装計画](docs/phases/implementation-plan-phase5.md)。現在はマスタデータ作成中のため Cloudflare Access で一時的にメール OTP 認証を要求しており、再公開は Access 設定の解除で行う）。
管理ツール: https://endfield-aic-admin.pages.dev （Cloudflare Access のメール OTP で管理者のみに制限。詳細は [Phase 6 実装計画](docs/phases/implementation-plan-phase6.md)）。

## ドキュメント

- [要件定義書](docs/requirements.md): スコープ・計算要件・データモデル・アーキテクチャ
- [仕様決定記録](docs/decision-records.md): 確定した仕様上の判断事項
- [実装計画](docs/implementation-plan.md): フェーズ別タスクと進捗の管理
- [残課題](docs/remaining-issues.md): レビューで先送りした構造上の課題
- [Phase 別文書](docs/phases/): 各 Phase の実装詳細計画とテスト仕様（実装済みの作業記録。Phase 1・5 の一部は未作成）
- [レビュー観点](REVIEW.md): PR の AI レビュー観点（AI レビューア向け）

## 開発

.NET 8 SDK が必要。

```sh
dotnet build                                    # ソリューション全体をビルド
dotnet test                                     # 単体テストを実行
dotnet run --project src/EndfieldAicWeb.App     # 計算アプリを起動
dotnet run --project src/EndfieldAicWeb.Admin   # 管理ツールを起動
```

マスタ JSON は `data/master.json`（構造定義は `data/master.schema.json`）。
`tools/validate_master.py` で構造定義との照合を検証できる（CI でも実行される。要 `pip install 'jsonschema[format]'`）。

## License & Disclaimer

* ソースコードは [MIT License](LICENSE) で公開する。
* `data/icons/` 配下のアイコン画像は作者の自作であり、MIT License の対象外とする（All rights reserved）。本アプリケーション外での複製・改変・再配布は許可しない。
* 同梱のサードパーティ製ライブラリ（Bootstrap・UPNG.js・pako など）は、それぞれのライセンスに従う。
* `src/*/wwwroot/` の `favicon.png`・`icon-192.png` は .NET の Blazor WebAssembly テンプレートの既定画像であり、.NET Foundation の MIT License に従う。
* 本アプリケーションは『アークナイツ：エンドフィールド（Arknights: Endfield）』の非公式ファンツールであり、株式会社Hypergryphおよび関連会社とは一切関係ありません。
* 本アプリケーション内で使用・参照されているゲーム内の名称、データ、および世界観等に関する著作権および知的財産権は、すべて原著作者（Hypergryph）に帰属します。
