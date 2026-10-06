#!/usr/bin/env python3
"""採番確認ツール: テスト ID・Phase 番号・仕様決定 ID の現行最大と次候補を報告する。

走査対象は tests/**/*.cs と docs/**/*.md のテスト ID（`接頭辞-連番` 形と
`接頭辞NN_` メソッド名形）、docs/implementation-plan.md の `### Phase N:` 見出しと
docs/phases/ のファイル名の Phase 番号、docs/decision-records.md 表第 1 列の仕様決定 ID。
読み取り専用で、リポジトリを変更しない。

- 既定: 接頭辞ごとの現行最大と次候補、Phase・仕様決定の次候補、採番メモ雛形を出力
- --check <file>: ファイル内の「現行最大は X-NN」記述を実測と照合し、ズレを報告
- --with-prs: gh でオープン PR の差分を走査し、使用済み ID を予約候補として列挙
  （gh 未導入・未認証時は警告して main のみの結果へ退避）
- --json: 機械向け出力

Python 標準ライブラリのみ。依存追加・blueprint 変更は不要。
"""
import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SKIP_DIR_NAMES = {"obj", "bin", ".git"}

# テスト ID の出現形:
# - `PREFIX-NN` 形（FG-63、MN-182、派生付番 OPT-03b は派生元番号に畳む）
# - `PREFIX-NN/MM`、`PREFIX-NN・MM`、`PREFIX-NN〜MM` の連記・範囲記述（MM は同じ接頭辞扱い）
# - `PREFIXNN_` メソッド名形（IDF01_…。SHA256.HashData 等を避けるため末尾の _ を必須とする）
# 数字の前後は ASCII 英数字・_ で区切り、日本語に隣接する出現も拾う。
ID_ANCHOR = re.compile(r"(?<![A-Za-z0-9_])([A-Z]{2,5})-(\d{1,3})(?!\d)[a-z]?")
ID_CONT = re.compile(r"[/・〜](\d{1,3})")
ID_NODASH = re.compile(r"(?<![A-Za-z0-9_])([A-Z]{2,5})(\d{2,3})(?!\d)(?=_)")

PHASE_HEADING = re.compile(r"^#{1,6}\s*Phase\s+(\d+)", re.MULTILINE)
PHASE_FILENAME = re.compile(r"phase(\d+)")
DECISION_CELL = re.compile(r"^\|\s*([A-Z]{1,3})\s*\|", re.MULTILINE)

# 「現行最大は …」の記録値。`Phase N の` は帰属表現なので採番クレームからは除く。
MAX_CLAIM_SEGMENT = re.compile(r"現行最大は?([^\n。）)]*)")
MAX_CLAIM_PHASE_ATTR = re.compile(r"Phase\s*\d+\s*の")
MAX_CLAIM_PHASE = re.compile(r"Phase\s*(\d+)")
MAX_CLAIM_ID = re.compile(r"([A-Z]{2,5})-(\d{1,3})")

# オープン PR 差分の走査用（+ 行のみ。+++ ヘッダは除く）
PR_PHASE_NAME = re.compile(r"phase(\d+)")
PR_PHASE_TEXT = re.compile(r"Phase\s+(\d+)")
PR_DECISION_ROW = re.compile(r"^\+\s*\|\s*([A-Z]{1,3})\s*\|", re.MULTILINE)


def iter_scan_files(root: Path):
    """走査対象ファイルを決定的な順序で列挙する（obj/bin など生成物を除く）。"""
    candidates = list(root.glob("tests/**/*.cs")) + list(root.glob("docs/**/*.md"))
    files = [
        p for p in candidates
        if p.is_file() and not (SKIP_DIR_NAMES & set(p.parts))
    ]
    return sorted(files)


