// Silero inference only. Audio capture and speech segmentation stay in C#.
mergeInto(LibraryManager.library, {
    $SileroVadWebGL: {
        nextHandle: 1,
        entries: {},
        runtimeUrl: null,
        runtimePromise: null,

        loadRuntime: function(url) {
            var bridge = SileroVadWebGL;
            return Promise.resolve().then(function() {
                if (!url) throw new Error('An ONNX Runtime Web script URL is required.');
                var absoluteUrl = new URL(url, document.baseURI).href;
                if (bridge.runtimePromise) {
                    if (bridge.runtimeUrl !== absoluteUrl) {
                        throw new Error('Silero instances must use the same ONNX Runtime Web script URL.');
                    }
                    return bridge.runtimePromise;
                }
                if (window.ort) {
                    throw new Error('ONNX Runtime Web is already loaded outside Silero. Remove the separate ort script and let Silero load the configured runtime.');
                }

                bridge.runtimeUrl = absoluteUrl;
                bridge.runtimePromise = new Promise(function(resolve, reject) {
                    var script = document.createElement('script');
                    var timeout = setTimeout(function() {
                        fail('Timed out loading ONNX Runtime Web: ' + absoluteUrl);
                    }, 30000);
                    var settled = false;
                    function cleanup() {
                        clearTimeout(timeout);
                        script.onload = null;
                        script.onerror = null;
                    }
                    function fail(message) {
                        if (settled) return;
                        settled = true;
                        cleanup();
                        script.remove();
                        reject(new Error(message));
                    }
                    script.onload = function() {
                        if (settled) return;
                        try {
                            var runtime = window.ort;
                            if (!runtime || !runtime.Tensor || !runtime.InferenceSession || !runtime.env || !runtime.env.wasm) {
                                throw new Error('The configured script did not provide ONNX Runtime Web.');
                            }
                            // Keep the runtime's .mjs and .wasm files alongside this script.
                            // One WASM thread works without cross-origin isolation headers.
                            runtime.env.wasm.wasmPaths = new URL('.', absoluteUrl).href;
                            runtime.env.wasm.numThreads = 1;
                            runtime.env.wasm.proxy = false;
                            settled = true;
                            cleanup();
                            resolve(runtime);
                        } catch (error) {
                            fail(error && error.message ? error.message : String(error));
                        }
                    };
                    script.onerror = function() { fail('Failed to load ONNX Runtime Web: ' + absoluteUrl); };
                    script.src = absoluteUrl;
                    script.async = true;
                    try { document.head.appendChild(script); }
                    catch (error) { fail(error && error.message ? error.message : String(error)); }
                }).catch(function(error) {
                    bridge.runtimePromise = null;
                    bridge.runtimeUrl = null;
                    throw error;
                });
                return bridge.runtimePromise;
            });
        },

        isCurrent: function(entry, generation) {
            return !entry.disposed && entry.generation === generation;
        },

        sendError: function(entry, requestId, error) {
            if (!entry.disposed) {
                SendMessage(entry.target, 'OnSileroError', JSON.stringify({
                    requestId: requestId,
                    message: error && error.message ? error.message : String(error)
                }));
            }
        },

        clearStates: function(entry) {
            // Replace arrays: an in-flight inference may still own the old ones.
            entry.state = new Float32Array(256);
            entry.context = new Float32Array(0);
            entry.sampleRate = 0;
        },

        disposeTensors: function(tensors) {
            var seen = [];
            var failure;
            Object.keys(tensors).forEach(function(key) {
                var tensor = tensors[key];
                if (tensor && typeof tensor.dispose === 'function' && seen.indexOf(tensor) === -1) {
                    seen.push(tensor);
                    try { tensor.dispose(); }
                    catch (error) { if (!failure) failure = error; }
                }
            });
            if (failure) throw failure;
        },

        run: async function(entry, generation, requestId, samples, sampleRate) {
            var bridge = SileroVadWebGL;
            if (!bridge.isCurrent(entry, generation)) return;
            if (entry.initializationError) throw entry.initializationError;
            if (entry.sampleRate !== sampleRate) {
                bridge.clearStates(entry);
                entry.context = new Float32Array(sampleRate === 16000 ? 64 : 32);
            }

            var input = new Float32Array(entry.context.length + samples.length);
            input.set(entry.context);
            input.set(samples, entry.context.length);
            var feeds = {};
            var outputs = {};
            var probability;
            try {
                feeds.input = new entry.runtime.Tensor('float32', input, [1, input.length]);
                feeds.state = new entry.runtime.Tensor('float32', entry.state, [2, 1, 128]);
                feeds.sr = new entry.runtime.Tensor('int64', new BigInt64Array([BigInt(sampleRate)]), []);
                outputs = await entry.session.run(feeds);
                if (!bridge.isCurrent(entry, generation)) return;
                if (!outputs.output || !outputs.stateN || outputs.output.type !== 'float32' || outputs.stateN.type !== 'float32' ||
                    outputs.output.dims.length !== 2 || outputs.stateN.dims.length !== 3 ||
                    outputs.output.data.length !== 1 || outputs.stateN.data.length !== 256) {
                    throw new Error('Silero returned an unexpected probability or state shape.');
                }
                probability = outputs.output.data[0];
                if (!Number.isFinite(probability) || probability < 0 || probability > 1) {
                    throw new Error('Silero returned an invalid speech probability.');
                }
                entry.state = new Float32Array(outputs.stateN.data);
                entry.context = samples.slice(samples.length - entry.context.length);
                entry.sampleRate = sampleRate;
            } finally {
                try { bridge.disposeTensors(outputs); }
                finally { bridge.disposeTensors(feeds); }
            }
            if (bridge.isCurrent(entry, generation)) {
                SendMessage(entry.target, 'OnSileroResult', JSON.stringify({ requestId: requestId, probability: probability }));
            }
        }
    },

    SileroVadCreate__deps: ['$SileroVadWebGL'],
    SileroVadCreate: function(modelUrlPointer, runtimeUrlPointer, targetPointer) {
        var bridge = SileroVadWebGL;
        var entry = {
            target: UTF8ToString(targetPointer),
            generation: 0,
            disposed: false,
            session: null,
            runtime: null,
            initializationError: null
        };
        var handle = bridge.nextHandle++;
        bridge.entries[handle] = entry;
        bridge.clearStates(entry);
        // Copy strings before yielding: Unity owns the pointers only for this call.
        var modelUrl = UTF8ToString(modelUrlPointer);
        var runtimeUrl = UTF8ToString(runtimeUrlPointer);
        entry.queue = bridge.loadRuntime(runtimeUrl).then(async function(runtime) {
            if (entry.disposed) return;
            entry.runtime = runtime;
            entry.session = await runtime.InferenceSession.create(modelUrl, { executionProviders: ['wasm'] });
            if (entry.disposed) return;
            if (!['input', 'state', 'sr'].every(function(name) { return entry.session.inputNames.indexOf(name) !== -1; }) ||
                !['output', 'stateN'].every(function(name) { return entry.session.outputNames.indexOf(name) !== -1; })) {
                throw new Error('The model must expose Silero input/state/sr and output/stateN tensors.');
            }
            SendMessage(entry.target, 'OnSileroReady', '');
        }).catch(function(error) {
            entry.initializationError = error;
            bridge.sendError(entry, 0, error);
        });
        return handle;
    },

    SileroVadPredict__deps: ['$SileroVadWebGL'],
    SileroVadPredict: function(handle, requestId, samplesPointer, sampleCount, sampleRate) {
        var bridge = SileroVadWebGL;
        var entry = bridge.entries[handle];
        if (!entry || entry.disposed) return 0;
        var generation = entry.generation;
        var samples;
        var error;
        try {
            if (sampleRate !== 16000 && sampleRate !== 8000) throw new Error('Silero supports 8000 or 16000 Hz.');
            var expected = sampleRate === 16000 ? 512 : 256;
            if (sampleCount !== expected) throw new Error('Expected ' + expected + ' samples at ' + sampleRate + ' Hz.');
            if (samplesPointer < 0 || samplesPointer % 4 !== 0 || samplesPointer / 4 + sampleCount > HEAPF32.length) {
                throw new Error('Invalid Silero audio buffer.');
            }
            // Never retain a Unity heap view across an await (GC and memory growth).
            samples = new Float32Array(HEAPF32.subarray(samplesPointer / 4, samplesPointer / 4 + sampleCount));
        } catch (caught) { error = caught; }
        entry.queue = entry.queue.then(function() {
            if (!bridge.isCurrent(entry, generation)) return;
            if (error) throw error;
            return bridge.run(entry, generation, requestId, samples, sampleRate);
        }).catch(function(caught) {
            if (bridge.isCurrent(entry, generation)) bridge.sendError(entry, requestId, caught);
        });
        return 1;
    },

    SileroVadReset__deps: ['$SileroVadWebGL'],
    SileroVadReset: function(handle) {
        var entry = SileroVadWebGL.entries[handle];
        if (!entry || entry.disposed) return;
        ++entry.generation;
        SileroVadWebGL.clearStates(entry);
    },

    SileroVadDispose__deps: ['$SileroVadWebGL'],
    SileroVadDispose: function(handle) {
        var bridge = SileroVadWebGL;
        var entry = bridge.entries[handle];
        if (!entry || entry.disposed) return;
        entry.disposed = true;
        ++entry.generation;
        delete bridge.entries[handle];
        bridge.clearStates(entry);
        // release() must wait for both session creation and any active run.
        entry.queue.then(function() {
            if (entry.session) return entry.session.release();
        }).catch(function(error) {
            console.error('Failed to release the Silero ONNX session:', error);
        });
    }
});
