mergeInto(LibraryManager.library, {
    StartGeminiMessageStreamJS: function(targetObjectNamePtr, sessionIdPtr, urlPtr, apiKeyPtr, geminiStreamRequestPtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let sessionId = UTF8ToString(sessionIdPtr);
        let url = UTF8ToString(urlPtr);
        let apiKey = UTF8ToString(apiKeyPtr);
        let geminiStreamRequest = UTF8ToString(geminiStreamRequestPtr);
        let decoder = new TextDecoder("utf-8");

        if (document.geminiAbortController == null) {
            document.geminiAbortController = new AbortController();
        }

        fetch(url + "?key=" + apiKey, {
            headers: {
                "Content-Type": "application/json",
            },
            method: "POST",
            body: geminiStreamRequest,
            signal: document.geminiAbortController.signal
        })
        .then((response) => response.body.getReader())
        .then((reader) => {
            const readChunk = function({done, value}) {
                if(done) {
                    reader.releaseLock();
                    return;
                }
                SendMessage(targetObjectName, "SetGeminiMessageStreamChunk", sessionId + "::" + decoder.decode(value));
                reader.read().then(readChunk);
            }
            reader.read().then(readChunk);
        })
        .catch((err) => {
            console.error(`Error at fetch: ${err.message}`);
        });
    },

    AbortGeminiMessageStreamJS: function() {
        console.log("Abort Gemini at AbortGeminiMessageStreamJS");
        document.geminiAbortController.abort();
    }
});
