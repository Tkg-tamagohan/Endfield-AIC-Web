// 編集パネルへスクロールする（一覧での選択・新規作成後にビューポート外へ置かれないようにする）。
window.scrollElementIntoView = (element) => {
    element?.scrollIntoView({ behavior: "smooth", block: "start" });
};
