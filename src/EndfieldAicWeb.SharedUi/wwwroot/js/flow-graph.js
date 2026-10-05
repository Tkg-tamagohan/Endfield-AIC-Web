// 生産フローグラフの WebGPU 描画（仕様決定 AJ）。
// canvas はエッジ帯と流量比例の粒子のみを描き、ノードは Blazor 側の DOM カードが担う。
// このモジュールはモデルのランク・順序からノード座標を決めて DOM に反映し、
// パン・ズーム・フォールバック判定を管理する。
// 非対応環境では create が null を返すだけで例外は投げない（仕様決定 AK）。
// 描画中の実行時失敗（デバイスロスト・GPU エラー・描画例外）は fail が一度だけ .NET へ通知し、
// 初期化失敗と同じ経路でグラフ節を畳む（仕様決定 CJ）。

const NODE_GAP_X = 110;
const NODE_GAP_Y = 22;
const MARGIN = 28;
const ZOOM_MIN = 0.2; // ピンチ対応で緩和（仕様決定 BL）
const ZOOM_MAX = 1.5; // アイコンが原寸を超えて拡大されない範囲（仕様決定 AL）
const ZOOM_STEP = 1.25; // 画面上の＋・−ボタンが段階的に掛ける倍率（仕様決定 BL）
const EDGE_SEGMENTS = 20;
const EDGE_HALF_W = 2.0;
// 粒子の個数は絶対流量に比例させ、30 個/分で飽和させる（仕様決定 AP）。
// グラフ内最大流量との相対比だと高流量の計画で粒子が過密になり見づらいため。
const PARTICLE_COUNT_MIN = 3;
const PARTICLE_COUNT_MAX = 10;
const PARTICLE_SATURATE_PER_MINUTE = 30;

// u.time は f32 で書かれるため、量子化誤差が位相へ蓄積しないよう時刻基準をリセットする閾値（秒）。
const TIME_WRAP_S = 1024;

// canvas バッファのピクセル比上限（仕様決定 CK）と総ピクセル上限（仕様決定 CQ）。
// CQ は CK のさらに上に積む総量上限で、大画面・高 DPR の組合せでも充填コストとバッファメモリを有界にする。
const DPR_MAX = 2;
const MAX_BUFFER_PIXELS = 4194304;
// CSS 寸法から実効ピクセル比を求める。DPR・DPR_MAX に加えて総ピクセル上限を超えない比まで下げる。
function dprFor(cw, ch) {
    const base = Math.min(devicePixelRatio || 1, DPR_MAX);
    if (!(cw > 0) || !(ch > 0)) return base;
    return Math.min(base, Math.sqrt(MAX_BUFFER_PIXELS / (cw * ch)));
}
// CSS 寸法に対するバッファ寸法と実効ピクセル比を求める。丸め後の実寸法の積が上限を超える
// 場合は両辺を floor で決め直す（dpr ≤ sqrt(上限 / CSS 積) なので floor 後の積は必ず上限以下）。
function bufferSize(cw, ch) {
    const dpr = dprFor(cw, ch);
    let w = Math.max(1, Math.round(cw * dpr));
    let h = Math.max(1, Math.round(ch * dpr));
    if (w * h > MAX_BUFFER_PIXELS) {
        w = Math.max(1, Math.floor(cw * dpr));
        h = Math.max(1, Math.floor(ch * dpr));
    }
    return { w, h, dpr };
}
// リサイズ要求からバッファ再確保までの静止時間（ms）。連続したリサイズイベントのたびに
// GPU バッファを作り直さないよう、サイズが安定してから 1 回だけ確保する（Phase 38）。
const RESIZE_SETTLE_MS = 80;

// FlowGraphEdgeKind の enum 序数に対応する描画色（RGBA、app.css の変数と同色）。
const EDGE_COLORS = [
    [0.60, 0.65, 0.74, 0.45],  // RecipeInput: --muted 系
    [0.95, 0.64, 0.24, 0.85],  // RecipeOutput: --accent
    [0.66, 0.58, 0.80, 0.50],  // FixedConsumption
    [0.45, 0.70, 0.80, 0.50],  // EnvironmentConsume
];
const CLEAR_COLOR = { r: 0.063, g: 0.078, b: 0.102, a: 1.0 }; // --panel-deep

const WGSL = `
struct Uniforms {
    transform: vec4f,   // sx, sy, tx, ty（ワールドpx → デバイスpx）
    resolution: vec2f,  // canvas バッファのピクセルサイズ
    time: f32,
    staticDraw: f32,
};
@group(0) @binding(0) var<uniform> u: Uniforms;

struct EdgeParams {
    p0: vec2f, p1: vec2f, p2: vec2f, p3: vec2f,
    color: vec4f,
    speed: f32, size: f32, pad: vec2f,
};
@group(0) @binding(1) var<storage, read> edges: array<EdgeParams>;

fn bez(t: f32, a: vec2f, b: vec2f, c: vec2f, d: vec2f) -> vec2f {
    let s = 1.0 - t;
    return s*s*s*a + 3.0*s*s*t*b + 3.0*s*t*t*c + t*t*t*d;
}
fn bezD(t: f32, a: vec2f, b: vec2f, c: vec2f, d: vec2f) -> vec2f {
    let s = 1.0 - t;
    return 3.0*s*s*(b - a) + 6.0*s*t*(c - b) + 3.0*t*t*(d - c);
}
fn toNdc(world: vec2f) -> vec4f {
    let scr = world * u.transform.xy + u.transform.zw;
    return vec4f(scr.x / u.resolution.x * 2.0 - 1.0,
                 1.0 - scr.y / u.resolution.y * 2.0, 0.0, 1.0);
}

struct VOut { @builtin(position) pos: vec4f, @location(0) color: vec4f };

@vertex
fn vsEdge(@location(0) pos: vec2f, @location(1) color: vec4f) -> VOut {
    var o: VOut;
    o.pos = toNdc(pos);
    o.color = color;
    return o;
}

@vertex
fn vsParticle(@location(0) edgeIndex: u32,
              @location(1) phase: f32,
              @location(2) corner: vec2f) -> VOut {
    let e = edges[edgeIndex];
    let t = select(fract(u.time * e.speed + phase), phase, u.staticDraw > 0.5);
    let c = bez(t, e.p0, e.p1, e.p2, e.p3);
    var d = bezD(t, e.p0, e.p1, e.p2, e.p3);
    d = d / max(length(d), 1e-4);
    let nrm = vec2f(-d.y, d.x);
    let world = c + (d * corner.x + nrm * corner.y) * e.size;
    var o: VOut;
    o.pos = toNdc(world);
    o.color = e.color;
    return o;
}

@fragment
fn fsSolid(input: VOut) -> @location(0) vec4f { return input.color; }
`;

