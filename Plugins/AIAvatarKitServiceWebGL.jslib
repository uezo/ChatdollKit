mergeInto(LibraryManager.library, {
    StartAIAvatarKitMessageStreamJS: function(targetObjectNamePtr, sessionIdPtr, urlPtr, aakStreamRequestPtr, aakHeadersPtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let sessionId = UTF8ToString(sessionIdPtr);
        let url = UTF8ToString(urlPtr);
        let aakStreamRequest = UTF8ToString(aakStreamRequestPtr);
        let aakHeaders = JSON.parse(UTF8ToString(aakHeadersPtr));
        let decoder = new TextDecoder("utf-8");

        if (document.aakAbortController == null) {
            document.aakAbortController = new AbortController();
        }

        let headers = { "Content-Type": "application/json" };
        for (let key in aakHeaders) {
            headers[key] = aakHeaders[key];
        }
        fetch(url, {
            headers: headers,
            method: "POST",
            body: aakStreamRequest,
            signal: document.aakAbortController.signal
        })
        .then(response => {
            if (!response.ok) {
                SendMessage(
                    targetObjectName,
                    "SetAIAvatarKitMessageStreamChunk",
                    sessionId + "::Error: " + response.status
                );
            }
            return response.body.getReader();
        })
        .then((reader) => {
            const readChunk = function({done, value}) {
                if(done) {
                    // Send empty message to ensure to stop stream handling
                    SendMessage(targetObjectName, "SetAIAvatarKitMessageStreamChunk", sessionId + "::");
                    reader.releaseLock();
                    return;
                }
                SendMessage(targetObjectName, "SetAIAvatarKitMessageStreamChunk", sessionId + "::" + decoder.decode(value));
                reader.read().then(readChunk);
            }
            reader.read().then(readChunk);
        })
        .catch((err) => {
            console.error(`Error at fetch: ${err.message}`);
        });
    },

    AbortAIAvatarKitMessageStreamJS: function() {
        console.log("Abort AIAvatarKit at AbortAIAvatarKitMessageStreamJS");
        document.aakAbortController.abort();
    }
});
