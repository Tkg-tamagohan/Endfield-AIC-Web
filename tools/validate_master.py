#!/usr/bin/env python3
"""data/master.json を data/master.schema.json（SchemaVersion=1）で検証する。

CI（.github/workflows/ci.yml）から呼び出し、マスタ JSON と構造定義のずれを検出する。
IconKey 文字種・File 形式・Bytes/Sha256 などの意味検証は C# 側の規則で担保する
（dotnet test の BundledMasterDataTests、Phase 7 仕様決定 R）ため、
ここではスキーマ適合と、参照・収録の偏りを知らせる警告だけを扱う。

- スキーマ不適合: 失敗
- 参照されているのに画像が無いキー・未参照マニフェストエントリ・マニフェスト未収録の画像ファイル: 警告のみ
"""
import json
import re
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parent.parent
SCHEMA_PATH = ROOT / "data" / "master.schema.json"
MASTER_PATH = ROOT / "data" / "master.json"
ICONS_DIR = ROOT / "data" / "icons"

# Python の $ は末尾改行の直前でも一致するため、アプリ側 IconKeyRules と揃うよう \Z を使う。
ICON_KEY_PATTERN = re.compile(r"^[A-Za-z0-9_-]{1,64}\Z")
PLACEHOLDER_KEY = "icon-placeholder"

ENTITY_SECTIONS = ("Items", "Facilities", "Environments", "GameEvents", "Recipes", "Maps")


def referenced_icon_keys(master: dict) -> list[str]:
    """エンティティの IconKey を出現順に列挙する（null・プレースホルダ・重複を除く）。"""
    keys: list[str] = []
    seen: set[str] = set()
    for section in ENTITY_SECTIONS:
        for entity in master.get(section) or []:
            key = entity.get("IconKey")
            if key and key != PLACEHOLDER_KEY and key not in seen:
                seen.add(key)
                keys.append(key)
    return keys


def icon_warnings(master: dict) -> list[str]:
    """マニフェスト↔参照・ファイル収録の偏りを警告として列挙する。"""
    warnings: list[str] = []

    manifest = master.get("Icons") or []
    referenced = referenced_icon_keys(master)
    manifest_keys = {e.get("Key") for e in manifest}
    manifest_files = {e.get("File") for e in manifest}

    # 参照されているのにマニフェストもファイルも無いキー → 公開側は必ず欠落するので警告
    for key in referenced:
        if not ICON_KEY_PATTERN.match(key):
            continue
        if key not in manifest_keys and not (ICONS_DIR / f"{key}.png").is_file():
            warnings.append(
                f"IconKey {key}: 参照されていますがマニフェストにも data/icons/ にも画像がありません（プレースホルダ表示になります）"
            )

    # 孤立エントリ・未収録ファイルは警告のみ（エクスポート時に落ちる差し込み経路も想定）
    for entry in manifest:
        if entry.get("Key") not in referenced:
            warnings.append(f"Icons/{entry.get('Key')}: どのエンティティからも参照されていません")
    if ICONS_DIR.is_dir():
        for file in sorted(ICONS_DIR.iterdir()):
            if file.suffix == ".png":
                rel = f"icons/{file.name}"
                if rel not in manifest_files:
                    warnings.append(f"{rel}: マニフェストに収録されていない画像ファイルです")

    return warnings


def main() -> int:
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)

    master = json.loads(MASTER_PATH.read_text(encoding="utf-8"))
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    errors = sorted(
        validator.iter_errors(master),
        key=lambda e: list(e.absolute_path),
    )
    for error in errors:
        path = "/".join(str(p) for p in error.absolute_path) or "(root)"
        print(f"{path}: {error.message}")
    if errors:
        print(f"master.json がスキーマに適合しません（{len(errors)} 件）")
        return 1

    for warning in icon_warnings(master):
        print(f"警告: {warning}")

    print("master.json はスキーマ v1 に適合します")
    return 0


if __name__ == "__main__":
    sys.exit(main())