// 最大化中の Esc 復帰（仕様決定 BK）。dispose で解除する。
export function registerEscape(dotnetRef) {
    const handler = e => {
        if (e.key === 'Escape') dotnetRef.invokeMethodAsync('OnEscapeKey').catch(() => {});
    };
    document.addEventListener('keydown', handler);
    return { dispose() { document.removeEventListener('keydown', handler); } };
}

// 最大化中はページ本体のスクロールを抑止する（仕様決定 BK）。
let scrollLockPrev = null;
export function setScrollLock(on) {
    const el = document.documentElement;
    if (on) {
        if (scrollLockPrev === null) scrollLockPrev = el.style.overflow;
        el.style.overflow = 'hidden';
    } else if (scrollLockPrev !== null) {
        el.style.overflow = scrollLockPrev;
        scrollLockPrev = null;
    }
}

// リサイズハンドルのドラッグを指先・ペンでも継続させるためポインターを捕捉する（仕様決定 BK）。
export function capturePointer(el, pointerId) {
    el.setPointerCapture(pointerId);
}

// 高さドラッグ中の領域高追従を JS 側で行う（Phase 38）。
// pointermove ごとの Blazor 再レンダーを止めるため、ドラッグ中は wrap の style.height だけを更新し、
// 確定（pointerup/cancel）時の高さだけを dotnetRef へ通知する。モード遷移の判定は C# 側の確定処理が担う。
export function trackResizeDrag(wrapEl, pointerId, startY, startH, minH, dotnetRef) {
    let cur = startH;
    function onMove(e) {
        if (e.pointerId !== pointerId) return;
        cur = Math.max(minH, startH + (e.clientY - startY));
        wrapEl.style.height = `${Math.round(cur)}px`;
    }
    function onEnd(e) {
        if (e.pointerId !== pointerId) return;
        cleanup();
        dotnetRef?.invokeMethodAsync('OnResizeCommitted', cur).catch(() => { });
    }
    function cleanup() {
        window.removeEventListener('pointermove', onMove);
        window.removeEventListener('pointerup', onEnd);
        window.removeEventListener('pointercancel', onEnd);
    }
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onEnd);
    window.addEventListener('pointercancel', onEnd);
    return { dispose: cleanup };
}

// 領域高さの下限（既定値）。CSS のメディアクエリ（780px 以下で 300px）と同じ閾値を使う（仕様決定 BK）。
export function minGraphHeight() {
    return window.innerWidth <= 780 ? 300 : 420;
}

// 内容がズーム 1.0 で収まる領域高を Blazor 側へ通知する（仕様決定 CM）。
// 領域高の決定権は Blazor 側にあり、ここでは要求値を渡すだけとする。
// 下限は手動リサイズと同じ既定値（minGraphHeight）、上限はビューポート高さ。
// ビューポートが下限を下回る画面では下限を優先する。既存 CSS の min-height が
// インラインの高さ指定より常に優先されるため、上限優先にすると通知値と実効高さが食い違う。
function contentHeight(worldH) {
    const minH = minGraphHeight();
    return Math.ceil(Math.min(Math.max(worldH, minH), Math.max(minH, window.innerHeight)));
}

// ノード DOM クリックからリスト行へのスクロール＋強調。
// 最大化オーバレイの復帰処理と競合しないよう、描画フレーム後にスクロールする。
export function scrollToRef(refId) {
    const el = document.querySelector(`[data-flow-ref="${CSS.escape(refId)}"]`);
    if (!el) return;
    requestAnimationFrame(() => {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        el.classList.remove('flow-flash');
        void el.offsetWidth; // 再付与のためのアニメーションリセット
        el.classList.add('flow-flash');
        setTimeout(() => el.classList.remove('flow-flash'), 1400);
    });
}

// グラフ節・切替ボタンを出すかの事前判定。create より先に呼ぶ（仕様決定 AK）。
export async function webgpuSupported() {
    try {
        return !!navigator.gpu && !!(await navigator.gpu.requestAdapter());
    } catch {
        return false;
    }
}

export function viewportWidth() {
    return window.innerWidth;
}

export async function create(canvas, layer, dotnetRef) {
    try {
        if (!navigator.gpu) return null;
        const adapter = await navigator.gpu.requestAdapter();
        if (!adapter) return null;
        const device = await adapter.requestDevice();
        const context = canvas.getContext('webgpu');
        if (!context) return null;
        const format = navigator.gpu.getPreferredCanvasFormat();
        context.configure({ device, format, alphaMode: 'opaque' });
        return makeHandle(canvas, layer, device, context, format, dotnetRef);
    } catch {
        return null;
    }
}

