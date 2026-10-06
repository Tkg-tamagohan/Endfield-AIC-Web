#!/usr/bin/env python3
"""tools/scan_ids.py の回帰テスト（stdlib unittest）。

実行: `python3 -m unittest tools.test_scan_ids`（リポジトリルートから）
または `python3 tools/test_scan_ids.py`。
"""
import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import scan_ids


def id_pairs(text):
    return sorted(scan_ids.iter_test_ids(text))


class TestIterTestIds(unittest.TestCase):
    def test_dash_form(self):
        self.assertEqual(id_pairs('DisplayName = "FG-63: 概要"'), [("FG", 63)])

    def test_nodash_method_name(self):
        pairs = id_pairs("public void IDF01_空の一覧なら連番の先頭を返す()")
        self.assertEqual(pairs, [("IDF", 1)])

    def test_derived_suffix_folds_to_base(self):
        self.assertEqual(id_pairs("OPT-03b"), [("OPT", 3)])

    def test_range_same_prefix(self):
        self.assertIn(("SEL", 25), id_pairs("SEL-01〜25"))

    def test_range_with_repeated_prefix(self):
        self.assertIn(("FG", 10), id_pairs("FG-08〜FG-10"))

    def test_slash_and_bullet_continuations(self):
        pairs = id_pairs("RCP-11/12")
        self.assertIn(("RCP", 11), pairs)
        self.assertIn(("RCP", 12), pairs)
        pairs = id_pairs("SEL-01〜05・07〜22")
        self.assertIn(("SEL", 22), pairs)

    def test_bullet_separated_prefixes_are_independent(self):
        # `FG-62・MN-181` の MN-181 を FG の継続として取り違えない
        pairs = id_pairs("現行最大は FG-62・MN-181")
        self.assertIn(("FG", 62), pairs)
        self.assertIn(("MN", 181), pairs)
        self.assertNotIn(("FG", 181), pairs)

    def test_non_id_tokens_excluded(self):
        text = "SHA256.HashData(bytes) NU1605 ABCDEF-12"
        self.assertEqual(id_pairs(text), [])

    def test_japanese_adjacency(self):
        # 日本語に隣接する出現も拾う（\b は日本語を語構成要素に含むため使わない）
        pairs = id_pairs("検査はMN-182で行う")
        self.assertIn(("MN", 182), pairs)

    def test_identifier_embedded_nodash_excluded(self):
        self.assertEqual(id_pairs("void Foo_IDF01_x()"), [])

    def test_partial_digits_not_split(self):
        # 4 桁の片側だけ読まない
        self.assertEqual(id_pairs("XX-1234"), [])


class TestLetterSequence(unittest.TestCase):
    def test_letter_index_order(self):
        self.assertLess(scan_ids.letter_index("Z"), scan_ids.letter_index("AA"))
        self.assertLess(scan_ids.letter_index("AZ"), scan_ids.letter_index("BA"))
        self.assertLess(scan_ids.letter_index("CS"), scan_ids.letter_index("CT"))

    def test_next_letters(self):
        self.assertEqual(scan_ids.next_letters("A"), "B")
        self.assertEqual(scan_ids.next_letters("Z"), "AA")
        self.assertEqual(scan_ids.next_letters("AZ"), "BA")
        self.assertEqual(scan_ids.next_letters("BZ"), "CA")
        self.assertEqual(scan_ids.next_letters("CS"), "CT")
        self.assertEqual(scan_ids.next_letters("ZZ"), "AAA")


class TestFormatId(unittest.TestCase):
    def test_zero_padding(self):
        self.assertEqual(scan_ids.format_id("FG", 68), "FG-68")
        self.assertEqual(scan_ids.format_id("MN", 184), "MN-184")
        self.assertEqual(scan_ids.format_id("IC", 4), "IC-04")


