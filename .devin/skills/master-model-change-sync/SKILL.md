---
name: master-model-change-sync
description: data/master.json のモデル項目（エンティティ・属性）を追加・変更・削除したとき、モデル・スキーマ・データ・文書・テストを同期させるチェックリスト。Endfield-AIC-Web でマスタデータの構造を変えるときに使用する。
---

# マスタモデル変更の同期チェックリスト

ドメインモデルの項目変更は、コードだけでなくスキーマ・データ・文書・テストを横断する。
片方だけ変えると CI のスキーマ検証または意味検証で失敗する。
過去のモデル変更（旧 `Item.IsBaseMaterial`＝現 `IsGatherable` の追加）は 12 ファイル横断のコミットになっている。

## 手順

1. Domain 側のモデルを変更する。
2. `data/master.schema.json` を同じ変更に追従させる。
3. `data/master.json` の実データを新モデルに合わせる。フィクスチャの一括置換は `sed -i` 等で行い、置換後はコンパイルを通してから次へ進む。
4. 文書を揃える。`docs/requirements.md`、対象フェーズの `docs/phases/test-specification-phaseN.md`、`docs/phases/implementation-plan-phaseN.md`。モデルの意味が変わる変更は仕様決定として `docs/decision-records.md` にも記録する。
5. `~/.venvs/validate/bin/python tools/validate_master.py` でスキーマ検証を通す。環境ブループリントが用意する `~/.venvs/validate`（jsonschema 4.25.1 入り）を使う。この venv がない環境では `pip install 'jsonschema[format]==4.25.1'` した python で同じスクリプトを実行する（CI も同じ依存を入れて実行している）。
6. `dotnet test` が全緑であること、意味論の変更には ID 採番の回帰テストを追加したことを確認する。
7. 同梱マスタの内容（レシピ構成・件数・名称）を変えたときは `.devin/skills/testing-blazor-apps` のゴールデンパス表とフィクスチャ節を照合し、ずれていれば同じ変更で追従させる。

## 原則

- モデル変更は「モデル・スキーマ・データ・文書・テスト」の五点セットでコミットする。
- 意味が変わる変更は回帰テストの ID で追跡できるようにする。
