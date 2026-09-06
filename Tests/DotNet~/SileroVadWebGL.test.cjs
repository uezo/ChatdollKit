const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { pathToFileURL } = require('node:url');
const { test } = require('node:test');
const source = fs.readFileSync(path.join(__dirname, '../../Plugins/SileroVadWebGL.jslib'), 'utf8');
const runtimeUrl = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.21.0/dist/ort.wasm.min.js';
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
};

function harness(options = {}) {
    const calls = [], scripts = [], tensors = [], sessions = [], releases = [];
    class Tensor {
        constructor(type, data, dims) { this.type = type; this.data = data; this.dims = dims; this.disposed = false; tensors.push(this); }
        dispose() { assert.equal(this.disposed, false); this.disposed = true; }
    }
    const result = (probability = 0.25, state = 1) => ({
        output: new Tensor('float32', new Float32Array([probability]), [1, 1]),
        stateN: new Tensor('float32', new Float32Array(256).fill(state), [2, 1, 128])
    });
    const ort = options.ort || {
        Tensor, env: { wasm: {} },
        InferenceSession: {
            create: async (url, sessionOptions) => {
                if (options.createError) throw Error(options.createError);
                if (options.createGate) await options.createGate.promise;
                const session = {
                    inputNames: ['input', 'state', 'sr'], outputNames: ['output', 'stateN'], runs: [],
                    run: async feeds => {
                        const frame = {
                            input: Array.from(feeds.input.data), inputDims: Array.from(feeds.input.dims),
                            state: Array.from(feeds.state.data), stateDims: Array.from(feeds.state.dims),
                            sr: Array.from(feeds.sr.data), srDims: Array.from(feeds.sr.dims)
                        };
                        session.runs.push(frame);
                        if (options.run) return options.run(feeds, session, result);
                        return result(0.25, session.runs.length);
                    },
                    release: async () => releases.push(session)
                };
                session.url = url;
                session.options = sessionOptions;
                sessions.push(session);
                return session;
            }
        }
    };
    const context = {
        LibraryManager: { library: {} }, mergeInto: Object.assign, UTF8ToString: value => value,
        Float32Array, BigInt64Array, URL, setTimeout: options.setTimeout || setTimeout,
        clearTimeout: options.clearTimeout || clearTimeout, console,
        HEAPF32: new Float32Array(2048), window: options.preloaded ? { ort: options.preloaded } : {},
        document: {
            baseURI: 'https://example.test/game/',
            createElement: () => ({ remove() { this.removed = true; } }),
            head: { appendChild(script) {
                scripts.push(script);
                if (!options.manualScript) queueMicrotask(() => { context.window.ort = ort; script.onload(); });
            } }
        },
        SendMessage: (target, method, value) => calls.push({ target, method, value })
    };
    vm.createContext(context);
    vm.runInContext(source, context);
    context.SileroVadWebGL = context.LibraryManager.library.$SileroVadWebGL;
    const lib = context.LibraryManager.library;
    return {
        lib, state: context.SileroVadWebGL, context, calls, scripts, tensors, sessions, releases, ort,
        create: (target = 'test', url = runtimeUrl) => lib.SileroVadCreate('model.onnx', url, target),
        predict(handle, requestId = 1, samples = new Float32Array(512), rate = 16000) {
            context.HEAPF32.set(samples, 16);
            return lib.SileroVadPredict(handle, requestId, 64, samples.length, rate);
        },
        messages: method => calls.filter(call => call.method === method).map(call => JSON.parse(call.value)),
        async settle(handle) { await context.SileroVadWebGL.entries[handle].queue; }
    };
}

test('initialization loads one pinned runtime and creates independent WASM sessions', async () => {
    const r = harness();
    const a = r.create('a'), b = r.create('b');
    assert.equal(r.calls.length, 0);
    await Promise.all([r.settle(a), r.settle(b)]);
    assert.notEqual(a, b);
    assert.equal(r.scripts.length, 1);
    assert.equal(r.scripts[0].src, runtimeUrl);
    assert.deepEqual(r.ort.env.wasm, { wasmPaths: new URL('.', runtimeUrl).href, numThreads: 1, proxy: false });
    assert.deepEqual(r.calls.map(call => [call.target, call.method]), [['a', 'OnSileroReady'], ['b', 'OnSileroReady']]);
    assert.equal(r.sessions.length, 2);
    assert.deepEqual(Array.from(r.sessions[0].options.executionProviders), ['wasm']);
});