class TestExtractMaxClaims(unittest.TestCase):
    P40_MEMO = (
        "> ID 採番: xUnit は FG-63 以降（main の現行最大は FG-62）。"
        "手動確認は MN-182 以降（main の現行最大は MN-181）。push 前に main で再確認する。"
    )

    def test_memo_line(self):
        claims = scan_ids.extract_max_claims(self.P40_MEMO)
        self.assertEqual(
            [(kind, key, val) for _ln, kind, key, val in claims],
            [("test", "FG", 62), ("test", "MN", 181)],
        )

    def test_phase_attribution_is_not_a_claim(self):
        # `Phase 37 の MN-165` は Phase 37 の採番クレームではない
        claims = scan_ids.extract_max_claims("現行最大は Phase 37 の MN-165）")
        self.assertEqual(
            [(kind, key, val) for _ln, kind, key, val in claims],
            [("test", "MN", 165)],
        )

    def test_phase_claim(self):
        claims = scan_ids.extract_max_claims("Phase 番号は 40 とする（main の現行最大は Phase 39）")
        self.assertEqual(
            [(kind, key, val) for _ln, kind, key, val in claims],
            [("phase", None, 39)],
        )

    def test_bullet_separated_list(self):
        claims = scan_ids.extract_max_claims("main の現行最大は FG-62・MN-181。並行セッションに注意")
        self.assertEqual(
            [(kind, key, val) for _ln, kind, key, val in claims],
            [("test", "FG", 62), ("test", "MN", 181)],
        )

    def test_no_claim(self):
        self.assertEqual(scan_ids.extract_max_claims("現行最大を再確認すること"), [])


class TestMeasureOnFixtureTree(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / "tests" / "T.Tests").mkdir(parents=True)
        (self.root / "docs" / "phases").mkdir(parents=True)
        (self.root / "tests" / "T.Tests" / "Obj.cs").write_text(
            "// FG-01: a\n// FG-03: b\nvoid IDF02_x() {}\nSHA256.HashData(x);\n",
            encoding="utf-8",
        )
        (self.root / "docs" / "phases" / "test-specification-phase3.md").write_text(
            "| FG-02 | SEL-04〜06 |\n> 現行最大は FG-02\n", encoding="utf-8"
        )
        (self.root / "docs" / "phases" / "implementation-plan-phase3.md").write_text("", encoding="utf-8")
        (self.root / "docs" / "implementation-plan.md").write_text(
            "### Phase 2: a\n### Phase 3: b\n", encoding="utf-8"
        )
        (self.root / "docs" / "decision-records.md").write_text(
            "| # | 項目 | 決定 |\n|---|---|---|\n| A | x | y |\n| AA | x | y |\n| AB | x | y |\n",
            encoding="utf-8",
        )

    def tearDown(self):
        self.tmp.cleanup()

    def test_measure_test_ids(self):
        result = scan_ids.measure_test_ids(self.root)
        self.assertEqual(result["FG"]["max"], 3)
        self.assertEqual(result["SEL"]["max"], 6)
        self.assertEqual(result["IDF"]["max"], 2)
        self.assertNotIn("SHA", result)
        self.assertEqual(result["FG"]["file"], "tests/T.Tests/Obj.cs")

    def test_measure_phase(self):
        self.assertEqual(scan_ids.measure_phase(self.root), 3)

    def test_measure_decision(self):
        self.assertEqual(scan_ids.measure_decision(self.root), "AB")

    def test_obj_and_bin_excluded(self):
        (self.root / "tests" / "T.Tests" / "obj").mkdir()
        (self.root / "tests" / "T.Tests" / "obj" / "Generated.cs").write_text(
            "// FG-99: generated\n", encoding="utf-8"
        )
        result = scan_ids.measure_test_ids(self.root)
        self.assertEqual(result["FG"]["max"], 3)

    def test_claim_segments_not_measured(self):
        # 「現行最大は」の記録値は使用・登録ではないため実測に含めない
        (self.root / "docs" / "phases" / "test-specification-phase3.md").write_text(
            "| FG-02 |\n> 現行最大は FG-99\n", encoding="utf-8"
        )
        result = scan_ids.measure_test_ids(self.root)
        self.assertEqual(result["FG"]["max"], 3)


