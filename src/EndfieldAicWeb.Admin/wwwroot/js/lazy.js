// エクスポート・アイコン系スクリプトの遅延ローダー（Phase 38）。
// 初期表示では読み込まず、正規化・ダウンロードの初回利用時に必要な分だけを読み込む。
// 呼び出し側は目的の関数群を await してから本来の関数を使う。
const _adminScriptCache = new Map();
function _loadAdminScript(src) {
    if (!_adminScriptCache.has(src)) {
        _adminScriptCache.set(src, new Promise((resolve, reject) => {
            const el = document.createElement("script");
            el.src = src;
            el.onload = resolve;
            el.onerror = () => {
                _adminScriptCache.delete(src);
                reject(new Error(`スクリプトの読み込みに失敗しました: ${src}`));
            };
            document.head.appendChild(el);
        }));
    }
    return _adminScriptCache.get(src);
}

// アイコン正規化用。UPNG.js は pako.min.js を、icons.js は UPNG.js を参照するためこの順で直列に読み込む。
window.loadIconTools = async () => {
    for (const src of ["js/pako.min.js", "js/UPNG.js", "js/icons.js"]) {
        await _loadAdminScript(src);
    }
};

// エクスポート物のダウンロード用。downloadBlobFile は icons.js、downloadTextFile は download.js が提供する。
window.loadDownloadTools = async () => {
    for (const src of ["js/download.js", "js/icons.js"]) {
        await _loadAdminScript(src);
    }
};
