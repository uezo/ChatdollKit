mergeInto(LibraryManager.library, {
    ChatCompletionJS: function(targetObjectNamePtr, apiKeyPtr, chatCompletionRequestPtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let apiKey = UTF8ToString(apiKeyPtr);
        let chatCompletionRequest = UTF8ToString(chatCompletionRequestPtr);
        let decoder = new TextDecoder("utf-8");

        fetch("https://api.openai.com/v1/chat/completions", {
            headers: {
                "Content-Type": "application/json",
                Authorization: `Bearer ${apiKey}`
            },
            method: "POST",
            body: chatCompletionRequest
        })
        .then((response) => response.body.getReader())
        .then((reader) => {
            const readChunk = function({done, value}) {
                if(done) {
                    reader.releaseLock();
                    return;
                }
                SendMessage(targetObjectName, "SetChatCompletionStreamChunk", decoder.decode(value));
                reader.read().then(readChunk);
            }
            reader.read().then(readChunk);
        });
    }
});