test('frames retain native-compatible recurrent state and 64/32 samples of context', async () => {
    const r = harness(), id = r.create();
    const first = Float32Array.from({ length: 512 }, (_, i) => i / 512);
    r.predict(id, 1, first);
    r.predict(id, 2, new Float32Array(512).fill(0.5));
    await r.settle(id);
    const [a, b] = r.sessions[0].runs;
    assert.deepEqual(a.inputDims, [1, 576]);
    assert.deepEqual(a.stateDims, [2, 1, 128]);
    assert.deepEqual(a.srDims, []);
    assert.deepEqual(a.sr, [16000n]);
    assert.deepEqual(a.input.slice(0, 64), Array(64).fill(0));
    assert.deepEqual(a.input.slice(64), Array.from(first));
    assert.ok(a.state.every(value => value === 0));
    assert.deepEqual(b.input.slice(0, 64), Array.from(first.slice(-64)));
    assert.ok(b.state.every(value => value === 1));
    r.predict(id, 3, new Float32Array(256).fill(0.75), 8000);
    await r.settle(id);
    const c = r.sessions[0].runs[2];
    assert.deepEqual(c.inputDims, [1, 288]);
    assert.deepEqual(c.sr, [8000n]);
    assert.deepEqual(c.input.slice(0, 32), Array(32).fill(0));
    assert.ok(c.state.every(value => value === 0));
    r.predict(id, 4);
    await r.settle(id);
    assert.ok(r.sessions[0].runs[3].state.every(value => value === 0));
    assert.ok(r.tensors.every(tensor => tensor.disposed));
    assert.deepEqual(r.messages('OnSileroResult').map(message => message.requestId), [1, 2, 3, 4]);
});

test('Unity input is copied before initialization, heap mutation, or memory growth', async () => {
    const r = harness(), id = r.create();
    r.predict(id, 1, new Float32Array(512).fill(0.75));
    r.context.HEAPF32.fill(-1);
    r.context.HEAPF32 = new Float32Array(4096);
    await r.settle(id);
    assert.ok(r.sessions[0].runs[0].input.slice(64).every(value => value === 0.75));
});

test('reset suppresses in-flight and queued results without overlapping session runs', async () => {
    const gate = deferred();
    const r = harness({ run: async (feeds, session, result) => {
        if (session.runs.length === 1) await gate.promise;
        return result(0.25, 7);
    } });
    const id = r.create();
    r.predict(id, 1, new Float32Array(512).fill(0.5));
    r.predict(id, 2);
    await flush();
    assert.equal(r.sessions[0].runs.length, 1);
    r.lib.SileroVadReset(id);
    r.predict(id, 3);
    await flush();
    assert.equal(r.sessions[0].runs.length, 1);
    gate.resolve();
    await r.settle(id);
    assert.equal(r.sessions[0].runs.length, 2);
    assert.ok(r.sessions[0].runs[1].state.every(value => value === 0));
    assert.ok(r.sessions[0].runs[1].input.slice(0, 64).every(value => value === 0));
    assert.deepEqual(r.messages('OnSileroResult'), [{ requestId: 3, probability: 0.25 }]);
    assert.ok(r.tensors.every(tensor => tensor.disposed));
});

test('reset before readiness cancels queued audio but preserves model initialization', async () => {
    const r = harness(), id = r.create();
    r.predict(id, 1);
    r.lib.SileroVadReset(id);
    r.predict(id, 2);
    await r.settle(id);
    assert.equal(r.sessions[0].runs.length, 1);
    assert.equal(r.calls.filter(call => call.method === 'OnSileroReady').length, 1);
    assert.deepEqual(r.messages('OnSileroResult').map(value => value.requestId), [2]);
});

test('an inference failure arriving after reset cannot fail the next request', async () => {
    const gate = deferred();
    const r = harness({ run: async (feeds, session, result) => {
        if (session.runs.length === 1) { await gate.promise; throw Error('Stale inference failure'); }
        return result();
    } });
    const id = r.create();
    r.predict(id, 1);
    await flush();
    r.lib.SileroVadReset(id);
    r.predict(id, 2);
    gate.resolve();
    await r.settle(id);
    assert.deepEqual(r.messages('OnSileroError'), []);
    assert.deepEqual(r.messages('OnSileroResult').map(value => value.requestId), [2]);
    assert.ok(r.tensors.every(tensor => tensor.disposed));
});

test('disposing an in-flight session suppresses callbacks and releases only after inference', async () => {
    const gate = deferred();
    const r = harness({ run: async (feeds, session, result) => { await gate.promise; return result(); } });
    const id = r.create();
    r.predict(id, 1);
    await flush();
    r.lib.SileroVadDispose(id);
    r.lib.SileroVadDispose(id);
    r.lib.SileroVadReset(id);
    assert.equal(r.state.entries[id], undefined);
    assert.equal(r.predict(id, 2), 0);
    assert.equal(r.releases.length, 0);
    gate.resolve();
    await flush();
    assert.equal(r.releases.length, 1);
    assert.equal(r.messages('OnSileroResult').length, 0);
    assert.ok(r.tensors.every(tensor => tensor.disposed));
});

