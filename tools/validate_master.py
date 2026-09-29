#!/usr/bin/env python3
"""data/master.json を data/master.schema.json（SchemaVersion=1）で検証する。

CI（.github/workflows/ci.yml）から呼び出し、正本と構造定義のずれを検出する。
参照整合性・ペア一意性などの意味検証は Domain の検証に委ね、ここでは構造と
Icons マニフェスト↔ファイル実体の整合だけを扱う（Phase 7 仕様決定 R）。

- マニフェスト記載ファイルの欠落・Bytes/Sha256 不一致: 失敗
- IconKey 文字種（^[A-Za-z0-9_-]{1,64}$）違反: 失敗（ファイル名導出の前提）
- 未参照マニフェストエントリ・マニフェスト未収録の画像ファイル: 警告のみ
"""
import hashlib
import json
import re
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parent.parent
SCHEMA_PATH = ROOT / "data" / "master.schema.json"
MASTER_PATH = ROOT / "data" / "master.json"
ICONS_DIR = ROOT / "data" / "icons"

ICON_KEY_PATTERN = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
PLACEHOLDER_KEY = "icon-placeholder"

ENTITY_SECTIONS = ("Items", "Facilities", "Environments", "GameEvents", "Recipes")


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


def check_icons(master: dict) -> tuple[list[str], list[str]]:
    """マニフェスト↔実ファイルの整合を検査し (失敗, 警告) を返す。"""
    failures: list[str] = []
    warnings: list[str] = []

    manifest = master.get("Icons") or []
    referenced = referenced_icon_keys(master)

    # IconKey 文字種（ファイル名導出の前提なので構造側で先に止める）
    for section in ENTITY_SECTIONS:
        for entity in master.get(section) or []:
            key = entity.get("IconKey")
            if key is not None and not ICON_KEY_PATTERN.match(key):
                failures.append(
                    f"{section}/{entity.get('Id', '?')}: IconKey が文字種に反します: {key!r}"
                )
    for entry in manifest:
        key = entry.get("Key", "?")
        if not ICON_KEY_PATTERN.match(key):
            failures.append(f"Icons/{key}: マニフェスト Key が文字種に反します")
        expected_file = f"icons/{key}.png"
        if entry.get("File") != expected_file:
            failures.append(
                f"Icons/{key}: File は {expected_file} 固定です: {entry.get('File')!r}"
            )

    # マニフェスト記載ファイルの実在・サイズ・ハッシュ
    seen_files: set[str] = set()
    for entry in manifest:
        key = entry.get("Key", "?")
        file_rel = entry.get("File", "")
        path = ROOT / "data" / file_rel
        if not file_rel.startswith("icons/") or ".." in file_rel.split("/"):
            failures.append(f"Icons/{key}: File が icons/ 配下を指していません: {file_rel!r}")
            continue
        seen_files.add(file_rel)
        if not path.is_file():
            failures.append(f"Icons/{key}: ファイルがありません: data/{file_rel}")
            continue
        content = path.read_bytes()
        if entry.get("Bytes") != len(content):
            failures.append(
                f"Icons/{key}: Bytes が実ファイルと一致しません（{entry.get('Bytes')} != {len(content)}）"
            )
        digest = hashlib.sha256(content).hexdigest()
        if entry.get("Sha256", "").lower() != digest:
            failures.append(f"Icons/{key}: Sha256 が実ファイルと一致しません")

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

    return failures, warnings


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

    icon_failures, icon_warnings = check_icons(master)
    for warning in icon_warnings:
        print(f"警告: {warning}")
    if icon_failures:
        for failure in icon_failures:
            print(f"エラー: {failure}")
        print(f"アイコンの整合チェックに失敗しました（{len(icon_failures)} 件）")
        return 1

    print("master.json はスキーマ v1 に適合し、アイコン整合チェックも問題ありません")
    return 0


if __name__ == "__main__":
    sys.exit(main())