def iter_test_ids(text: str):
    """テキスト中のテスト ID 出現を (接頭辞, 番号) で返す。

    `PREFIX-NN` の直後に接続する `/MM`・`・MM`・`〜MM` は同じ接頭辞の番号として畳み込む
    （SEL-01〜25・07〜22、RCP-11/12 などの記法）。`〜PREFIX-MM` のように接頭辞を
    繰り返す形はアンカーとして別途検出されるため二重計上は起きない。
    """
    for m in ID_ANCHOR.finditer(text):
        prefix = m.group(1)
        yield prefix, int(m.group(2))
        end = m.end()
        while True:
            cont = ID_CONT.match(text, end)
            if not cont:
                break
            yield prefix, int(cont.group(1))
            end = cont.end()
    for m in ID_NODASH.finditer(text):
        yield m.group(1), int(m.group(2))


def measure_test_ids(root: Path) -> dict:
    """接頭辞ごとの現行最大と、その最大が最初に出るファイルを返す。"""
    result: dict[str, dict] = {}
    for path in iter_scan_files(root):
        rel = path.relative_to(root).as_posix()
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        for prefix, num in iter_test_ids(text):
            entry = result.get(prefix)
            if entry is None:
                result[prefix] = {"max": num, "file": rel}
            elif num > entry["max"]:
                entry["max"] = num
                entry["file"] = rel
            elif num == entry["max"] and entry["file"] is None:
                entry["file"] = rel
    return result


def measure_phase(root: Path) -> int:
    """Phase 番号の現行最大（implementation-plan の見出しと phases/ ファイル名）。"""
    max_phase = 0
    plan = root / "docs" / "implementation-plan.md"
    if plan.is_file():
        for m in PHASE_HEADING.finditer(plan.read_text(encoding="utf-8")):
            max_phase = max(max_phase, int(m.group(1)))
    phases_dir = root / "docs" / "phases"
    if phases_dir.is_dir():
        for path in phases_dir.iterdir():
            m = PHASE_FILENAME.search(path.name)
            if m:
                max_phase = max(max_phase, int(m.group(1)))
    return max_phase


def letter_index(letters: str) -> int:
    """英字連番の順序値（A=1, Z=26, AA=27, AZ=52, BA=53）。"""
    n = 0
    for ch in letters:
        n = n * 26 + (ord(ch) - ord("A") + 1)
    return n


def next_letters(letters: str) -> str:
    """英字連番の次候補（Z→AA、AZ→BA、CS→CT）。"""
    chars = list(letters)
    i = len(chars) - 1
    while i >= 0 and chars[i] == "Z":
        chars[i] = "A"
        i -= 1
    if i < 0:
        return "A" + "".join(chars)
    chars[i] = chr(ord(chars[i]) + 1)
    return "".join(chars)


def measure_decision(root: Path):
    """仕様決定 ID の現行最大（decision-records.md 表第 1 列）。"""
    path = root / "docs" / "decision-records.md"
    if not path.is_file():
        return None
    letters = DECISION_CELL.findall(path.read_text(encoding="utf-8"))
    if not letters:
        return None
    return max(letters, key=letter_index)


def format_id(prefix: str, number: int) -> str:
    """出現形に合わせたゼロ埋めの ID 表記（2 桁最小、桁溢れは自然幅）。"""
    return f"{prefix}-{number:02d}"


def extract_max_claims(text: str):
    """「現行最大は …」記述から採番クレームを抽出する。

    戻り値は (行番号, 種別, キー, 記録値) のリスト。種別は "test"（キー=接頭辞）か
    "phase"（キー=None）。`Phase N の` は帰属表現として取り除いてから評価する。
    """
    claims = []
    for line_no, line in enumerate(text.splitlines(), start=1):
        for seg in MAX_CLAIM_SEGMENT.finditer(line):
            body = MAX_CLAIM_PHASE_ATTR.sub("", seg.group(1))
            for pm in MAX_CLAIM_PHASE.finditer(body):
                claims.append((line_no, "phase", None, int(pm.group(1))))
            for im in MAX_CLAIM_ID.finditer(body):
                claims.append((line_no, "test", im.group(1), int(im.group(2))))
    return claims