test('disposing during session creation releases the eventual session without readiness callback', async () => {
    const gate = deferred(), r = harness({ createGate: gate }), id = r.create();
    await flush();
    r.lib.SileroVadDispose(id);
    gate.resolve();
    await flush();
    assert.equal(r.releases.length, 1);
    assert.equal(r.calls.length, 0);
});

test('disposing before the runtime loads avoids creating a session', async () => {
    const r = harness(), id = r.create();
    r.lib.SileroVadDispose(id);
    await flush();
    assert.equal(r.sessions.length, 0);
    assert.equal(r.calls.length, 0);
});

test('validation errors retain valid stream state and invalid handles reject synchronously', async () => {
    const r = harness(), id = r.create();
    r.predict(id, 1, new Float32Array(512).fill(0.5));
    r.predict(id, 2, new Float32Array(511));
    r.predict(id, 3, new Float32Array(512), 44100);
    assert.equal(r.lib.SileroVadPredict(id, 4, 3, 512, 16000), 1);
    assert.equal(r.lib.SileroVadPredict(id, 5, 99999 * 4, 512, 16000), 1);
    assert.equal(r.predict(99999), 0);
    r.predict(id, 6);
    await r.settle(id);
    assert.equal(r.sessions[0].runs.length, 2);
    assert.ok(r.sessions[0].runs[1].state.every(value => value === 1));
    assert.ok(r.sessions[0].runs[1].input.slice(0, 64).every(value => value === 0.5));
    assert.deepEqual(r.messages('OnSileroError').map(message => message.requestId), [2, 3, 4, 5]);
});

test('failed runs report the request and dispose inputs; later requests can recover', async () => {
    const r = harness({ run: async (feeds, session, result) => {
        if (session.runs.length === 1) throw Error('Inference failed');
        return result();
    } });
    const id = r.create();
    r.predict(id, 1);
    r.predict(id, 2);
    await r.settle(id);
    assert.deepEqual(r.messages('OnSileroError'), [{ requestId: 1, message: 'Inference failed' }]);
    assert.deepEqual(r.messages('OnSileroResult').map(message => message.requestId), [2]);
    assert.ok(r.tensors.every(tensor => tensor.disposed));
});

test('invalid outputs report errors and are disposed without committing state', async () => {
    const r = harness({ run: async (feeds, session, result) => result(NaN, 42) }), id = r.create();
    r.predict(id, 1);
    await r.settle(id);
    assert.match(r.messages('OnSileroError')[0].message, /invalid speech probability/);
    assert.ok(r.state.entries[id].state.every(value => value === 0));
    assert.ok(r.tensors.every(tensor => tensor.disposed));
});

test('incorrect output shape fails the request and disposes every output tensor', async () => {
    const r = harness({ run: async (feeds, session, result) => {
        const outputs = result();
        outputs.stateN.dims = [256];
        return outputs;
    } });
    const id = r.create();
    r.predict(id, 1);
    await r.settle(id);
    assert.match(r.messages('OnSileroError')[0].message, /unexpected probability or state shape/);
    assert.ok(r.tensors.every(tensor => tensor.disposed));
});

test('independent models never share their state or context', async () => {
    const r = harness(), a = r.create('a'), b = r.create('b');
    r.predict(a, 1, new Float32Array(512).fill(0.5));
    await r.settle(a);
    r.predict(b, 2);
    r.predict(a, 3);
    await Promise.all([r.settle(a), r.settle(b)]);
    assert.ok(r.sessions[1].runs[0].state.every(value => value === 0));
    assert.ok(r.sessions[1].runs[0].input.every(value => value === 0));
    assert.ok(r.sessions[0].runs[1].state.every(value => value === 1));
    assert.ok(r.sessions[0].runs[1].input.slice(0, 64).every(value => value === 0.5));
});

test('external runtimes and conflicting runtime URLs are rejected explicitly', async () => {
    const r = harness({ preloaded: { version: 'old' } }), id = r.create();
    await r.settle(id);
    assert.match(r.messages('OnSileroError')[0].message, /already loaded outside Silero/);
    assert.equal(r.messages('OnSileroError')[0].requestId, 0);
    assert.equal(r.scripts.length, 0);
    const s = harness(), a = s.create(), b = s.create('b', 'https://example.test/other/ort.wasm.min.js');
    await Promise.all([s.settle(a), s.settle(b)]);
    assert.match(s.messages('OnSileroError')[0].message, /same ONNX Runtime Web script URL/);
    assert.equal(s.sessions.length, 1);
});

