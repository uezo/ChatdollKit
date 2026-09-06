mergeInto(LibraryManager.library, {
    $AIAvatarSockets: { next: 1, entries: {} },
    AIAvatarSocketConnect__deps: ['$AIAvatarSockets'],
    AIAvatarSocketConnect: function (urlPointer, protocolPointer, targetPointer) {
        var id = AIAvatarSockets.next++;
        var target = UTF8ToString(targetPointer);
        var protocol = UTF8ToString(protocolPointer);
        var entry = { socket: null, disposed: false };
        AIAvatarSockets.entries[id] = entry;
        var notify = function (method, value) {
            if (!entry.disposed) SendMessage(target, method, value || '');
        };
        try {
            var socket = protocol ? new WebSocket(UTF8ToString(urlPointer), [protocol]) : new WebSocket(UTF8ToString(urlPointer));
            entry.socket = socket;
            socket.onopen = function () { notify('OnWebSocketOpen'); };
            socket.onmessage = function (event) {
                if (typeof event.data !== 'string') { notify('OnWebSocketError'); return; }
                notify('OnWebSocketMessage', event.data);
            };
            socket.onerror = function () { notify('OnWebSocketError'); };
            socket.onclose = function () { notify('OnWebSocketClose'); };
        } catch (error) {
            // Defer until C# has received the handle. Never include credentials in callbacks.
            setTimeout(function () { notify('OnWebSocketError'); }, 0);
        }
        return id;
    },
    AIAvatarSocketSend__deps: ['$AIAvatarSockets'],
    AIAvatarSocketSend: function (id, messagePointer) {
        var entry = AIAvatarSockets.entries[id];
        if (!entry || entry.disposed || !entry.socket || entry.socket.readyState !== WebSocket.OPEN) return 0;
        try { entry.socket.send(UTF8ToString(messagePointer)); return 1; }
        catch (error) { return 0; }
    },
    AIAvatarSocketBufferedAmount__deps: ['$AIAvatarSockets'],
    AIAvatarSocketBufferedAmount: function (id) {
        var entry = AIAvatarSockets.entries[id];
        return entry && entry.socket ? Math.min(entry.socket.bufferedAmount, 2147483647) : 0;
    },
    AIAvatarSocketDispose__deps: ['$AIAvatarSockets'],
    AIAvatarSocketDispose: function (id) {
        var entry = AIAvatarSockets.entries[id];
        if (!entry) return;
        entry.disposed = true;
        delete AIAvatarSockets.entries[id];
        if (entry.socket) {
            entry.socket.onopen = entry.socket.onmessage = entry.socket.onerror = entry.socket.onclose = null;
            try { entry.socket.close(1000, ''); } catch (error) { }
        }
    }
});
