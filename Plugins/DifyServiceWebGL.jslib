mergeInto(LibraryManager.library, {
    StartDifyMessageStreamJS: function(targetObjectNamePtr, sessionIdPtr, urlPtr, apiKeyPtr, userPtr, difyStreamRequestPtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let sessionId = UTF8ToString(sessionIdPtr);
        let url = UTF8ToString(urlPtr);
        let apiKey = UTF8ToString(apiKeyPtr);
        let user = UTF8ToString(userPtr);
        let difyStreamRequest = UTF8ToString(difyStreamRequestPtr);
        let decoder = new TextDecoder("utf-8");

        if (document.difyAbortController == null) {
            document.difyAbortController = new AbortController();
        }

        fetch(url, {
            headers: {
                "Content-Type": "application/json",
                "Authorization": `Bearer ${apiKey}`
            },
            method: "POST",
            body: difyStreamRequest,
            signal: document.difyAbortController.signal
        })
        .then(response => {
            if (!response.ok) {
                SendMessage(
                    targetObjectName,
                    "SetDifyMessageStreamChunk",
                    sessionId + "::Error: " + response.status
                );
            }
            return response.body.getReader();
        })
        .then((reader) => {
            const readChunk = function({done, value}) {
                if(done) {
                    // Send empty message to ensure to stop stream handling
                    SendMessage(targetObjectName, "SetDifyMessageStreamChunk", sessionId + "::");
                    reader.releaseLock();
                    return;
                }
                SendMessage(targetObjectName, "SetDifyMessageStreamChunk", sessionId + "::" + decoder.decode(value));
                reader.read().then(readChunk);
            }
            reader.read().then(readChunk);
        })
        .catch((err) => {
            console.error(`Error at fetch: ${err.message}`);
        });
    },

    AbortDifyMessageStreamJS: function() {
        console.log("Abort Dify at AbortDifyMessageStreamJS");
        document.difyAbortController.abort();
    }
});