test('script failures are reported and a later initialization can retry', async () => {
    const r = harness({ manualScript: true }), a = r.create();
    await flush();
    r.scripts[0].onerror();
    await r.settle(a);
    assert.match(r.messages('OnSileroError')[0].message, /Failed to load/);
    assert.equal(r.scripts[0].removed, true);
    const b = r.create('b');
    await flush();
    assert.equal(r.scripts.length, 2);
    r.context.window.ort = r.ort;
    r.scripts[1].onload();
    await r.settle(b);
    assert.equal(r.sessions.length, 1);
    assert.equal(r.calls.at(-1).method, 'OnSileroReady');
});

test('model initialization failures are propagated without a readiness callback', async () => {
    const r = harness({ createError: 'Model fetch failed' }), id = r.create();
    await r.settle(id);
    assert.deepEqual(r.messages('OnSileroError'), [{ requestId: 0, message: 'Model fetch failed' }]);
    assert.equal(r.calls.length, 1);
    r.lib.SileroVadDispose(id);
    await flush();
});

test('script loading timeout reports an initialization error and permits retry', async () => {
    let timeoutCallback;
    let cancelled = 0;
    const r = harness({ manualScript: true,
        setTimeout: callback => { timeoutCallback = callback; return 123; },
        clearTimeout: id => { assert.equal(id, 123); ++cancelled; }
    });
    const id = r.create();
    await flush();
    timeoutCallback();
    await r.settle(id);
    assert.match(r.messages('OnSileroError')[0].message, /Timed out loading/);
    assert.equal(r.state.runtimePromise, null);
    assert.equal(r.scripts[0].removed, true);
    assert.equal(cancelled, 1);
});

// Opt-in: use the actual ONNX Runtime Web WASM backend, never onnxruntime-node.
// CHATDOLLKIT_SILERO_WEB_RUNTIME=/path/to/onnxruntime-web/dist/ort.wasm.min.js
// CHATDOLLKIT_SILERO_MODEL_PATH=/path/to/silero_vad.onnx node --test SileroVadWebGL.test.cjs
test('real WASM probabilities match the native/Python reference at 16 kHz and 8 kHz', {
    skip: !process.env.CHATDOLLKIT_SILERO_WEB_RUNTIME || !process.env.CHATDOLLKIT_SILERO_MODEL_PATH,
    timeout: 30000
}, async () => {
    const modelBytes = new Uint8Array(fs.readFileSync(process.env.CHATDOLLKIT_SILERO_MODEL_PATH));
    const hash = require('node:crypto').createHash('sha256').update(modelBytes).digest('hex');
    assert.equal(hash, 'a4a068cd6cf1ea8355b84327595838ca748ec29a25bc91fc82e6c299ccdc5808', 'Use the model documented in OnnxSileroVadModelTests.');
    const actual = require(path.resolve(process.env.CHATDOLLKIT_SILERO_WEB_RUNTIME));
    const ort = {
        env: actual.env, Tensor: actual.Tensor,
        InferenceSession: { create: (url, options) => actual.InferenceSession.create(modelBytes, options) }
    };
    const r = harness({ ort });
    const id = r.create('wasm', pathToFileURL(path.resolve(process.env.CHATDOLLKIT_SILERO_WEB_RUNTIME)).href);
    await r.settle(id);
    assert.deepEqual(r.messages('OnSileroError'), []);
    const expected = {
        16000: [0.01201203465, 0.05746787786, 0.05079486966, 0.04458412528, 0.03277337551, 0.01497119665,
            0.01267051697, 0.0168544054, 0.02390763164, 0.01497945189, 0.004746317863, 0.006074130535],
        8000: [0.005321055651, 0.008907079697, 0.01409497857, 0.02212440968, 0.005745410919, 0.003769934177,
            0.01101842523, 0.0044272542, 0.000635176897, 0.008609205484, 0.001866310835, 0.002747178078]
    };
    try {
        let requestId = 0;
        for (const rate of [16000, 8000, 16000]) {
            let random = 123456789;
            for (let frame = 0; frame < expected[rate].length; ++frame) {
                const samples = new Float32Array(rate === 16000 ? 512 : 256);
                for (let i = 0; i < samples.length; ++i) {
                    random = (Math.imul(1664525, random) + 1013904223) >>> 0;
                    samples[i] = frame % 4 === 0 ? 0 : ((random >>> 16) - 32768) / 32768;
                }
                r.predict(id, ++requestId, samples, rate);
                await r.settle(id);
                assert.deepEqual(r.messages('OnSileroError'), []);
                const probability = r.messages('OnSileroResult').at(-1).probability;
                assert.ok(Math.abs(probability - expected[rate][frame]) <= 0.00001,
                    `${rate} Hz frame ${frame}: expected ${expected[rate][frame]}, got ${probability}`);
            }
            r.lib.SileroVadReset(id);
        }
    } finally {
        r.lib.SileroVadDispose(id);
        await flush();
    }
});
