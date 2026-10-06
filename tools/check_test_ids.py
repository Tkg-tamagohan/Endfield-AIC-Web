#!/usr/bin/env python3
"""tests/ のテスト ID と docs/phases/test-specification-*.md の登録行を両方向に照合する。

CI（.github/workflows/ci.yml）から呼び出し、帳簿（テスト仕様書・追補書）への
登録漏れや齟齬を検出する。コード側は追補書 §1 が認める 3 形式を抽出する。

- F1: `[Fact(DisplayName = "PREFIX-NN: …")]`（Theory 含む）
- F2: メソッド名 `PREFIXNN_…`
- F3: `// PREFIX-NN` または `/// PREFIX-NN` で始まるコメント（`NN〜MM` 範囲は展開）

文書側は表行の最初のセルが ID トークンになる行を登録行とし、`NN〜MM` 範囲と
`・`・`/` 区切りの連番を展開する。「廃止するケース」節の行は廃止指定であり登録に数えない
（廃止済み ID をコード側で再利用する場合も新しい登録行が必要）。
文書は phase 番号順（追補書は最後）に読み、最後のイベントが登録の ID を現行登録とみなす。
`> ID 採番:` 行や「現行最大は X-NN」のような言及は登録行でないため自然に対象外になる。

分類（E1 のみ失敗、ほかは報告のみ）:

- E1 未登録: コードに実在するが現行の登録行がない
- W1 文書のみ: 現行の登録行はあるがコード側に実在しない
- W2 同名衝突: 同一 ID が複数ファイルに実在する
- W3 マーカーなしテスト: `[Fact]`/`[Theory]` の直前ブロックに ID マーカーがない
- I1 再登録: 同一 ID が複数の文書に登録行を持つ
- I2 コメント由来のみ: 個々のテストメソッドに紐付かないコメント（クラス要約・
  範囲一覧など）だけが根拠の ID。実在扱いにはするが、個別テストの削除は検出できない

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


def expand_comment_ids(text: str) -> set[str]:
    """F3 コメント形式の ID を抽出する（`NN〜MM` 範囲は展開）。"""
    ids = set()
    for m in F3_RE.finditer(text):
        prefix, start, suffix, end = m.group(1), int(m.group(2)), m.group(3), m.group(4)
        if end:
            for n in range(start, int(end) + 1):
                ids.add(canon(prefix, n, suffix))
        else:
            ids.add(canon(prefix, start, suffix))
    return ids


def extract_code_ids(path: Path) -> set[str]:
    """1 ファイルから F1〜F3 の ID を抽出する。"""
    text = path.read_text(encoding="utf-8")
    ids = set()
    for m in F1_RE.finditer(text):
        ids.add(canon(m.group(1), m.group(2), m.group(3)))
    for m in F2_RE.finditer(text):
        ids.add(canon(m.group(1), m.group(2), m.group(3)))
    ids |= expand_comment_ids(text)
    return ids


def scan_test_methods(path: Path) -> tuple[set[str], list[tuple[int, str]]]:
    """テストメソッドの直前ブロック（属性と // コメントの連続行＋シグネチャ行）に
    紐付く ID の集合と、ID マーカー（F1/F2/F3）のないテスト（行番号・メソッド名）を返す。"""
    lines = path.read_text(encoding="utf-8").splitlines()
    associated: set[str] = set()
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
        near_text = "\n".join(block) + "\n" + line
        marks = {
            canon(fm.group(1), fm.group(2), fm.group(3)) for fm in F1_RE.finditer(near_text)
        }
        marks |= expand_comment_ids(near_text)
        name_m = re.match(rf"^({PREFIX})(\d+)(b?)_", m.group(1))
        if name_m:
            marks.add(canon(name_m.group(1), name_m.group(2), name_m.group(3)))
        if not marks:
            missing.append((i + 1, m.group(1)))
        associated |= marks
    return associated, missing


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


def doc_sort_key(path: Path):
    """phaseN は番号順、それ以外（追補書など）は最後に並べる。"""
    m = re.search(r"phase(\d+)", path.name)
    if m:
        return (0, int(m.group(1)))
    return (1, path.name)


def extract_doc_ids(root: Path, skip_undecodable: bool = False):
    """登録行の ID（文書→集合）と、廃止指定のある ID の集合、
    文書順で最後のイベントが登録である「現行登録」の集合を返す。

    skip_undecodable=True のとき UTF-8 でデコードできない文書を飛ばす。
    既定の False はデコード失敗で UnicodeDecodeError を送出する
    （照合側は読めない帳簿を黙って無視しない）。"""
    registered: dict[str, set[str]] = {}
    abandoned: set[str] = set()
    last_event: dict[str, str] = {}
    for path in sorted(root.glob(DOCS_GLOB), key=doc_sort_key):
        rel = path.relative_to(root).as_posix()
        in_abandoned = False
        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except UnicodeDecodeError:
            if not skip_undecodable:
                raise
            continue
        for line in lines:
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
                for test_id in ids:
                    last_event[test_id] = "retire"
            else:
                for test_id in ids:
                    registered.setdefault(test_id, set()).add(rel)
                    last_event[test_id] = "register"
    active = {test_id for test_id, kind in last_event.items() if kind == "register"}
    return registered, abandoned, active


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
    associated: set[str] = set()
    markerless: list[tuple[str, int, str]] = []
    for path in sorted(TESTS_DIR.glob("**/*.cs")):
        rel = path.relative_to(ROOT).as_posix()
        for test_id in extract_code_ids(path):
            code_ids.setdefault(test_id, set()).add(rel)
        assoc, missing = scan_test_methods(path)
        associated |= assoc
        markerless.extend((rel, line, name) for line, name in missing)

    registered, abandoned, active = extract_doc_ids(ROOT)
    prefixes_docs_only, prefixes_rereg, ids_docs_only, ids_collision = load_exceptions()

    def docs_only_allowed(test_id: str) -> bool:
        return test_id in ids_docs_only or test_id.split("-", 1)[0] in prefixes_docs_only

    # E1: コードに実在するが現行の登録行がなく、例外でもない
    #     （廃止後に再登録がない ID は古い登録行が残っていても未登録扱い）
    e1 = [
        (test_id, sorted(code_ids[test_id]))
        for test_id in sorted(code_ids)
        if test_id not in active and not docs_only_allowed(test_id)
    ]
    # W1: 現行の登録行はあるがコード側に実在しない（例外指定は除く）
    w1 = [
        (test_id, sorted(registered[test_id]))
        for test_id in sorted(active)
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
    # I2: 実在の根拠がテストメソッドに紐付かないコメントのみの ID
    #     （クラス要約・範囲一覧など。個別テストが消えても ID が残るため検出できない）
    group_only = sorted(set(code_ids) - associated)

    for test_id, files in e1:
        note = "（廃止指定があるため復活には新しい登録行が必要です）" if test_id in abandoned else ""
        print(f"E1: {disp(test_id)} はコードに実在しますが帳簿の登録行がありません{note}（{', '.join(files)}）")
    for test_id, files in w1:
        print(f"警告 W1: {disp(test_id)} は帳簿に登録されていますがコード側に実在しません（{', '.join(files)}）")
    for test_id, files in w2:
        print(f"警告 W2: {disp(test_id)} は複数ファイルに実在します（{', '.join(files)}）")
    for rel, line, name in markerless:
        print(f"警告 W3: {rel}:{line} {name} に近接する ID マーカーがありません")
    for test_id, files in i1:
        print(f"情報 I1: {disp(test_id)} は複数の文書に登録されています（{', '.join(files)}）")
    for test_id in group_only:
        files = ", ".join(sorted(code_ids[test_id]))
        print(
            f"情報 I2: {disp(test_id)} の実在根拠はテストメソッドに紐付かないコメントのみです"
            f"（{files}）。個別テストの削除は検出できません"
        )

    info_count = len(i1) + len(group_only)
    print(
        f"照合結果: コード側 {len(code_ids)} 件・帳簿登録 {len(registered)} 件"
        f"（現行 {len(active)} 件）。"
        f"E1 {len(e1)} 件、警告 {len(w1) + len(w2) + len(markerless)} 件、情報 {info_count} 件"
    )
    if e1:
        print("未登録のテスト ID があります。追補書 §2 への登録か test-id-exceptions.txt の例外指定が必要です")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
