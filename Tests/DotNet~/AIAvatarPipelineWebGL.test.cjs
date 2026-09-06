const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const source = fs.readFileSync(path.join(__dirname, '../../Plugins/AIAvatarPipelineWebGL.jslib'), 'utf8');

function runtime(Socket) {
    const calls = [];
    const context = {
        LibraryManager: { library: {} }, mergeInto: Object.assign,
        UTF8ToString: value => value, WebSocket: Socket, setTimeout,
        SendMessage: (target, method, value) => calls.push({ target, method, value })
    };
    vm.createContext(context);
    vm.runInContext(source, context);
    context.AIAvatarSockets = context.LibraryManager.library.$AIAvatarSockets;
    return { lib: context.LibraryManager.library, state: context.AIAvatarSockets, calls };
}
class FakeSocket {
    static OPEN = 1;
    constructor(url, protocols) { this.url = url; this.protocols = protocols; this.readyState = 1; this.bufferedAmount = 0; this.sent = []; }
    send(message) { this.sent.push(message); }
    close() { this.closed = true; }
}

test('connections route complete Unicode messages independently', () => {
    const r = runtime(FakeSocket);
    const a = r.lib.AIAvatarSocketConnect('ws://example/ws', '', 'a');
    const b = r.lib.AIAvatarSocketConnect('ws://example/ws', 'Authorization.dGVzdA', 'b');
    assert.notEqual(a, b);
    assert.equal(r.state.entries[a].socket.protocols, undefined);
    assert.equal(r.state.entries[b].socket.protocols[0], 'Authorization.dGVzdA');
    r.state.entries[b].socket.onmessage({ data: 'こんにちは::🙂' });
    r.state.entries[a].socket.onopen();
    assert.deepEqual(r.calls, [
        { target: 'b', method: 'OnWebSocketMessage', value: 'こんにちは::🙂' },
        { target: 'a', method: 'OnWebSocketOpen', value: '' }
    ]);
});
test('send checks state and reports failure without throwing', () => {
    const r = runtime(FakeSocket), id = r.lib.AIAvatarSocketConnect('ws://example/ws', '', 'a');
    const socket = r.state.entries[id].socket;
    assert.equal(r.lib.AIAvatarSocketSend(id, 'hello'), 1);
    assert.deepEqual(socket.sent, ['hello']);
    socket.bufferedAmount = 1234;
    assert.equal(r.lib.AIAvatarSocketBufferedAmount(id), 1234);
    socket.readyState = 3;
    assert.equal(r.lib.AIAvatarSocketSend(id, 'closed'), 0);
    assert.equal(r.lib.AIAvatarSocketSend(999, 'missing'), 0);
});
test('dispose detaches handlers and rejects already queued callbacks', () => {
    const r = runtime(FakeSocket), id = r.lib.AIAvatarSocketConnect('ws://example/ws', '', 'a');
    const socket = r.state.entries[id].socket, queued = socket.onmessage;
    r.lib.AIAvatarSocketDispose(id);
    queued({ data: 'late' });
    r.lib.AIAvatarSocketDispose(id);
    assert.equal(socket.onmessage, null);
    assert.equal(socket.closed, true);
    assert.equal(r.state.entries[id], undefined);
    assert.equal(r.calls.length, 0);
});
test('binary frames and socket failures report errors without payload disclosure', () => {
    const r = runtime(FakeSocket), id = r.lib.AIAvatarSocketConnect('ws://example/ws', '', 'a');
    const socket = r.state.entries[id].socket;
    socket.onmessage({ data: new Uint8Array([1, 2, 3]) });
    socket.onerror({ message: 'must not expose event details' });
    socket.onclose();
    assert.deepEqual(r.calls.map(c => c.method), ['OnWebSocketError', 'OnWebSocketError', 'OnWebSocketClose']);
    assert.ok(r.calls.every(c => c.value === ''));
});
test('constructor failures are deferred until the handle is published', async () => {
    const r = runtime(class { constructor() { throw Error('private detail'); } });
    const id = r.lib.AIAvatarSocketConnect('bad', '', 'a');
    assert.ok(id > 0);
    assert.equal(r.calls.length, 0);
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(r.calls[0].method, 'OnWebSocketError');
    assert.equal(r.calls[0].value, '');
    r.lib.AIAvatarSocketDispose(id);
});
test('disposing before a deferred failure suppresses its callback', async () => {
    const r = runtime(class { constructor() { throw Error(); } });
    const id = r.lib.AIAvatarSocketConnect('bad', '', 'a');
    r.lib.AIAvatarSocketDispose(id);
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(r.calls.length, 0);
});
test('browser-compatible WebSocket connects to the live AIAvatarKit protocol', {
    skip: process.env.AIAVATAR_WEBGL_LIVE !== '1', timeout: 15000
}, async () => {
    const r = runtime(globalThis.WebSocket);
    const id = r.lib.AIAvatarSocketConnect(process.env.AIAVATAR_TEST_URL || 'ws://localhost:48001/ws', '', 'live');
    try {
        const until = async predicate => {
            const deadline = Date.now() + 10000;
            while (!predicate()) {
                if (r.calls.some(c => c.method === 'OnWebSocketError')) throw Error('WebSocket failed');
                if (Date.now() > deadline) throw Error('WebSocket test timed out');
                await new Promise(resolve => setTimeout(resolve, 10));
            }
        };
        await until(() => r.calls.some(c => c.method === 'OnWebSocketOpen'));
        const session = 'chatdoll-browser-test-' + require('node:crypto').randomUUID();
        assert.equal(r.lib.AIAvatarSocketSend(id, JSON.stringify({ type: 'start', session_id: session })), 1);
        await until(() => r.calls.some(c => c.method === 'OnWebSocketMessage'));
        const connected = JSON.parse(r.calls.find(c => c.method === 'OnWebSocketMessage').value);
        assert.equal(connected.type, 'connected');
        assert.equal(connected.session_id, session);
    } finally { r.lib.AIAvatarSocketDispose(id); }
});
