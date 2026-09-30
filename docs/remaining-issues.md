# 残課題

この文書は、FCIS（Functional Core, Imperative Shell）観点のレビューで見つかった指摘のうち、今回の改善対象としなかった項目を記録するものである。

レビュー指摘のうち優先度の高い 5 件は次の PR で対応・マージ済みである。

- スナップショットの不変化: <https://github.com/Tkg-tamagohan/Endfield-AIC-Web/pull/12>
- 計算入力・既定ビュー規則の Application 集約: <https://github.com/Tkg-tamagohan/Endfield-AIC-Web/pull/11>
- アイコンフォールバック一本化・Export 引数化・純粋ロジック抽出: <https://github.com/Tkg-tamagohan/Endfield-AIC-Web/pull/13>

以下は対応を先送りした項目である。
いずれも動作上の障害ではなく、構造の整合性や保守性に関わるものとして列挙する。

## エンティティ可変性の根本対策

ドメインモデル（`MasterDocument` と配下のエンティティ）は `set` アクセサと `List<T>` を持つ可変 POCO であり、Admin の編集画面は共有オブジェクトを直接書き換える設計になっている。
スナップショット側の不変化（PR #12）により計算経路への混入は防がれているが、編集対象自体の可変性は残っている。
これを不変型へ移行する場合、編集画面の「直接代入して NotifyChanged」という操作モデル全体が更新を受けるため、影響範囲は編集系ページすべてに及ぶ。
移行する場合は、エンティティ差し替え型（変更のたびに新インスタンスを作り直す）か、編集用の別モデルを持つかの設計判断が必要である。

（PR <https://github.com/Tkg-tamagohan/Endfield-AIC-Web/pull/22> で対応。現状維持＋境界の明文化とし、可変なのは管理ツールの編集対象のみ・計算はスナップショット経由、を requirements §6.2 に明記）

## Razor ページ内ロジックのテスト空白

既定ビュー規則や入力パースは Application へ移したため純粋層のテストでカバーできるようになった。
一方、razor の `@code` に残る UI 状態遷移（チェック切り替え、行の追加削除、イベントハンドラ）は依然としてテスト対象外である。
カバーする場合は bUnit などのコンポーネントテスト基盤の導入が要になるが、現状 UI 層の変更頻度が低いため、導入コストとの兼ね合いで先送りとした。

## 細かな構造上の指摘

以下はいずれも小規模な整形で、次に該当箇所を触る機会にまとめて直せるものとして扱う。

- `ProductionCalculator` は状態を持たないのにインスタンスクラスであり、呼び出し側が毎回 `new` している。`static` クラスにしてよい。
- `PairSelector.Select` は警告を `ICollection<CalculationWarning>` の出力引数に集め、`ListCandidates` は捨てるためだけに `new List<>()` を渡している。戻り値に警告を含める形が素直である。
- `ContextFilter` は `init` プロパティのみの読み取り専用コンテナなので、`record` にすると意図が明確になる。
- `AdminDocumentService.ResolveIconPath` は呼び出しごとに `IconResolver` を組み立ててマニフェスト辞書を再構築している。App 側 `IconCatalog` は読み込み時に一度だけ構築しており、設計が揃っていない。アイコン数規模では性能差は小さい。

## 時刻の解釈規約

`GameEvent.ActiveFrom`/`ActiveTo` は `DateTime?` で、ページ側は `DateOnly.FromDateTime(DateTime.Now)` のローカル時刻と比較している。
正本 JSON の日時を UTC と読むかローカル時刻と読むかの規約は明文化されていないため、境界での解釈ずれを防ぐには規約の決定が必要である。
また、`DateTime.Now` の読み取りはページのフィールド初期化時に一度だけ行われるため、日付をまたいで開き続けた場合に既定イベントが更新されない軽微な問題もある。

## アイコン規格の分散

アイコンに関する規格は三箇所に分散している。
キー規則とハッシュ照合は C#（`IconKeyRules`、`IconManifestVerifier`）と CI 側 Python（`tools/validate_master.py` の再実装）に二重化され、画像正規化（中央正方形・128×128 PNG）の規格は `wwwroot/js/icons.js` の `normalizeIconPng` のみが保持している。
アイコン規格を変更する場合は三箇所の同期が必要であり、現状はコメントによる相互参照に留まる。

## スキルへの追記案

改善作業中のテストエージェントから、リポジトリ内スキル `.devin/skills/testing-blazor-apps` への追記案が複数出ている。
操作系の知見（コンボボックス操作の注意・座標ずれ回避）とアイコン・エクスポート検証の手順はスキルへ取り込み済みである。
ゴールデンパスの期待値表は未取り込みのため、必要であれば別途追記する。

（期待値表も PR <https://github.com/Tkg-tamagohan/Endfield-AIC-Web/pull/22> で取り込み済み）
