// 生産フローグラフの WebGPU 描画（仕様決定 AJ）。
// canvas はエッジ帯と流量比例の粒子のみを描き、ノードは Blazor 側の DOM カードが担う。
// このモジュールはモデルのランク・順序からノード座標を決めて DOM に反映し、
// パン・ズーム・フォールバック判定を管理する。
// 非対応環境では create が null を返すだけで例外は投げない（仕様決定 AK）。

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

// FlowGraphEdgeKind の enum 序数に対応する描画色（RGBA、app.css の変数と同色）。
const EDGE_COLORS = [
    [0.60, 0.65, 0.74, 0.45],  // RecipeInput: --muted 系
    [0.95, 0.64, 0.24, 0.85],  // RecipeOutput: --accent
    [0.66, 0.58, 0.80, 0.50],  // FixedConsumption
    [0.45, 0.70, 0.80, 0.50],  // EnvironmentConsume
    [0.42, 0.72, 0.50, 0.70],  // Gathered
];
const OVER_COLOR = [0.90, 0.28, 0.30, 0.95]; // --danger
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

// 領域高さの下限（既定値）。CSS のメディアクエリ（780px 以下で 300px）と同じ閾値を使う（仕様決定 BK）。
export function minGraphHeight() {
    return window.innerWidth <= 780 ? 300 : 420;
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

export async function create(canvas, layer) {
    try {
        if (!navigator.gpu) return null;
        const adapter = await navigator.gpu.requestAdapter();
        if (!adapter) return null;
        const device = await adapter.requestDevice();
        const context = canvas.getContext('webgpu');
        if (!context) return null;
        const format = navigator.gpu.getPreferredCanvasFormat();
        context.configure({ device, format, alphaMode: 'opaque' });
        return makeHandle(canvas, layer, device, context, format);
    } catch {
        return null;
    }
}

function makeHandle(canvas, layer, device, context, format) {
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

    const view = { s: 1, tx: 0, ty: 0 };
    let worldBounds = { x: 0, y: 0, w: 0, h: 0 };
    let running = false;
    let visible = true;
    let inView = true;
    let rafId = 0;
    let startTime = performance.now();
    const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;

    function applyView() {
        layer.style.transformOrigin = '0 0';
        layer.style.transform = `translate(${view.tx}px, ${view.ty}px) scale(${view.s})`;
        const dpr = devicePixelRatio || 1;
        const uniforms = new Float32Array([
            view.s * dpr, view.s * dpr, view.tx * dpr, view.ty * dpr,
            canvas.width, canvas.height,
            (performance.now() - startTime) / 1000,
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
        // 動き抑制時は静止画のため、状態変化ごとの単発描画にする。
        if (!reducedMotion) rafId = requestAnimationFrame(frame);
    }

    function requestFrames() {
        if (!rafId) rafId = requestAnimationFrame(frame);
    }

    function resize() {
        const dpr = devicePixelRatio || 1;
        const w = Math.max(1, Math.round(canvas.clientWidth * dpr));
        const h = Math.max(1, Math.round(canvas.clientHeight * dpr));
        if (canvas.width === w && canvas.height === h) return;
        canvas.width = w;
        canvas.height = h;
    }

    function fitView() {
        resize();
        const cw = canvas.clientWidth || 1;
        const ch = canvas.clientHeight || 1;
        if (worldBounds.w <= 0 || worldBounds.h <= 0) {
            view.s = 1; view.tx = 0; view.ty = 0;
            return;
        }
        view.s = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.min(cw / worldBounds.w, ch / worldBounds.h)));
        view.s = Math.min(view.s, 1);
        view.tx = (cw - worldBounds.w * view.s) / 2 - worldBounds.x * view.s;
        view.ty = (ch - worldBounds.h * view.s) / 2 - worldBounds.y * view.s;
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

    const resizeObserver = new ResizeObserver(() => { resize(); requestFrames(); });
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

    // ---- レイアウトとジオメトリ ----
    function cubic(p0, p1, p2, p3, t) {
        const s = 1 - t;
        return [
            s * s * s * p0[0] + 3 * s * s * t * p1[0] + 3 * s * t * t * p2[0] + t * t * t * p3[0],
            s * s * s * p0[1] + 3 * s * s * t * p1[1] + 3 * s * t * t * p2[1] + t * t * t * p3[1],
        ];
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
                // 後退エッジ: 同じ列のノードどうしではループが潰れ、間の行のノードカードをまたぐ。
                // 曲線の側面への最大到達は制御点距離の約 0.75 倍に留まるため、実際のベジェ曲線を
                // サンプリングして「間にあるノード矩形をすべて避ける」最小の張り出し量を決める。
                const loY = p0[1], hiY = p3[1];
                const mids = [];
                for (const r of rects.values()) {
                    if (r === a || r === b) continue;
                    if (r.y + r.h > loY + 4 && r.y < hiY - 4) mids.push(r);
                }
                const PAD = 10;
                const offFor = (side) => {
                    let off = Math.max(a.w, b.w) / 2 + 32;
                    for (let iter = 0; iter < 10; iter++) {
                        const c1 = [p0[0] + side * off, p0[1] - d];
                        const c2 = [p3[0] + side * off, p3[1] + d];
                        let clear = true;
                        for (let i = 0; i <= 48 && clear; i++) {
                            const pt = cubic(p0, c1, c2, p3, i / 48);
                            for (const r of mids) {
                                if (pt[1] > r.y - PAD && pt[1] < r.y + r.h + PAD &&
                                    pt[0] > r.x - PAD && pt[0] < r.x + r.w + PAD) {
                                    clear = false;
                                    break;
                                }
                            }
                        }
                        if (clear) return off;
                        off = off * 1.6 + 24;
                    }
                    return off;
                };
                const offL = offFor(-1);
                const offR = offFor(1);
                const side = offR <= offL ? 1 : -1;
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

    function update(model, vertical) {
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
            if (!(vertical && g.p3[1] > g.p0[1])) continue;
            edgeMinX = Math.min(edgeMinX, g.p0[0], g.p1[0], g.p2[0], g.p3[0]);
            edgeMinY = Math.min(edgeMinY, g.p0[1], g.p1[1], g.p2[1], g.p3[1]);
            edgeMaxX = Math.max(edgeMaxX, g.p0[0], g.p1[0], g.p2[0], g.p3[0]);
            edgeMaxY = Math.max(edgeMaxY, g.p0[1], g.p1[1], g.p2[1], g.p3[1]);
        }
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
            const color = e.overCapacity ? OVER_COLOR : EDGE_COLORS[e.kind] ?? EDGE_COLORS[0];
            const edgeIndex = params.length;
            params.push({
                ...g,
                color: e.overCapacity ? OVER_COLOR : brighten(color),
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
        const instanceData = new Float32Array(instances);
        const instanceU32 = new Uint32Array(instanceData.buffer);
        for (let i = 0; i < particleCount; i++) instanceU32[i * 2] = instances[i * 2];
        particleInstanceBuffer = particleCount
            ? device.createBuffer({ size: instanceData.byteLength, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST })
            : null;
        if (particleInstanceBuffer) device.queue.writeBuffer(particleInstanceBuffer, 0, instanceData);

        requestFrames();
    }

    function brighten(c) {
        return [Math.min(1, c[0] + 0.18), Math.min(1, c[1] + 0.18), Math.min(1, c[2] + 0.18), Math.min(1, c[3] + 0.25)];
    }

    running = true;
    resize();
    requestFrames();

    return {
        update,
        refit() { fitView(); requestFrames(); },
        // ＋・−ボタン: 領域中心アンカーの段階的な拡大縮小（仕様決定 BL）。
        zoomStep(dir) { zoomTo(view.s * (dir > 0 ? ZOOM_STEP : 1 / ZOOM_STEP)); },
        dispose() {
            destroyed = true;
            running = false;
            if (rafId) cancelAnimationFrame(rafId);
            resizeObserver.disconnect();
            inViewObserver.disconnect();
            document.removeEventListener('visibilitychange', onVisibility);
            container.removeEventListener('pointerdown', onPointerDown);
            window.removeEventListener('pointermove', onPointerMove);
            window.removeEventListener('pointerup', onPointerUp);
            window.removeEventListener('pointercancel', onPointerUp);
            container.removeEventListener('click', onClickCapture, true);
            container.removeEventListener('wheel', onWheel);
            edgeVertexBuffer?.destroy();
            particleInstanceBuffer?.destroy();
            edgeParamBuffer?.destroy();
            quadBuffer.destroy();
            uniformBuffer.destroy();
            device.destroy();
        },
    };
}