class TestParsePrDiff(unittest.TestCase):
    DIFF = """diff --git a/docs/phases/test-specification-phase41.md b/docs/phases/test-specification-phase41.md
new file mode 100644
+++ b/docs/phases/test-specification-phase41.md
@@ -0,0 +1,4 @@
+### Phase 41: タイトル
+| ID | 内容 | 期待 |
+| FG-70 | 内容 |
+| MN-200 | 手動 |
+| CT | 仕様決定 | 内容 |
+Phase 50 で再検討する
+（main の現行最大は FG-99）
+仕様決定は CS の次の採番で CU・CV
+仕様決定 CW・CX は CS の次の採番であり
+仕様決定 ID は CY〜DA で採番する
 unchanged context line
-removed | FG-99 | old |
diff --git a/docs/decision-records.md b/docs/decision-records.md
+++ b/docs/decision-records.md
@@ -10,0 +10,2 @@
+| CT | 内容 | 決定 |
"""

    def test_added_lines_only(self):
        usage = scan_ids.parse_pr_diff(self.DIFF)
        self.assertEqual(usage["test_ids"]["FG"], [70])
        self.assertEqual(usage["test_ids"]["MN"], [200])
        self.assertNotIn(99, usage["test_ids"].get("FG", []))

    def test_phase_reservation_sources(self):
        usage = scan_ids.parse_pr_diff(self.DIFF)
        # ファイル名 + 新規見出し行のみ。本文の「Phase 50 で再検討」は予約しない
        self.assertEqual(usage["phases"], [41])

    def test_decision_reservations(self):
        usage = scan_ids.parse_pr_diff(self.DIFF)
        # decision-records.md の表行（CT）と散文予約（CU・CV・CW・CX・CY〜DA）
        # テスト仕様書の表ヘッダ `| ID |` や表中の CT 行は予約にしない
        self.assertEqual(
            usage["decisions"],
            ["CT", "CU", "CV", "CW", "CX", "CY", "CZ", "DA"],
        )

    def test_table_header_id_not_decision(self):
        self.assertNotIn("ID", scan_ids.parse_pr_diff(self.DIFF)["decisions"])


class TestRunCheck(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / "docs").mkdir(parents=True)
        (self.root / "tests").mkdir()
        (self.root / "docs" / "implementation-plan.md").write_text("### Phase 40: x\n", encoding="utf-8")
        (self.root / "docs" / "decision-records.md").write_text("| CS | x | y |\n", encoding="utf-8")
        (self.root / "docs" / "spec.md").write_text(
            "> 現行最大は FG-62・MN-181\n", encoding="utf-8"
        )
        (self.root / "tests" / "T.cs").write_text("// FG-67: x // MN-183\n", encoding="utf-8")
        self.report = scan_ids.build_report(self.root)

    def tearDown(self):
        self.tmp.cleanup()

    def run_check(self, name):
        buf = io.StringIO()
        with redirect_stdout(buf):
            code = scan_ids.run_check(self.root, self.root / name, self.report)
        return code, buf.getvalue()

    def test_mismatch_reported(self):
        code, out = self.run_check("docs/spec.md")
        self.assertEqual(code, 1)
        self.assertIn("記録値 FG-62 / 実測 FG-67", out)
        self.assertIn("記録値 MN-181 / 実測 MN-183", out)

    def test_stale_high_claim_fails(self):
        # 実測を超える水増し記録値もズレとして検出する（自己正当化しない）
        (self.root / "docs" / "high.md").write_text("> 現行最大は FG-99\n", encoding="utf-8")
        code, out = self.run_check("docs/high.md")
        self.assertEqual(code, 1)
        self.assertIn("記録値 FG-99 / 実測 FG-67", out)

    def test_consistent(self):
        (self.root / "docs" / "ok.md").write_text("> 現行最大は FG-67\n", encoding="utf-8")
        code, out = self.run_check("docs/ok.md")
        self.assertEqual(code, 0)
        self.assertIn("一致", out)

    def test_missing_file(self):
        code = scan_ids.run_check(self.root, self.root / "none.md", self.report)
        self.assertEqual(code, 2)


if __name__ == "__main__":
    unittest.main()
