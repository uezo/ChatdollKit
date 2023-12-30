mergeInto(LibraryManager.library, {
    StartClaudeMessageStreamJS: function(targetObjectNamePtr, sessionIdPtr, urlPtr, apiKeyPtr, claudeStreamRequestPtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let sessionId = UTF8ToString(sessionIdPtr);
        let url = UTF8ToString(urlPtr);
        let apiKey = UTF8ToString(apiKeyPtr);
        let claudeStreamRequest = UTF8ToString(claudeStreamRequestPtr);
        let decoder = new TextDecoder("utf-8");

        if (document.claudeAbortController == null) {
            document.claudeAbortController = new AbortController();
        }

        fetch(url, {
            headers: {
                "anthropic-version": "2023-06-01",
                "anthropic-beta": "messages-2023-12-15",
                "Content-Type": "application/json",
                "x-api-key": `${apiKey}`
            },
            method: "POST",
            body: claudeStreamRequest,
            signal: document.claudeAbortController.signal
        })
        .then((response) => response.body.getReader())
        .then((reader) => {
            const readChunk = function({done, value}) {
                if(done) {
                    reader.releaseLock();
                    return;
                }
                SendMessage(targetObjectName, "SetClaudeMessageStreamChunk", sessionId + "::" + decoder.decode(value));
                reader.read().then(readChunk);
            }
            reader.read().then(readChunk);
        })
        .catch((err) => {
            console.error(`Error at fetch: ${err.message}`);
        });
    },

    AbortClaudeMessageStreamJS: function() {
        console.log("Abort Claude at AbortClaudeMessageStreamJS");
        document.claudeAbortController.abort();
    }
});