function makeHandle(canvas, layer, device, context, format, dotnetRef) {
    const module = device.createShaderModule({ code: WGSL });
    const blend = {
        color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha' },
        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
    };
    const layout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.VERTEX, buffer: { type: 'uniform' } },
            { binding: 1, visibility: GPUShaderStage.VERTEX, buffer: { type: 'read-only-storage' } },
        ],
    });
    const pipeLayout = device.createPipelineLayout({ bindGroupLayouts: [layout] });
    const edgePipeline = device.createRenderPipeline({
        layout: pipeLayout,
        vertex: {
            module, entryPoint: 'vsEdge',
            buffers: [{
                arrayStride: 24,
                attributes: [
                    { shaderLocation: 0, offset: 0, format: 'float32x2' },
                    { shaderLocation: 1, offset: 8, format: 'float32x4' },
                ],
            }],
        },
        fragment: { module, entryPoint: 'fsSolid', targets: [{ format, blend }] },
        primitive: { topology: 'triangle-list' },
    });
    const particlePipeline = device.createRenderPipeline({
        layout: pipeLayout,
        vertex: {
            module, entryPoint: 'vsParticle',
            buffers: [
                { // インスタンス: edgeIndex, phase
                    arrayStride: 8, stepMode: 'instance',
                    attributes: [
                        { shaderLocation: 0, offset: 0, format: 'uint32' },
                        { shaderLocation: 1, offset: 4, format: 'float32' },
                    ],
                },
                { // 四角形の頂点
                    arrayStride: 8,
                    attributes: [{ shaderLocation: 2, offset: 0, format: 'float32x2' }],
                },
            ],
        },
        fragment: { module, entryPoint: 'fsSolid', targets: [{ format, blend }] },
        primitive: { topology: 'triangle-list' },
    });

    const uniformBuffer = device.createBuffer({ size: 32, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const quadBuffer = device.createBuffer({
        size: 6 * 8,
        usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(quadBuffer, 0, new Float32Array([
        -1, -1, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1,
    ]));

    let edgeVertexBuffer = null;
    let edgeVertexCount = 0;
    let particleInstanceBuffer = null;
    let particleCount = 0;
    let edgeParamBuffer = null;
    let bindGroup = null;
    let destroyed = false;
    let failed = false;
    // バッファ確保に使った実効ピクセル比（総量上限で下がりうる）。applyView の変換行列はこの値を使う。
    let bufferDpr = Math.min(devicePixelRatio || 1, DPR_MAX);
    // 直前に DOM へ書いた transform 文字列。不変なら書き込みを飛ばす（Phase 38）。
    let lastTransform = '';
    // バッファ再確保の保留。ResizeObserver・DPR 変化で要求サイズを記録し、
    // 安定してから frame() が applyPendingResize で確保する。
    let pendingW = -1;
    let pendingH = -1;
    let pendingAt = 0;
    // update() の呼出回数（E2E で差分発火を検証するカウンタ）。
    let updateCalls = 0;
    // 時刻リセットで位相を畳み込むため、インスタンスバッファの生データと各粒子のエッジ速度を保持する。
    let instanceData = null;
    let instanceSpeeds = null;

    const view = { s: 1, tx: 0, ty: 0 };
    let worldBounds = { x: 0, y: 0, w: 0, h: 0 };
    let nodeBounds = { x: 0, y: 0, w: 0, h: 0 }; // ノードのみの世界矩形（後退エッジの張り出しを含まない）
    let running = false;
    let visible = true;
    let inView = true;
    let rafId = 0;
    let startTime = performance.now();
    const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;

    function applyView() {
        // view（行列成分）が不変なら transform の DOM 書込みを飛ばす。
        // 静止時に毎フレーム走っていた style 無効化を止める（Phase 38）。
        const transform = `translate(${view.tx}px, ${view.ty}px) scale(${view.s})`;
        if (transform !== lastTransform) {
            lastTransform = transform;
            layer.style.transformOrigin = '0 0';
            layer.style.transform = transform;
        }
        const dpr = bufferDpr;
        let elapsed = (performance.now() - startTime) / 1000;
        // 動き抑制の静止画モードでは u.time を使わず位相をそのまま描くため、畳み込みは行わない。
        // 行うと次回の単発描画で粒子が現在位置から動いて見えてしまう。
        if (!reducedMotion && elapsed > TIME_WRAP_S && instanceData) {
            // u.time が大きくなると f32 の量子化誤差が粒子位相へ出始めるため、経過時間を
            // 位相へ畳み込んで時刻基準をリセットする。fract は整数シフト不変なので表示は連続する（Phase 34）。
            for (let i = 0; i < particleCount; i++) {
                instanceData[i * 2 + 1] = (elapsed * instanceSpeeds[i] + instanceData[i * 2 + 1]) % 1;
            }
            if (particleInstanceBuffer) device.queue.writeBuffer(particleInstanceBuffer, 0, instanceData);
            startTime = performance.now();
            elapsed = (performance.now() - startTime) / 1000;
        }
        const uniforms = new Float32Array([
            view.s * dpr, view.s * dpr, view.tx * dpr, view.ty * dpr,
            canvas.width, canvas.height,
            elapsed,
            reducedMotion ? 1 : 0,
        ]);
        device.queue.writeBuffer(uniformBuffer, 0, uniforms);
    }

    function rebuildBindGroup() {
        bindGroup = device.createBindGroup({
            layout,
            entries: [
                { binding: 0, resource: { buffer: uniformBuffer } },
                { binding: 1, resource: { buffer: edgeParamBuffer } },
            ],
        });
    }
    // ストレージバッファが空だとバインドできないため、常時 1 要素ぶん確保する。
    edgeParamBuffer = device.createBuffer({ size: 64, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
    rebuildBindGroup();

    function frame() {
        if (destroyed) return;
        rafId = 0;
        if (!(running && visible && inView)) return;
        applyPendingResize(performance.now());
        // getCurrentTexture や submit の実行時例外はデバイスロストの兆候であり、
        // rAF を再登録せずに fail へ渡してリスト表示へ退避する（仕様決定 CJ）。
        try {
            applyView();
            const encoder = device.createCommandEncoder();
            const pass = encoder.beginRenderPass({
                colorAttachments: [{
                    view: context.getCurrentTexture().createView(),
                    loadOp: 'clear', clearValue: CLEAR_COLOR, storeOp: 'store',
                }],
            });
            pass.setBindGroup(0, bindGroup);
            if (edgeVertexCount > 0 && edgeVertexBuffer) {
                pass.setPipeline(edgePipeline);
                pass.setVertexBuffer(0, edgeVertexBuffer);
                pass.draw(edgeVertexCount);
            }
            if (particleCount > 0 && particleInstanceBuffer) {
                pass.setPipeline(particlePipeline);
                pass.setVertexBuffer(0, particleInstanceBuffer);
                pass.setVertexBuffer(1, quadBuffer);
                pass.draw(6, particleCount);
            }
            pass.end();
            device.queue.submit([encoder.finish()]);
        } catch (err) {
            fail(err);
            return;
        }
        // 動き抑制時は静止画のため、状態変化ごとの単発描画にする。
        // ただし再確保の保留がある間は静止判定のためフレームを継続する。
        if (!reducedMotion || pendingW >= 0) rafId = requestAnimationFrame(frame);
    }

    function requestFrames() {
        if (!rafId) rafId = requestAnimationFrame(frame);
    }

    // 要求サイズを即時確保する。初期化と fitView（サイズ確定が前提の計算）で使う。
    function resizeNow() {
        const size = bufferSize(canvas.clientWidth || 1, canvas.clientHeight || 1);
        if (canvas.width !== size.w || canvas.height !== size.h) {
            canvas.width = size.w;
            canvas.height = size.h;
        }
        bufferDpr = size.dpr;
        pendingW = -1;
        pendingH = -1;
    }

    // ResizeObserver・DPR 変化からの再確保要求。要求サイズと変化時刻だけを保持し、
    // 実際のバッファ再確保は frame() 側で静止を確認してから行う。
    // force=true は CSS 寸法が不変でも再確保が要る経路（DPR 変化）で使う。
    function requestResize(force = false) {
        const cw = canvas.clientWidth || 0;
        const ch = canvas.clientHeight || 0;
        if (!force && pendingW === cw && pendingH === ch) return;
        pendingW = cw;
        pendingH = ch;
        pendingAt = performance.now();
    }

    // frame() から呼ぶ。要求サイズが RESIZE_SETTLE_MS 変わっていないときだけ再確保する。
    // フレームより遅い間隔で届く連続リサイズで繰り返し再確保しないよう、静止判定は時間で行う。
    function applyPendingResize(now) {
        if (pendingW < 0 || now - pendingAt < RESIZE_SETTLE_MS) return;
        const size = bufferSize(pendingW || 1, pendingH || 1);
        if (canvas.width !== size.w || canvas.height !== size.h) {
            canvas.width = size.w;
            canvas.height = size.h;
        }
        bufferDpr = size.dpr;
        pendingW = -1;
        pendingH = -1;
    }

    // DPR 変化（CSS 寸法が不変でもモニター間移動等で実効比が変わる）を検知する。
    // MediaQueryList の change は一度きりのため、発火ごとに現在値で再登録する。
    let dprQuery = null;
    function onDprChange() {
        watchDpr();
        requestResize(true);
        requestFrames();
    }
    function watchDpr() {
        dprQuery?.removeEventListener('change', onDprChange);
        dprQuery = matchMedia(`(resolution: ${devicePixelRatio || 1}dppx)`);
        dprQuery.addEventListener('change', onDprChange);
    }

    function fitView() {
        resizeNow();
        const cw = canvas.clientWidth || 1;
        const ch = canvas.clientHeight || 1;
        if (worldBounds.w <= 0 || worldBounds.h <= 0) {
            view.s = 1; view.tx = 0; view.ty = 0;
            return;
        }
        // 後退エッジの張り出し込みの境界が ZOOM_MIN で収まらない場合、
        // ループの端が欠けるよりノードが画面外へ出るほうが悪いため、ノード境界へ切り替える。
        const b = (Math.min(cw / worldBounds.w, ch / worldBounds.h) >= ZOOM_MIN)
            ? worldBounds
            : nodeBounds;
        view.s = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.min(cw / b.w, ch / b.h)));
        view.s = Math.min(view.s, 1);
        view.tx = (cw - b.w * view.s) / 2 - b.x * view.s;
        view.ty = (ch - b.h * view.s) / 2 - b.y * view.s;
    }

    // ---- パン・ピンチ・ズーム ----
    // アクティブポインターを Map で追跡し、1 点はパン・2 点はピンチとする（仕様決定 BL）。
    const pointers = new Map();
    let moved = false;
    let lastX = 0, lastY = 0;
    let pinchStartDist = 0, pinchStartScale = 1, pinchMidX = 0, pinchMidY = 0;
    const container = canvas.parentElement;

    function onPointerDown(e) {
        if (e.button !== 0) return;
        // リサイズハンドルやズームボタン等のオーバレイ UI 上の押下はパン・ピンチへ登録しない。
        // Blazor 委任の stopPropagation は祖先のネイティブリスナーより後に評価されるためここで除外する。
        if (e.target.closest('.flow-resize, .flow-zoom')) return;
        pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (pointers.size === 1) {
            moved = false;
            lastX = e.clientX; lastY = e.clientY;
        } else if (pointers.size === 2) {
            // 2 点目が触れた時点でパンからピンチへ移行し、初期の距離と中点を記録する。
            const [a, b] = [...pointers.values()];
            pinchStartDist = Math.hypot(b.x - a.x, b.y - a.y) || 1;
            pinchStartScale = view.s;
            const rect = canvas.getBoundingClientRect();
            pinchMidX = (a.x + b.x) / 2 - rect.left;
            pinchMidY = (a.y + b.y) / 2 - rect.top;
            moved = true; // ピンチ後の click は行ジャンプとして扱わない
        }
    }
    function onPointerMove(e) {
        const p = pointers.get(e.pointerId);
        if (!p) return;
        p.x = e.clientX; p.y = e.clientY;
        if (pointers.size >= 2) {
            // 中点をアンカーに距離比で拡大率を変え、中点の移動量をそのままパンへ加算する。
            const [a, b] = [...pointers.values()];
            const rect = canvas.getBoundingClientRect();
            const midX = (a.x + b.x) / 2 - rect.left;
            const midY = (a.y + b.y) / 2 - rect.top;
            const dist = Math.hypot(b.x - a.x, b.y - a.y) || 1;
            const next = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, pinchStartScale * dist / pinchStartDist));
            const k = next / view.s;
            view.tx = midX - (pinchMidX - view.tx) * k;
            view.ty = midY - (pinchMidY - view.ty) * k;
            view.s = next;
            pinchMidX = midX; pinchMidY = midY;
            requestFrames();
            return;
        }
        const dx = e.clientX - lastX;
        const dy = e.clientY - lastY;
        if (!moved && Math.hypot(dx, dy) < 4) return;
        moved = true;
        lastX = e.clientX; lastY = e.clientY;
        view.tx += dx; view.ty += dy;
        requestFrames();
    }
    function onPointerUp(e) {
        pointers.delete(e.pointerId);
        if (pointers.size === 1) {
            // 残った 1 点でパンへ戻る。
            const [r] = [...pointers.values()];
            lastX = r.x; lastY = r.y;
        }
    }
    function onClickCapture(e) {
        // ドラッグ移動・ピンチした直後の click はノードの行ジャンプとして扱わない。
        if (moved) {
            e.stopPropagation();
            moved = false;
        }
    }
    // 領域中心をアンカーに拡大率を next へ変える（ホイールと同じアンカー方式）。
    function zoomTo(next) {
        next = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, next));
        if (next === view.s) return;
        const rect = canvas.getBoundingClientRect();
        const mx = rect.width / 2;
        const my = rect.height / 2;
        view.tx = mx - (mx - view.tx) * (next / view.s);
        view.ty = my - (my - view.ty) * (next / view.s);
        view.s = next;
        requestFrames();
    }
    function onWheel(e) {
        e.preventDefault();
        const rect = canvas.getBoundingClientRect();
        const mx = e.clientX - rect.left;
        const my = e.clientY - rect.top;
        const next = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, view.s * Math.exp(-e.deltaY * 0.0012)));
        view.tx = mx - (mx - view.tx) * (next / view.s);
        view.ty = my - (my - view.ty) * (next / view.s);
        view.s = next;
        requestFrames();
    }

    container.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
    window.addEventListener('pointercancel', onPointerUp);
    container.addEventListener('click', onClickCapture, true);
    container.addEventListener('wheel', onWheel, { passive: false });

    // 領域の実寸変化（自動高さの適用・ビューポート高やモバイル閾値の変化）でも
    // 直近の世界矩形高から要求領域高を再通知する（仕様決定 CM）。
    const resizeObserver = new ResizeObserver(() => { requestResize(); notifyContentHeight(); requestFrames(); });
    // ビューポート高だけの変化では canvas の寸法が変わらず ResizeObserver が発火しないため、
    // 上限=ビューポート高へ追従させる window 側の resize でも再通知する（仕様決定 CM）。
    const onWindowResize = () => notifyContentHeight();
    window.addEventListener('resize', onWindowResize);
    resizeObserver.observe(canvas);
    const inViewObserver = new IntersectionObserver(entries => {
        inView = entries[0]?.isIntersecting !== false;
        if (inView) requestFrames();
    });
    inViewObserver.observe(canvas);
    const onVisibility = () => {
        visible = document.visibilityState === 'visible';
        if (visible) requestFrames();
    };
    document.addEventListener('visibilitychange', onVisibility);
    watchDpr();

    // ---- 実行時失敗の検知（仕様決定 CJ）----
    // dispose 内の device.destroy() が発火する reason 'destroyed' は destroyed フラグで除外する。
    const onUncapturedError = e => { if (!destroyed) fail(e.error); };
    if (typeof device.addEventListener === 'function') {
        device.addEventListener('uncapturederror', onUncapturedError);
    }
    device.lost.then(info => {
        if (!destroyed) fail(new Error(`WebGPU device lost (${info.reason}): ${info.message}`));
    });

    // ---- レイアウトとジオメトリ ----
    function cubic(p0, p1, p2, p3, t) {
        const s = 1 - t;
        return [
            s * s * s * p0[0] + 3 * s * s * t * p1[0] + 3 * s * t * t * p2[0] + t * t * t * p3[0],
            s * s * s * p0[1] + 3 * s * s * t * p1[1] + 3 * s * t * t * p2[1] + t * t * t * p3[1],
        ];
    }

    // 三次ベジェの実際の到達範囲（制御点ではなく曲線の極値）。導関数の根を両端と合わせて評価する。
    function cubicBounds(p0, p1, p2, p3) {
        const roots = (v0, v1, v2, v3) => {
            const a = v3 - 3 * v2 + 3 * v1 - v0;
            const b = 2 * (v2 - 2 * v1 + v0);
            const c = v1 - v0;
            const ts = [0, 1];
            if (Math.abs(a) < 1e-9) {
                if (Math.abs(b) > 1e-9) ts.push(-c / b);
            } else {
                const disc = b * b - 4 * a * c;
                if (disc >= 0) {
                    const r = Math.sqrt(disc);
                    ts.push((-b + r) / (2 * a), (-b - r) / (2 * a));
                }
            }
            return ts.filter(t => t > 0 && t < 1 || t === 0 || t === 1);
        };
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const t of roots(p0[0], p1[0], p2[0], p3[0])) {
            const x = cubic(p0, p1, p2, p3, t)[0];
            minX = Math.min(minX, x); maxX = Math.max(maxX, x);
        }
        for (const t of roots(p0[1], p1[1], p2[1], p3[1])) {
            const y = cubic(p0, p1, p2, p3, t)[1];
            minY = Math.min(minY, y); maxY = Math.max(maxY, y);
        }
        return { minX, minY, maxX, maxY };
    }

    function edgeGeometry(e, rects, vertical) {
        const a = rects.get(e.fromId);
        const b = rects.get(e.toId);
        if (!a || !b) return null;
        if (vertical) {
            // 縦表示（仕様決定 BJ）: 出口はソース上辺中央、入口はターゲット下辺中央、
            // 制御点は垂直方向へ張り出す。循環の後退エッジも同じ規則で側面に膨らむループになる。
            const p0 = [a.x + a.w / 2, a.y];
            const p3 = [b.x + b.w / 2, b.y + b.h];
            const d = Math.max(36, Math.abs(p3[1] - p0[1]) * 0.5);
            let c1x = p0[0], c2x = p3[0];
            if (p3[1] > p0[1]) {
                // 後退エッジ（仕様決定 CO）: 張り出し量は「間のノード矩形をすべて避ける」反復探索ではなく、
                // 縦スパン内ノード矩形の側面最外縁＋余白へ曲線の水平極値が到達する量を決定論的に求める。
                // 回避可能な量が存在しない配置では従来の探索が発散して巨大なループを生成していた。
                // 間のノードカードとの交差はノード背面への通過として許容する。
                const loY = p0[1], hiY = p3[1];
                const mids = [];
                for (const r of rects.values()) {
                    if (r === a || r === b) continue;
                    if (r.y + r.h > loY + 4 && r.y < hiY - 4) mids.push(r);
                }
                const PAD = 10;
                const MIN_OFF = Math.max(a.w, b.w) / 2 + 32;
                // 両制御点を同量ずらしたとき曲線の側面への最大到達は制御点距離の約 0.75 倍に留まる。
                // 近似のため cubicBounds の実極値で不足分を補正する（外縁への到達まで、上限 8 回。
                // 端点の水平距離が大きい配置では補正しきれない残差を残したまま確定しうる）。
                const K = 0.75;
                let outerL = Infinity, outerR = -Infinity;
                for (const r of mids) {
                    outerL = Math.min(outerL, r.x);
                    outerR = Math.max(outerR, r.x + r.w);
                }
                const edgeL = mids.length ? outerL - PAD : -Infinity;
                const edgeR = mids.length ? outerR + PAD : Infinity;
                const offFor = (side) => {
                    if (!mids.length) return MIN_OFF;
                    const edge = side > 0 ? edgeR : edgeL;
                    const base = side > 0 ? Math.max(p0[0], p3[0]) : Math.min(p0[0], p3[0]);
                    const gap = side > 0 ? edge - base : base - edge;
                    let off = Math.max(MIN_OFF, gap / K);
                    for (let iter = 0; iter < 8; iter++) {
                        const c1 = [p0[0] + side * off, p0[1] - d];
                        const c2 = [p3[0] + side * off, p3[1] + d];
                        const gb = cubicBounds(p0, c1, c2, p3);
                        const shortfall = side > 0 ? edge - gb.maxX : gb.minX - edge;
                        if (shortfall <= 0) break;
                        off += shortfall / K;
                    }
                    return off;
                };
                const offL = offFor(-1);
                const offR = offFor(1);
                // 必要張り出し量の小さい側を選び、同量ならソース上辺点に近い外縁側にする。
                let side;
                if (offR < offL - 1) side = 1;
                else if (offL < offR - 1) side = -1;
                else side = !mids.length || Math.abs(p0[0] - edgeL) > Math.abs(edgeR - p0[0]) ? 1 : -1;
                const off = side === 1 ? offR : offL;
                c1x += side * off;
                c2x += side * off;
            }
            const p1 = [c1x, p0[1] - d];
            const p2 = [c2x, p3[1] + d];
            return { p0, p1, p2, p3 };
        }
        const p0 = [a.x + a.w, a.y + a.h / 2];
        const p3 = [b.x, b.y + b.h / 2];
        const d = Math.max(36, Math.abs(p3[0] - p0[0]) * 0.5);
        const p1 = [p0[0] + d, p0[1]];
        const p2 = [p3[0] - d, p3[1]];
        return { p0, p1, p2, p3 };
    }

    // レイアウトに影響する構造（配置と実測寸法）の署名。署名が同じ限り fitView をせず
    // ユーザーのパン・ズームを維持する。流量など寸法に影響しない値は含めない。
    let lastTopology = '';
    function topologyKey(m, rects, vertical) {
        return (vertical ? 'v|' : 'h|') + (m?.nodes ?? []).map(n => {
            const r = rects.get(n.id);
            return `${n.id}:${n.rank}:${n.order}:${r ? r.w : 0}x${r ? r.h : 0}`;
        }).join('|');
    }

    // 直近の世界矩形高から要求領域高を算出し Blazor 側へ通知する（仕様決定 CM）。
    // 世界矩形はノード群と縦表示の後退エッジ側面ループ張り出しを含む（フィット対象と同じ境界）。
    function notifyContentHeight() {
        dotnetRef?.invokeMethodAsync('OnContentHeight', contentHeight(worldBounds.h)).catch(() => { });
    }

    function update(model, vertical) {
        if (destroyed) return;
        updateCalls++;
        const nodes = model?.nodes ?? [];
        const edgeList = model?.edges ?? [];
        const maxRate = Math.max(model?.maxRatePerMinute ?? 0, 1e-9);

        // DOM ノードの実サイズを測ってランク×順序の座標に配置する。
        const rects = new Map();
        const els = new Map();
        for (const el of layer.querySelectorAll('.fnode')) {
            els.set(el.dataset.nodeId, el);
        }
        const byRank = new Map();
        for (const n of nodes) {
            if (!byRank.has(n.rank)) byRank.set(n.rank, []);
            byRank.get(n.rank).push(n);
        }
        if (vertical) {
            // 縦表示（仕様決定 BJ）: rank→行・order→行内の横位置の転置。
            // 行の高さは含まれるノードの最大高、行は最大行幅に対して水平中央寄せ、
            // ノードは行内で上辺揃え。rank 0（採取側の最深）を最下行、最大 rank を最上行とする。
            let maxW = 0;
            const rankMeta = new Map();
            for (const [rank, list] of byRank) {
                list.sort((a, b) => a.order - b.order);
                let w = MARGIN;
                let h = 0;
                for (const n of list) {
                    const el = els.get(n.id);
                    const nw = el ? el.offsetWidth : 140;
                    const nh = el ? el.offsetHeight : 56;
                    rects.set(n.id, { x: 0, y: 0, w: nw, h: nh });
                    w += nw + NODE_GAP_X;
                    h = Math.max(h, nh);
                }
                const rowW = w - NODE_GAP_X + MARGIN;
                rankMeta.set(rank, { w: rowW, h });
                maxW = Math.max(maxW, rowW);
            }
            let y = MARGIN;
            let maxY = 0;
            for (const rank of [...rankMeta.keys()].sort((a, b) => b - a)) {
                const meta = rankMeta.get(rank);
                const offsetX = Math.max(0, (maxW - meta.w) / 2);
                let x = MARGIN + offsetX;
                for (const n of byRank.get(rank)) {
                    const r = rects.get(n.id);
                    r.x = x;
                    r.y = y;
                    const el = els.get(n.id);
                    if (el) el.style.transform = `translate(${r.x}px, ${r.y}px)`;
                    x += r.w + NODE_GAP_X;
                }
                y += meta.h + NODE_GAP_Y;
                maxY = y - NODE_GAP_Y + MARGIN;
            }
            worldBounds = { x: 0, y: 0, w: maxW, h: maxY };
        } else {
            let maxH = 0;
            const rankMeta = new Map();
            for (const [rank, list] of byRank) {
                list.sort((a, b) => a.order - b.order);
                let h = MARGIN;
                let w = 0;
                for (const n of list) {
                    const el = els.get(n.id);
                    const nw = el ? el.offsetWidth : 140;
                    const nh = el ? el.offsetHeight : 56;
                    rects.set(n.id, { x: 0, y: h, w: nw, h: nh });
                    h += nh + NODE_GAP_Y;
                    w = Math.max(w, nw);
                }
                rankMeta.set(rank, { h: h - NODE_GAP_Y + MARGIN, w });
                maxH = Math.max(maxH, h - NODE_GAP_Y + MARGIN);
            }
            let x = MARGIN;
            let maxX = 0;
            for (const rank of [...rankMeta.keys()].sort((a, b) => a - b)) {
                const meta = rankMeta.get(rank);
                const offsetY = Math.max(0, (maxH - meta.h) / 2);
                for (const n of byRank.get(rank)) {
                    const r = rects.get(n.id);
                    r.x = x;
                    r.y += offsetY;
                    const el = els.get(n.id);
                    if (el) el.style.transform = `translate(${r.x}px, ${r.y}px)`;
                }
                x += meta.w + NODE_GAP_X;
                maxX = x - NODE_GAP_X + MARGIN;
            }
            worldBounds = { x: 0, y: 0, w: maxX, h: maxH };
        }

        // エッジ幾何を先に確定し、後退エッジのループがノードの世界矩形より外へ出る場合は
        // フィット範囲へ含める（はみ出たループが領域端で欠けないため）。
        const geoms = [];
        let edgeMinX = Infinity, edgeMinY = Infinity, edgeMaxX = -Infinity, edgeMaxY = -Infinity;
        for (const e of edgeList) {
            const g = edgeGeometry(e, rects, vertical);
            if (!g) continue;
            geoms.push([e, g]);
            // 側面ループ化する縦の後退エッジのみ範囲へ含める。前進エッジの制御点の
            // 微小なオーバーシュートは従来どおりマージン側へ描画する。
            // 範囲は制御点ボックスではなく曲線の真の極値から取る（制御点は曲線より
            // 遠くへ張り出すため、箱で取るとフィットが必要以上に小さくなる）。
            if (!(vertical && g.p3[1] > g.p0[1])) continue;
            const gb = cubicBounds(g.p0, g.p1, g.p2, g.p3);
            edgeMinX = Math.min(edgeMinX, gb.minX);
            edgeMinY = Math.min(edgeMinY, gb.minY);
            edgeMaxX = Math.max(edgeMaxX, gb.maxX);
            edgeMaxY = Math.max(edgeMaxY, gb.maxY);
        }
        nodeBounds = worldBounds;
        if (edgeMinX !== Infinity) {
            const bx = Math.min(worldBounds.x, edgeMinX);
            const by = Math.min(worldBounds.y, edgeMinY);
            worldBounds = {
                x: bx,
                y: by,
                w: Math.max(worldBounds.x + worldBounds.w, edgeMaxX) - bx,
                h: Math.max(worldBounds.y + worldBounds.h, edgeMaxY) - by,
            };
        }
        // 後退エッジの張り出しはモデルのエッジ構成由来でノード署名に現れないため、
        // フィット境界へ含めた分だけ署名へ追加して再フィット漏れを防ぐ。
        const topoKey = topologyKey(model, rects, vertical) +
            (edgeMinX === Infinity ? '' : `|e${Math.round(edgeMinX)}_${Math.round(edgeMaxX)}_${Math.round(edgeMinY)}_${Math.round(edgeMaxY)}`);
        if (topoKey !== lastTopology) {
            lastTopology = topoKey;
            fitView();
        }

        // エッジ帯（三角形展開）と粒子インスタンスのバッファを作り直す。
        const verts = [];
        const params = [];
        const instances = [];
        for (const [e, g] of geoms) {
            const color = EDGE_COLORS[e.kind] ?? EDGE_COLORS[0];
            const edgeIndex = params.length;
            params.push({
                ...g,
                color: brighten(color),
                speed: 0.10 + 0.22 * (e.ratePerMinute / maxRate),
                size: 2.6,
            });
            let prev = cubic(g.p0, g.p1, g.p2, g.p3, 0);
            for (let i = 1; i <= EDGE_SEGMENTS; i++) {
                const cur = cubic(g.p0, g.p1, g.p2, g.p3, i / EDGE_SEGMENTS);
                let dx = cur[0] - prev[0];
                let dy = cur[1] - prev[1];
                const len = Math.hypot(dx, dy) || 1;
                dx /= len; dy /= len;
                const nx = -dy * EDGE_HALF_W, ny = dx * EDGE_HALF_W;
                verts.push(
                    prev[0] + nx, prev[1] + ny, ...color,
                    prev[0] - nx, prev[1] - ny, ...color,
                    cur[0] + nx, cur[1] + ny, ...color,
                    prev[0] - nx, prev[1] - ny, ...color,
                    cur[0] - nx, cur[1] - ny, ...color,
                    cur[0] + nx, cur[1] + ny, ...color,
                );
                prev = cur;
            }
            const norm = Math.min(e.ratePerMinute / PARTICLE_SATURATE_PER_MINUTE, 1);
            const count = Math.round(PARTICLE_COUNT_MIN + (PARTICLE_COUNT_MAX - PARTICLE_COUNT_MIN) * norm);
            for (let i = 0; i < count; i++) {
                instances.push(edgeIndex, i / count);
            }
        }

        if (edgeVertexBuffer) edgeVertexBuffer.destroy();
        edgeVertexCount = verts.length / 6;
        edgeVertexBuffer = verts.length
            ? device.createBuffer({ size: verts.length * 4, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST })
            : null;
        if (edgeVertexBuffer) device.queue.writeBuffer(edgeVertexBuffer, 0, new Float32Array(verts));

        if (edgeParamBuffer) edgeParamBuffer.destroy();
        const paramFloats = [];
        for (const p of params) {
            paramFloats.push(
                p.p0[0], p.p0[1], p.p1[0], p.p1[1], p.p2[0], p.p2[1], p.p3[0], p.p3[1],
                ...p.color, p.speed, p.size, 0, 0);
        }
        edgeParamBuffer = device.createBuffer({
            size: Math.max(64, paramFloats.length * 4),
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
        });
        if (paramFloats.length) device.queue.writeBuffer(edgeParamBuffer, 0, new Float32Array(paramFloats));
        rebuildBindGroup();

        if (particleInstanceBuffer) particleInstanceBuffer.destroy();
        particleCount = instances.length / 2;
        instanceData = new Float32Array(instances);
        const instanceU32 = new Uint32Array(instanceData.buffer);
        for (let i = 0; i < particleCount; i++) instanceU32[i * 2] = instances[i * 2];
        // 時刻リセットの位相畳み込みで使う各粒子のエッジ速度を保持する。
        instanceSpeeds = new Float32Array(particleCount);
        for (let i = 0; i < particleCount; i++) instanceSpeeds[i] = params[instances[i * 2]].speed;
        particleInstanceBuffer = particleCount
            ? device.createBuffer({ size: instanceData.byteLength, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST })
            : null;
        if (particleInstanceBuffer) device.queue.writeBuffer(particleInstanceBuffer, 0, instanceData);

        requestFrames();
        notifyContentHeight();
    }

    function brighten(c) {
        return [Math.min(1, c[0] + 0.18), Math.min(1, c[1] + 0.18), Math.min(1, c[2] + 0.18), Math.min(1, c[3] + 0.25)];
    }

    // ---- 後片付けと失敗通知（仕様決定 CJ）----
    // fail と dispose で共有する冪等の後片付け。投げうる操作はそれぞれ独立した
    // try/catch で囲み、一つの失敗が後続の解放をスキップしないようにする。
    function tryQuietly(fn) {
        try { fn(); } catch { }
    }

    function teardown() {
        tryQuietly(() => resizeObserver.disconnect());
        tryQuietly(() => inViewObserver.disconnect());
        tryQuietly(() => document.removeEventListener('visibilitychange', onVisibility));
        tryQuietly(() => container.removeEventListener('pointerdown', onPointerDown));
        tryQuietly(() => window.removeEventListener('pointermove', onPointerMove));
        tryQuietly(() => window.removeEventListener('pointerup', onPointerUp));
        tryQuietly(() => window.removeEventListener('pointercancel', onPointerUp));
        tryQuietly(() => container.removeEventListener('click', onClickCapture, true));
        tryQuietly(() => container.removeEventListener('wheel', onWheel));
        tryQuietly(() => window.removeEventListener('resize', onWindowResize));
        tryQuietly(() => dprQuery?.removeEventListener('change', onDprChange));
        tryQuietly(() => device.removeEventListener('uncapturederror', onUncapturedError));
        tryQuietly(() => edgeVertexBuffer?.destroy());
        tryQuietly(() => particleInstanceBuffer?.destroy());
        tryQuietly(() => edgeParamBuffer?.destroy());
        tryQuietly(() => quadBuffer.destroy());
        tryQuietly(() => uniformBuffer.destroy());
        tryQuietly(() => device.destroy());
        debugHandles.delete(canvas);
    }

    // 描画の継続不能を検知したとき、後片付けをして一度だけ .NET へ通知する。
    // ページ上の警告は出さず console.warn に理由を残す（仕様決定 CF の対象外）。
    function fail(err) {
        if (destroyed) return;
        destroyed = true;
        running = false;
        if (rafId) {
            cancelAnimationFrame(rafId);
            rafId = 0;
        }
        teardown();
        if (!failed) {
            failed = true;
            dotnetRef?.invokeMethodAsync('OnGraphFailed').catch(() => { });
        }
        console.warn('flow-graph: render failed, folding to list view', err);
    }

    running = true;
    resizeNow();
    requestFrames();

    // E2E 検証用の canvas → ハンドル登録（Phase 34）。teardown で除去する。
    debugHandles.set(canvas, {
        fail: () => fail(new Error('debugFail: injected failure')),
        updateCount: () => updateCalls,
        advance: s => {
            if (destroyed) return;
            startTime -= s * 1000;
            requestFrames();
        },
    });

    return {
        update,
        refit() { if (destroyed) return; fitView(); requestFrames(); },
        // ＋・−ボタン: 領域中心アンカーの段階的な拡大縮小（仕様決定 BL）。
        zoomStep(dir) { if (destroyed) return; zoomTo(view.s * (dir > 0 ? ZOOM_STEP : 1 / ZOOM_STEP)); },
        dispose() {
            if (destroyed) return;
            destroyed = true;
            running = false;
            if (rafId) cancelAnimationFrame(rafId);
            teardown();
        },
    };
}

// E2E 検証用レジストリ（Phase 34）。canvas → 失敗・時刻操作の内部入口。
const debugHandles = new WeakMap();

// DevTools・E2E から失敗経路を発火する。未登録・破棄済みなら何もしない。
export function debugFail(canvas) {
    debugHandles.get(canvas)?.fail();
}

// 時刻基準を s 秒だけ過去へずらし、次フレームで時刻リセットを通す。未登録・破棄済みなら何もしない。
export function debugAdvance(canvas, seconds) {
    debugHandles.get(canvas)?.advance(seconds);
}

// update() の呼出回数（E2E の差分発火検証用・Phase 38）。未登録・破棄済みなら -1。
export function debugUpdateCount(canvas) {
    return debugHandles.get(canvas)?.updateCount() ?? -1;
}

// 現在の canvas バッファの総ピクセル数（E2E の CQ 上限検証用）。
export function debugBufferPixels(canvas) {
    return canvas ? canvas.width * canvas.height : -1;
}
