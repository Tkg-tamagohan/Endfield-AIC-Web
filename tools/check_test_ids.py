#!/usr/bin/env python3
"""tests/ のテスト ID と docs/phases/test-specification-*.md の登録行を両方向に照合する。

CI（.github/workflows/ci.yml）から呼び出し、帳簿（テスト仕様書・追補書）への
登録漏れや齟齬を検出する。コード側は追補書 §1 が認める 3 形式を抽出する。

- F1: `[Fact(DisplayName = "PREFIX-NN: …")]`（Theory 含む）
- F2: メソッド名 `PREFIXNN_…`
- F3: `// PREFIX-NN` または `/// PREFIX-NN` で始まるコメント（`NN〜MM` 範囲は展開）

文書側は表行の最初のセルが ID トークンになる行を登録行とし、`NN〜MM` 範囲と
`・`・`/` 区切りの連番を展開する。「廃止するケース」節の行は廃止指定であり登録に数えない。
`> ID 採番:` 行や「現行最大は X-NN」のような言及は登録行でないため自然に対象外になる。

分類（E1 のみ失敗、ほかは報告のみ）:

- E1 未登録: コードに実在するが登録行がない
- W1 文書のみ: 登録行はあるがコード側に実在しない
- W2 同名衝突: 同一 ID が複数ファイルに実在する
- W3 マーカーなしテスト: `[Fact]`/`[Theory]` の直前ブロックに ID マーカーがない
- I1 再登録: 同一 ID が複数の文書に登録行を持つ

正常ケース（手動確認項目、廃止・移管済み ID、既知の同番号別対象）は
tools/test-id-exceptions.txt の除外・許容規約で抑止する。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TESTS_DIR = ROOT / "tests"
DOCS_GLOB = "docs/phases/test-specification-*.md"
EXCEPTIONS_PATH = ROOT / "tools" / "test-id-exceptions.txt"

# ID の接頭辞は 2〜6 文字の英大文字（SNP・SCAF・ADMIN 系までを想定）。
# 内部の正規形は接頭辞 + 番号の数値化（ゼロ埋め差を吸収）+ 派生サフィックス b。
PREFIX = r"[A-Z]{2,6}"

F1_RE = re.compile(rf"DisplayName\s*=\s*\"\s*({PREFIX})-(\d+)(b?)")
F2_RE = re.compile(
    rf"(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?"
    rf"(?:void|Task|ValueTask)\s+({PREFIX})(\d+)(b?)\w*\s*\("
)
F3_RE = re.compile(rf"//[/]?\s*({PREFIX})-(\d+)(b?)(?:\s*〜\s*(\d+))?")

ATTR_OR_COMMENT_RE = re.compile(r"^\s*(\[|//)")
FACT_RE = re.compile(r"\[(?:Fact|Theory)\b")
METHOD_RE = re.compile(
    r"^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?"
    r"(?:void|Task|ValueTask)\s+(\w+)\s*\("
)


def canon(prefix: str, num, suffix: str = "") -> str:
    return f"{prefix}-{int(num)}{suffix}"


def disp(test_id: str) -> str:
    """表示用に番号を 2 桁へ戻す（ADM-9 → ADM-09、MN-122 はそのまま）。"""
    prefix, rest = test_id.split("-", 1)
    m = re.match(r"^(\d+)(b?)$", rest)
    if not m:
        return test_id
    return f"{prefix}-{int(m.group(1)):02d}{m.group(2)}"


def extract_code_ids(path: Path) -> set[str]:
    """1 ファイルから F1〜F3 の ID を抽出する。"""
    text = path.read_text(encoding="utf-8")
    ids = set()
    for m in F1_RE.finditer(text):
        ids.add(canon(m.group(1), m.group(2), m.group(3)))
    for m in F2_RE.finditer(text):
        ids.add(canon(m.group(1), m.group(2), m.group(3)))
    for m in F3_RE.finditer(text):
        prefix, start, suffix, end = m.group(1), int(m.group(2)), m.group(3), m.group(4)
        if end:
            for n in range(start, int(end) + 1):
                ids.add(canon(prefix, n, suffix))
        else:
            ids.add(canon(prefix, start, suffix))
    return ids


def markerless_tests(path: Path) -> list[tuple[int, str]]:
    """[Fact]/[Theory] の直前ブロック（属性と // コメントの連続行）に
    ID マーカー（F1/F2/F3）がないテストメソッドを返す。"""
    lines = path.read_text(encoding="utf-8").splitlines()
    missing = []
    for i, line in enumerate(lines):
        m = METHOD_RE.match(line)
        if not m:
            continue
        j = i - 1
        block = []
        while j >= 0 and ATTR_OR_COMMENT_RE.match(lines[j]):
            block.append(lines[j])
            j -= 1
        if not any(FACT_RE.search(b) for b in block):
            continue
        has_marker = (
            any(F1_RE.search(b) or F3_RE.search(b) for b in block)
            or re.match(rf"^{PREFIX}\d+b?_", m.group(1)) is not None
        )
        if not has_marker:
            missing.append((i + 1, m.group(1)))
    return missing


def expand_id_field(text: str) -> set[str]:
    """`SEL-01〜25・SEL-26`、`SEL-01〜05・07〜22`、`FLW-01/02/04` のような
    複合セルや例外指定の対象欄を展開して正規形の ID 集合にする。

    番号だけの継続（`02`）や範囲（`07〜22`）は直前の接頭辞を継ぐ。
    `NN〜`（開放範囲）は起点の ID のみとして扱う。
    """
    text = re.sub(r"（[^）]*）", "", text)
    ids = set()
    prefix = None
    for tok in re.split(r"[・/、,\s]+", text):
        if not tok:
            continue
        m = re.match(rf"^({PREFIX})-(\d+)(b?)\s*〜\s*(\d+)$", tok)
        if m:
            prefix = m.group(1)
            for n in range(int(m.group(2)), int(m.group(4)) + 1):
                ids.add(canon(prefix, n, m.group(3)))
            continue
        m = re.match(rf"^({PREFIX})-(\d+)(b?)\s*〜?$", tok)
        if m:
            prefix = m.group(1)
            ids.add(canon(prefix, m.group(2), m.group(3)))
            continue
        m = re.match(r"^(\d+)\s*〜\s*(\d+)$", tok)
        if m and prefix:
            for n in range(int(m.group(1)), int(m.group(2)) + 1):
                ids.add(canon(prefix, n))
            continue
        m = re.match(r"^(\d+)$", tok)
        if m and prefix:
            ids.add(canon(prefix, m.group(1)))
    return ids


def extract_doc_ids():
    """登録行の ID（文書→集合）と、廃止指定された ID の集合を返す。"""
    registered: dict[str, set[str]] = {}
    abandoned: set[str] = set()
    for path in sorted(ROOT.glob(DOCS_GLOB)):
        rel = path.relative_to(ROOT).as_posix()
        in_abandoned = False
        for line in path.read_text(encoding="utf-8").splitlines():
            heading = re.match(r"^#{1,6}\s*(.*)", line)
            if heading:
                in_abandoned = "廃止" in heading.group(1)
                continue
            if not line.lstrip().startswith("|"):
                continue
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            if not cells or not re.match(rf"^{PREFIX}-\d", cells[0]):
                continue
            ids = expand_id_field(cells[0])
            if in_abandoned:
                abandoned |= ids
            else:
                for test_id in ids:
                    registered.setdefault(test_id, set()).add(rel)
    return registered, abandoned


def load_exceptions():
    """tools/test-id-exceptions.txt を読む。戻り値は種別ごとの集合。

    形式は「種別: 対象  # 理由」。対象は ID の列挙（範囲・区切り可）または接頭辞。
    docs-only-prefix: 文書のみ許容の接頭辞 / docs-only: 文書のみ許容の ID
    collision: 複数ファイル実在を許容する ID / rereg-prefix: 再掲を許容する接頭辞
    """
    prefixes_docs_only: set[str] = set()
    prefixes_rereg: set[str] = set()
    ids_docs_only: set[str] = set()
    ids_collision: set[str] = set()
    if not EXCEPTIONS_PATH.is_file():
        return prefixes_docs_only, prefixes_rereg, ids_docs_only, ids_collision
    for line in EXCEPTIONS_PATH.read_text(encoding="utf-8").splitlines():
        body = line.split("#", 1)[0].strip()
        if not body or ":" not in body:
            continue
        kind, _, target = body.partition(":")
        kind, target = kind.strip(), target.strip()
        if kind in ("docs-only-prefix", "rereg-prefix"):
            if re.fullmatch(PREFIX, target):
                (prefixes_docs_only if kind == "docs-only-prefix" else prefixes_rereg).add(target)
            else:
                print(f"警告: test-id-exceptions.txt の「{line.strip()}」は接頭辞ではありません")
        elif kind in ("docs-only", "collision"):
            ids = expand_id_field(target)
            if not ids:
                print(f"警告: test-id-exceptions.txt の「{line.strip()}」から ID を読めませんでした")
            (ids_docs_only if kind == "docs-only" else ids_collision).update(ids)
        else:
            print(f"警告: test-id-exceptions.txt の種別「{kind}」は不明です")
    return prefixes_docs_only, prefixes_rereg, ids_docs_only, ids_collision


def main() -> int:
    code_ids: dict[str, set[str]] = {}
    markerless: list[tuple[str, int, str]] = []
    for path in sorted(TESTS_DIR.glob("**/*.cs")):
        rel = path.relative_to(ROOT).as_posix()
        for test_id in extract_code_ids(path):
            code_ids.setdefault(test_id, set()).add(rel)
        markerless.extend((rel, line, name) for line, name in markerless_tests(path))

    registered, abandoned = extract_doc_ids()
    prefixes_docs_only, prefixes_rereg, ids_docs_only, ids_collision = load_exceptions()

    def docs_only_allowed(test_id: str) -> bool:
        return test_id in ids_docs_only or test_id.split("-", 1)[0] in prefixes_docs_only

    # E1: コードに実在するが登録行も廃止行もなく、例外でもない
    e1 = [
        (test_id, sorted(code_ids[test_id]))
        for test_id in sorted(code_ids)
        if test_id not in registered and test_id not in abandoned and not docs_only_allowed(test_id)
    ]
    # W1: 登録行はあるがコード側に実在しない（例外指定は除く）
    w1 = [
        (test_id, sorted(registered[test_id]))
        for test_id in sorted(registered)
        if test_id not in code_ids and not docs_only_allowed(test_id)
    ]
    # W2: 同一 ID が複数ファイルに実在する（許容指定は除く）
    w2 = [
        (test_id, sorted(files))
        for test_id, files in sorted(code_ids.items())
        if len(files) > 1 and test_id not in ids_collision
    ]
    # I1: 同一 ID が複数文書に登録行を持つ（許容接頭辞は除く）
    i1 = [
        (test_id, sorted(files))
        for test_id, files in sorted(registered.items())
        if len(files) > 1 and test_id.split("-", 1)[0] not in prefixes_rereg
    ]

    for test_id, files in e1:
        print(f"E1: {disp(test_id)} はコードに実在しますが帳簿の登録行がありません（{', '.join(files)}）")
    for test_id, files in w1:
        print(f"警告 W1: {disp(test_id)} は帳簿に登録されていますがコード側に実在しません（{', '.join(files)}）")
    for test_id, files in w2:
        print(f"警告 W2: {disp(test_id)} は複数ファイルに実在します（{', '.join(files)}）")
    for rel, line, name in markerless:
        print(f"警告 W3: {rel}:{line} {name} に近接する ID マーカーがありません")
    for test_id, files in i1:
        print(f"情報 I1: {disp(test_id)} は複数の文書に登録されています（{', '.join(files)}）")

    print(
        f"照合結果: コード側 {len(code_ids)} 件・帳簿登録 {len(registered)} 件。"
        f"E1 {len(e1)} 件、警告 {len(w1) + len(w2) + len(markerless)} 件、情報 {len(i1)} 件"
    )
    if e1:
        print("未登録のテスト ID があります。追補書 §2 への登録か test-id-exceptions.txt の例外指定が必要です")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