def parse_pr_diff(diff_text: str) -> dict:
    """PR 差分の追加行からテスト ID・Phase 番号・仕様決定 ID の使用分を拾う。"""
    added_lines = []
    phase_nums: set[int] = set()
    for line in diff_text.splitlines():
        if line.startswith("diff --git") or line.startswith("+++"):
            for m in PR_PHASE_NAME.finditer(line):
                phase_nums.add(int(m.group(1)))
            continue
        if line.startswith("+"):
            added_lines.append(line)
    added = "\n".join(added_lines)
    test_ids: dict[str, set[int]] = {}
    for prefix, num in iter_test_ids(added):
        test_ids.setdefault(prefix, set()).add(num)
    for m in PR_PHASE_TEXT.finditer(added):
        phase_nums.add(int(m.group(1)))
    decisions = set(PR_DECISION_ROW.findall(added))
    return {
        "test_ids": {p: sorted(ns) for p, ns in sorted(test_ids.items())},
        "phases": sorted(phase_nums),
        "decisions": sorted(decisions, key=letter_index),
    }


def scan_open_prs():
    """gh でオープン PR 差分を走査する。戻り値は (予約リスト, 警告 or None)。"""
    if shutil.which("gh") is None:
        return None, "gh が見つかりません。main のみの結果です"
    try:
        proc = subprocess.run(
            ["gh", "pr", "list", "--state", "open", "--json", "number,title", "--limit", "200"],
            capture_output=True, text=True, timeout=30,
        )
    except subprocess.TimeoutExpired:
        return None, "gh pr list がタイムアウトしました。main のみの結果です"
    if proc.returncode != 0:
        detail = (proc.stderr or proc.stdout or "").strip().splitlines()
        hint = f"（{detail[0][:120]}）" if detail else ""
        return None, f"gh pr list に失敗しました{hint}。gh 未認証の可能性があります。main のみの結果です"
    try:
        prs = json.loads(proc.stdout)
    except json.JSONDecodeError:
        return None, "gh pr list の出力を解釈できませんでした。main のみの結果です"

    reservations = []
    warnings = []
    for pr in prs:
        number = pr.get("number")
        try:
            diff = subprocess.run(
                ["gh", "pr", "diff", str(number)],
                capture_output=True, text=True, timeout=60,
            )
        except subprocess.TimeoutExpired:
            warnings.append(f"PR #{number} の差分取得がタイムアウトしました")
            continue
        if diff.returncode != 0:
            warnings.append(f"PR #{number} の差分取得に失敗しました")
            continue
        usage = parse_pr_diff(diff.stdout)
        if usage["test_ids"] or usage["phases"] or usage["decisions"]:
            reservations.append({
                "number": number,
                "title": pr.get("title") or "",
                **usage,
            })
    warning = "。".join(warnings) if warnings else None
    return reservations, warning


def merge_reservations(reservations):
    """オープン PR の使用分を接頭辞・Phase・仕様決定ごとの最大へ集約する。"""
    test: dict[str, dict] = {}
    phases: dict[int, list[int]] = {}
    decisions: dict[str, list[int]] = {}
    for r in reservations:
        for prefix, nums in r["test_ids"].items():
            entry = test.setdefault(prefix, {"max": 0, "prs": []})
            entry["max"] = max(entry["max"], max(nums))
            entry["prs"].append(r["number"])
        for n in r["phases"]:
            phases.setdefault(n, []).append(r["number"])
        for d in r["decisions"]:
            decisions.setdefault(d, []).append(r["number"])
    return {"test_ids": test, "phases": phases, "decisions": decisions}


