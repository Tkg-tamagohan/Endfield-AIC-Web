#!/usr/bin/env python3
"""data/master.json を data/master.schema.json（SchemaVersion=1）で検証する。

CI（.github/workflows/ci.yml）から呼び出し、正本と構造定義のずれを検出する。
参照整合性・ペア一意性などの意味検証は Domain の検証に委ね、ここでは構造のみを扱う。
"""
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parent.parent
SCHEMA_PATH = ROOT / "data" / "master.schema.json"
MASTER_PATH = ROOT / "data" / "master.json"


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
    print("master.json はスキーマ v1 に適合しています")
    return 0


if __name__ == "__main__":
    sys.exit(main())
