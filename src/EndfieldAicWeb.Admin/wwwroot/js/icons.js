// アイコン画像のブラウザ内正規化と zip バイナリのダウンロード。
// NuGet を増やさず Canvas と同梱の UPNG.js で済ませる（Phase 7/8 仕様）。
// 正規化の規格（中央正方形クロップ → 128px 以下は原寸中央配置・超過は縮小、
// アニメーションは全フレーム同一規則で APNG 保存）は
// docs/requirements.md の Icons 節と仕様決定 AA を参照。

const ICON_CANVAS_SIZE = 128;

// data: URI を MIME とバイト列に分解する。
function _dataUrlToBytes(dataUrl) {
    const comma = dataUrl.indexOf(",");
    const mime = dataUrl.slice(5, dataUrl.indexOf(";"));
    const binary = atob(dataUrl.slice(comma + 1));
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return { mime, bytes };
}

function _bytesToBase64(bytes) {
    let text = "";
    const chunk = 0x8000;
    for (let i = 0; i < bytes.length; i += chunk) {
        text += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
    }
    return btoa(text);
}

// PNG のチャンク列をたどって acTL（アニメーション制御）の有無だけを見る。
function _hasApngActlChunk(bytes) {
    if (bytes.length < 8 || bytes[0] !== 0x89) return false;
    let p = 8;
    while (p + 8 <= bytes.length) {
        const length = ((bytes[p] << 24) | (bytes[p + 1] << 16) | (bytes[p + 2] << 8) | bytes[p + 3]) >>> 0;
        const type = String.fromCharCode(bytes[p + 4], bytes[p + 5], bytes[p + 6], bytes[p + 7]);
        if (type === "acTL") return true;
        if (type === "IEND") return false;
        p += 12 + length;
    }
    return false;
}

// 正規化規則（仕様決定 AA）: ソースの中央正方形を取り、128px 超なら 128×128 へ縮小、
// 以下なら拡大せず 128×128 透過キャンバスの中央へ原寸配置する。
function _drawNormalized(ctx, source, sourceWidth, sourceHeight) {
    const size = Math.min(sourceWidth, sourceHeight);
    const sx = (sourceWidth - size) / 2;
    const sy = (sourceHeight - size) / 2;
    if (size > ICON_CANVAS_SIZE) {
        ctx.drawImage(source, sx, sy, size, size, 0, 0, ICON_CANVAS_SIZE, ICON_CANVAS_SIZE);
    } else {
        const offset = (ICON_CANVAS_SIZE - size) / 2;
        ctx.drawImage(source, sx, sy, size, size, offset, offset, size, size);
    }
}

// 1 フレーム分のソースを 128×128 へ正規化描画し、RGBA バイト列（ArrayBuffer）を採取する。
function _normalizedFrameRgba(source, width, height) {
    const canvas = document.createElement("canvas");
    canvas.width = ICON_CANVAS_SIZE;
    canvas.height = ICON_CANVAS_SIZE;
    const ctx = canvas.getContext("2d");
    _drawNormalized(ctx, source, width, height);
    return ctx.getImageData(0, 0, ICON_CANVAS_SIZE, ICON_CANVAS_SIZE).data.buffer;
}

// 正規化済み RGBA フレーム列と遅延（ミリ秒）から APNG の data: URI を作る。ループは無限。
function _encodeApngDataUrl(frames, delays) {
    const apng = UPNG.encode(frames, ICON_CANVAS_SIZE, ICON_CANVAS_SIZE, 0, delays);
    return "data:image/png;base64," + _bytesToBase64(new Uint8Array(apng));
}