def build_report(root: Path) -> dict:
    """main の実測結果を集約する。"""
    test_ids = measure_test_ids(root)
    phase_max = measure_phase(root)
    decision_max = measure_decision(root)
    decision_next = next_letters(decision_max) if decision_max else None
    memo = (
        f"> ID 採番: xUnit は {format_id('FG', test_ids.get('FG', {}).get('max', 0) + 1)} 以降"
        f"（main の現行最大は {format_id('FG', test_ids.get('FG', {}).get('max', 0))}）。"
        f"手動確認は {format_id('MN', test_ids.get('MN', {}).get('max', 0) + 1)} 以降"
        f"（main の現行最大は {format_id('MN', test_ids.get('MN', {}).get('max', 0))}）。"
        "push 前に main で再確認する。"
    )
    plan_memo = (
        f"> Phase 番号は {phase_max + 1} とする（main の現行最大は Phase {phase_max}）。"
        f"仕様決定は {decision_max} の次の採番で {decision_next}、"
        f"xUnit は {format_id('FG', test_ids.get('FG', {}).get('max', 0) + 1)} 以降、"
        f"手動確認は {format_id('MN', test_ids.get('MN', {}).get('max', 0) + 1)} 以降を使う"
        f"（main の現行最大は {format_id('FG', test_ids.get('FG', {}).get('max', 0))}・"
        f"{format_id('MN', test_ids.get('MN', {}).get('max', 0))}。"
        "並行セッションの採番衝突に注意して push 前に main を再確認する）。"
    )
    return {
        "test_ids": {
            p: {
                "max": e["max"],
                "max_formatted": format_id(p, e["max"]),
                "next": format_id(p, e["max"] + 1),
                "latest_file": e["file"],
            }
            for p, e in sorted(test_ids.items())
        },
        "phase": {"max": phase_max, "next": phase_max + 1},
        "decision": {"max": decision_max, "next": decision_next},
        "memo_test_spec": memo,
        "memo_impl_plan": plan_memo,
    }


def print_report(report: dict, reservations, pr_warning):
    ids = report["test_ids"]
    print(f"テスト ID（{len(ids)} 接頭辞）")
    width = max((len(p) for p in ids), default=6)
    for prefix, e in ids.items():
        print(f"  {prefix:<{width}}  現行最大 {e['max_formatted']:<8}  次候補 {e['next']:<8}  最新の出現: {e['latest_file']}")
    print()
    print(f"Phase 番号: 現行最大 {report['phase']['max']} → 次候補 {report['phase']['next']}")
    print(f"仕様決定 ID: 現行最大 {report['decision']['max']} → 次候補 {report['decision']['next']}")
    print()
    print("採番メモ雛形（テスト仕様書用）:")
    print(report["memo_test_spec"])
    print()
    print("採番メモ雛形（実装計画書用）:")
    print(report["memo_impl_plan"])
    if reservations is None and pr_warning:
        print()
        print(f"警告: {pr_warning}")
        return
    if reservations is not None:
        print()
        print("オープン PR の使用 ID:")
        if not reservations:
            print("  （オープン PR に採番対象の使用はありません）")
        for r in reservations:
            parts = []
            for prefix, nums in r["test_ids"].items():
                shown = nums if len(nums) <= 6 else nums[:5] + ["…", nums[-1]]
                parts.append(f"{prefix}-{'/'.join(str(n) for n in shown)}")
            if r["phases"]:
                parts.append("Phase " + "/".join(str(n) for n in r["phases"]))
            if r["decisions"]:
                parts.append("仕様決定 " + "/".join(r["decisions"]))
            print(f"  PR #{r['number']} {r['title']}: {', '.join(parts)}")
        merged = merge_reservations(reservations)
        if merged["test_ids"] or merged["phases"] or merged["decisions"]:
            print()
            print("予約を考慮した実効次候補:")
            for prefix, e in sorted(merged["test_ids"].items()):
                main_max = ids.get(prefix, {}).get("max", 0)
                effective = format_id(prefix, max(main_max, e["max"]) + 1)
                prs = "・".join(f"#{n}" for n in e["prs"])
                print(f"  {prefix}: main {format_id(prefix, main_max)} / PR {format_id(prefix, e['max'])}（{prs}）→ 実効次候補 {effective}")
            if merged["phases"]:
                n = max(merged["phases"])
                prs = "・".join(f"#{x}" for x in merged["phases"][n])
                print(f"  Phase: main {report['phase']['max']} / PR {n}（{prs}）→ 実効次候補 {max(report['phase']['max'], n) + 1}")
            if merged["decisions"]:
                d = max(merged["decisions"], key=letter_index)
                prs = "・".join(f"#{x}" for x in merged["decisions"][d])
                top = max(report["decision"]["max"], d, key=letter_index) if report["decision"]["max"] else d
                print(f"  仕様決定: main {report['decision']['max']} / PR {d}（{prs}）→ 実効次候補 {next_letters(top)}")
        if pr_warning:
            print()
            print(f"警告: {pr_warning}")


