// アイコン画像のブラウザ内正規化（中央正方形クロップ → 128×128 PNG）と
// zip バイナリのダウンロード。NuGet を増やさず Canvas で済ませる（Phase 7 仕様）。

// data: URI の画像を中央正方形にクロップし 128×128 PNG の data: URI で返す。
// デコードは fetch ではなく Image 要素で行う（data: URI の fetch は環境によって失敗するため）。
window.normalizeIconPng = (dataUrl) =>
    new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
            const size = Math.min(img.naturalWidth, img.naturalHeight);
            const sx = (img.naturalWidth - size) / 2;
            const sy = (img.naturalHeight - size) / 2;
            const canvas = document.createElement("canvas");
            canvas.width = 128;
            canvas.height = 128;
            canvas.getContext("2d").drawImage(img, sx, sy, size, size, 0, 0, 128, 128);
            resolve(canvas.toDataURL("image/png"));
        };
        img.onerror = () => reject(new Error("画像をデコードできませんでした"));
        img.src = dataUrl;
    });

// base64 バイト列を Blob ダウンロードする（downloadTextFile のバイナリ版）。
window.downloadBlobFile = (fileName, base64, contentType) => {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
};
