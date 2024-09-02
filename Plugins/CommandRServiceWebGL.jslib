mergeInto(LibraryManager.library, {
    StartCommandRMessageStreamJS: function(targetObjectNamePtr, sessionIdPtr, urlPtr, apiKeyPtr, commandRStreamRequestPtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let sessionId = UTF8ToString(sessionIdPtr);
        let url = UTF8ToString(urlPtr);
        let apiKey = UTF8ToString(apiKeyPtr);
        let commandRStreamRequest = UTF8ToString(commandRStreamRequestPtr);
        let decoder = new TextDecoder("utf-8");

        if (document.commandRAbortController == null) {
            document.commandRAbortController = new AbortController();
        }

        fetch(url, {
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json",
                "Authorization": `Bearer ${apiKey}`
            },
            method: "POST",
            body: commandRStreamRequest,
            signal: document.commandRAbortController.signal
        })
        .then((response) => response.body.getReader())
        .then((reader) => {
            const readChunk = function({done, value}) {
                if(done) {
                    reader.releaseLock();
                    return;
                }
                SendMessage(targetObjectName, "SetCommandRMessageStreamChunk", sessionId + "::" + decoder.decode(value));
                reader.read().then(readChunk);
            }
            reader.read().then(readChunk);
        })
        .catch((err) => {
            console.error(`Error at fetch: ${err.message}`);
        });
    },

    AbortCommandRMessageStreamJS: function() {
        console.log("Abort Command R at AbortCommandRMessageStreamJS");
        document.commandRAbortController.abort();
    }
});