def run_check(root: Path, check_path: Path, report: dict) -> int:
    """--check: ファイル内の「現行最大」記述を実測と照合する。"""
    if not check_path.is_file():
        print(f"エラー: {check_path} が見つかりません", file=sys.stderr)
        return 2
    claims = extract_max_claims(check_path.read_text(encoding="utf-8"))
    if not claims:
        print(f"{check_path}: 「現行最大」の記述はありません")
        return 0
    mismatches = []
    unknown = []
    for line_no, kind, key, recorded in claims:
        if kind == "phase":
            measured = report["phase"]["max"]
            if recorded != measured:
                mismatches.append(f"  行 {line_no}: 記録値 Phase {recorded} / 実測 Phase {measured}")
        else:
            entry = report["test_ids"].get(key)
            if entry is None:
                unknown.append(f"  行 {line_no}: {key}-{recorded}（接頭辞 {key} は現在の走査で見つかりません）")
            elif recorded != entry["max"]:
                mismatches.append(
                    f"  行 {line_no}: 記録値 {format_id(key, recorded)} / 実測 {entry['max_formatted']}"
                )
    rel = check_path
    try:
        rel = check_path.relative_to(root)
    except ValueError:
        pass
    for line in unknown:
        print(line)
    if mismatches:
        print(f"{rel}: 記録値と実測のズレ（{len(mismatches)} 件 / 記述 {len(claims)} 件）")
        for line in mismatches:
            print(line)
        return 1
    suffix = f"、接頭辞不明 {len(unknown)} 件" if unknown else ""
    print(f"{rel}: 現行最大の記述 {len(claims)} 件はすべて実測と一致します{suffix}")
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(
        description="テスト ID・Phase 番号・仕様決定 ID の現行最大と次候補を報告する（読み取り専用）"
    )
    parser.add_argument("--check", metavar="FILE", help="ファイル内の「現行最大は X-NN」記述を実測と照合する")
    parser.add_argument("--with-prs", action="store_true", help="gh でオープン PR の使用 ID も列挙する（gh 未認証時は警告して退避）")
    parser.add_argument("--json", action="store_true", help="機械向け JSON 出力")
    args = parser.parse_args(argv)

    report = build_report(ROOT)
    reservations = None
    pr_warning = None
    if args.with_prs:
        reservations, pr_warning = scan_open_prs()

    if args.check:
        check_path = Path(args.check)
        if not check_path.is_absolute():
            check_path = ROOT / check_path
        return run_check(ROOT, check_path, report)

    if args.json:
        out = dict(report)
        if args.with_prs:
            out["open_prs"] = {
                "warning": pr_warning,
                "reservations": reservations if reservations is not None else [],
            }
        print(json.dumps(out, ensure_ascii=False, indent=2))
        return 0

    print_report(report, reservations if args.with_prs else None, pr_warning)
    return 0


if __name__ == "__main__":
    sys.exit(main())
