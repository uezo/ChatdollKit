mergeInto(LibraryManager.library, {
    InitJSMessageHandler: function(targetObjectNamePtr, targetFunctionNamePtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let targetFunctionName = UTF8ToString(targetFunctionNamePtr);
        window.SendMessageToChatdollKit = (message) => {
            console.log("Send message to ChatdollKit: " + message);
            SendMessage(targetObjectName, targetFunctionName, message);
        };
    }
});