// WebCodecs ImageDecoder でアニメーションフレーム列を取る主経路。
// アニメーションでない・非対応・失敗時は null を返し、呼び出し側のフォールバックに委ねる。
async function _decodeViaImageDecoder(bytes, mime) {
    if (typeof ImageDecoder === "undefined") return null;
    try {
        if (!(await ImageDecoder.isTypeSupported(mime))) return null;
    } catch {
        return null;
    }

    let decoder;
    try {
        decoder = new ImageDecoder({ data: bytes, type: mime });
        await decoder.tracks.ready;
    } catch {
        return null;
    }

    try {
        const track = decoder.tracks.selectedTrack;
        if (!track || !track.animated || track.frameCount <= 1) return null;

        // 差分フレームが返る実装に備え、フルサイズのオフスクリーンへ重ね描きしてから採取する。
        const composite = document.createElement("canvas");
        const compositeCtx = composite.getContext("2d");
        const frames = [];
        const delays = [];
        for (let i = 0; i < track.frameCount; i++) {
            const { image } = await decoder.decode({ frameIndex: i });
            const width = image.displayWidth;
            const height = image.displayHeight;
            if (composite.width !== width || composite.height !== height) {
                composite.width = width;
                composite.height = height;
            }
            const rect = image.visibleRect;
            if (rect) {
                compositeCtx.drawImage(image, rect.x, rect.y, rect.width, rect.height);
            } else {
                compositeCtx.drawImage(image, 0, 0);
            }
            delays.push(Math.round(image.duration ? image.duration / 1000 : 100));
            image.close();
            frames.push(_normalizedFrameRgba(composite, width, height));
        }
        return _encodeApngDataUrl(frames, delays);
    } catch {
        return null;
    } finally {
        decoder.close();
    }
}

// APNG 入力向けの副経路。UPNG.decode → UPNG.toRGBA8 が blend/dispose 処理済みの
// 全フレーム RGBA を返す。静止画・失敗時は null。
function _decodeViaUpng(buffer) {
    if (typeof UPNG === "undefined") return null;
    try {
        const image = UPNG.decode(buffer);
        if (!image.frames || image.frames.length <= 1) return null;

        const rgbaFrames = UPNG.toRGBA8(image);
        const delays = image.frames.map(f => (f.delay > 0 ? f.delay : 100));
        const composite = document.createElement("canvas");
        composite.width = image.width;
        composite.height = image.height;
        const compositeCtx = composite.getContext("2d");
        const frames = rgbaFrames.map(rgba => {
            compositeCtx.putImageData(
                new ImageData(new Uint8ClampedArray(rgba), image.width, image.height), 0, 0);
            return _normalizedFrameRgba(composite, image.width, image.height);
        });
        return _encodeApngDataUrl(frames, delays);
    } catch {
        return null;
    }
}

// 静止画経路。従来どおり Image 要素でデコードする（data: URI の fetch は環境によって失敗するため）。
function _normalizeStatic(dataUrl) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
            const canvas = document.createElement("canvas");
            canvas.width = ICON_CANVAS_SIZE;
            canvas.height = ICON_CANVAS_SIZE;
            _drawNormalized(canvas.getContext("2d"), img, img.naturalWidth, img.naturalHeight);
            resolve(canvas.toDataURL("image/png"));
        };
        img.onerror = () => reject(new Error("画像をデコードできませんでした"));
        img.src = dataUrl;
    });
}

// data: URI の画像を正規化して 128×128 PNG/APNG の data: URI で返す。
// アニメーション（GIF・APNG・ImageDecoder で扱えるアニメーション WebP）は
// 全フレームへ同一規則を適用した APNG を返し、返り値の契約は PNG と同じ data:image/png に統一する。
window.normalizeIconPng = async (dataUrl) => {
    const { mime, bytes } = _dataUrlToBytes(dataUrl);
    const maybeApng = mime === "image/apng" || (mime === "image/png" && _hasApngActlChunk(bytes));
    const maybeAnimated = maybeApng || mime === "image/gif" || mime === "image/webp";

    const viaDecoder = await _decodeViaImageDecoder(bytes, mime);
    if (viaDecoder !== null) return viaDecoder;

    if (maybeApng) {
        const viaUpng = _decodeViaUpng(bytes.buffer);
        if (viaUpng !== null) return viaUpng;
    }

    // いずれの経路も使えないアニメーション入力は、先頭フレーム相当の静止画として取り込む。
    if (maybeAnimated) {
        console.warn("アニメーションフレームをデコードできなかったため、先頭フレームの静止画として取り込みます。");
    }
    return _normalizeStatic(dataUrl);
};

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
