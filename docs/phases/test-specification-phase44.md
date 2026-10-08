# Phase 44 テスト仕様

**対象フェーズ**: Phase 44（Domain/Application の構造整理（分割系）: MasterValidator・FlowGraphModelBuilder.AssignRanks の partial 分割）
**前提ドキュメント**: [implementation-plan-phase44.md](implementation-plan-phase44.md)、[decision-records.md](../decision-records.md)（仕様決定 DD）
**関連ドキュメント**: [implementation-plan-phase43.md](implementation-plan-phase43.md)・[test-specification-phase43.md](test-specification-phase43.md)（移動系の先行 Phase）

> 本書は Phase 44 の検査項目を ID 付きで管理する。実施結果は PR 本文に表で記録する。
> ID 採番: 手動確認は Phase 43 の採番に続く番号（Phase 43 が MN-198〜MN-204 を使う前提で MN-205 以降。Phase 43 未実施で本 Phase を先に行う場合は MN-198 以降を使い本書を更新する）。xUnit の新規採番はなし（verbatim 分割のみで挙動は不変）。push 前に main で再確認する。

## 1. 機械検査（コマンド）

| ID | 対象 | 内容 | 期待 |
|---|---|---|---|
| MN-205 | MasterValidator 分割の verbatim 性 | (a) メンバーブロック比較: 後述のスクリプトで、元ファイル `main:src/EndfieldAicWeb.Domain/Validation/MasterValidator.cs` と分割後ファイル群 `src/EndfieldAicWeb.Domain/Validation/MasterValidator*.cs` のメンバーブロック集合を比較する。(b) 行多重集合比較: `diff <(git diff main...HEAD -- 'src/EndfieldAicWeb.Domain/Validation/MasterValidator*.cs' | grep '^-' | grep -v '^---' | cut -c2- | sort) <(git diff main...HEAD -- 'src/EndfieldAicWeb.Domain/Validation/MasterValidator*.cs' | grep '^+' | grep -v '^+++' | cut -c2- | sort)` | (a) メンバーブロックの集合が一致（各メンバー内の行順・内容が verbatim で、集合として過不足なし）。(b) 移動行は削除と追加で相殺され、差分として残るのは partial 化に伴う行のみ（各ファイルの `using`・`namespace` 宣言・`partial class` 宣言・閉じ括弧） |
| MN-206 | AssignRanks 分割の verbatim 性 | MN-205 と同じ 2 段手順を `src/EndfieldAicWeb.Application/Graph/FlowGraphModelBuilder.AssignRanks*.cs`（Phase 43 未マージ時は `src/EndfieldAicWeb.Application/FlowGraphModelBuilder.AssignRanks*.cs`）に対して行う | MN-205 と同じ許容基準 |
| MN-207 | 公開 API・修飾名の不変 | 実施前後で `rg '^\s*(public|internal)\s' src/EndfieldAicWeb.Domain/Validation/MasterValidator*.cs | sort` と `rg '^\s*(public|internal)\s' -g 'FlowGraphModelBuilder.AssignRanks*.cs' src/EndfieldAicWeb.Application | sort` をそれぞれ比較する | public・internal メンバーの宣言集合が分割前後で一致する。クラス・名前空間が不変（partial 化のみ） |
| MN-208 | メンバーの配置割り当て | `rg -l 'ValidateItem' src/EndfieldAicWeb.Domain/Validation/`・`rg -l 'OrderNodesWithinRanks|CountCrossings' src/EndfieldAicWeb.Application/` 等で、各メソッドの所在ファイルを確認する | 計画書 §3 の割り当て表どおりのファイルにメンバーが置かれている |
| MN-209 | 既存検証の回帰 | `dotnet build`・`dotnet test`・`~/.venvs/validate/bin/python tools/validate_master.py` を実行する | 全緑。変更は verbatim 分割のみのため、それ以外の失敗は変更混入を疑う |

MN-205・MN-206 (a) のメンバーブロック比較スクリプト（`OLD`・`NEW` を対象に合わせて差し替えて使う。クラス直下メンバーの開始行 `    public|private|internal|protected`（直上の `///` ドキュメント・属性行を含む）から、次のメンバー開始またはクラスの閉じ括弧 `}` までを 1 ブロックとしてソート比較する。式形式メンバーや署名が複数行にまたがるメンバーも扱える）:

```sh
python3 - <<'EOF'
import glob, re, subprocess
OLD = 'main:src/EndfieldAicWeb.Domain/Validation/MasterValidator.cs'
NEW = 'src/EndfieldAicWeb.Domain/Validation/MasterValidator*.cs'
START = re.compile(r'    (?:public|private|internal|protected) ')
def members(src):
    lines = src.splitlines(keepends=True)
    idx = [i for i, l in enumerate(lines) if START.match(l)]
    starts = []
    for i in idx:
        s = i
        while s > 0 and (lines[s-1].startswith('    ///') or lines[s-1].startswith('    [')):
            s -= 1
        starts.append(s)
    blocks = []
    for k, s in enumerate(starts):
        e1 = starts[k+1] if k+1 < len(starts) else len(lines)
        e2 = next((i for i in range(s+1, len(lines)) if lines[i].startswith('}')), len(lines))
        blocks.append(''.join(lines[s:min(e1, e2)]).rstrip('\n'))
    return sorted(blocks)
old = subprocess.run(['git', 'show', OLD], capture_output=True, text=True).stdout
new = ''.join(open(f).read() for f in sorted(glob.glob(NEW)))
a, b = members(old), members(new)
print(f'members: {len(a)} -> {len(b)}; identical={a == b}')
EOF
```

## 2. 目視確認

なし（verbatim 分割のみのため UI への影響はない）。

## 3. 受け入れ条件との対応

- MN-205・MN-206 が verbatim 分割の不変条件をカバーする
- MN-207 が公開 API・修飾名の不変を、MN-208 が割り当てどおりの配置をカバーする
- MN-209 が回帰をカバーする
