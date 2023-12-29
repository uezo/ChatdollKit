mergeInto(LibraryManager.library, {
    ChatCompletionJS: function(targetObjectNamePtr, sessionIdPtr, urlPtr, apiKeyPtr, chatCompletionRequestPtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let sessionId = UTF8ToString(sessionIdPtr);
        let url = UTF8ToString(urlPtr);
        let apiKey = UTF8ToString(apiKeyPtr);
        let chatCompletionRequest = UTF8ToString(chatCompletionRequestPtr);
        let decoder = new TextDecoder("utf-8");

        if (document.chatGPTAbortController == null) {
            document.chatGPTAbortController = new AbortController();
        }

        fetch(url, {
            headers: {
                "Content-Type": "application/json",
                "Authorization": `Bearer ${apiKey}`,
                "api-key": `${apiKey}`
            },
            method: "POST",
            body: chatCompletionRequest,
            signal: document.chatGPTAbortController.signal
        })
        .then((response) => response.body.getReader())
        .then((reader) => {
            const readChunk = function({done, value}) {
                if(done) {
                    reader.releaseLock();
                    return;
                }
                SendMessage(targetObjectName, "SetChatCompletionStreamChunk", sessionId + "::" + decoder.decode(value));
                reader.read().then(readChunk);
            }
            reader.read().then(readChunk);
        })
        .catch((err) => {
            console.error(`Error at fetch: ${err.message}`);
        });
    },

    AbortChatCompletionJS: function() {
        console.log("Abort ChatGPT at AbortChatCompletionJS");
        document.chatGPTAbortController.abort();
    }
});
