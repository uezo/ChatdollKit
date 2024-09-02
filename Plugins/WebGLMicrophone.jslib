mergeInto(LibraryManager.library, {
    InitWebGLMicrophone: function(targetObjectNamePtr) {
        // Initialize webGLMicrophone
        document.webGLMicrophone = new Object();
        document.webGLMicrophone.isRecording = 0;
        document.webGLMicrophone.targetObjectName = UTF8ToString(targetObjectNamePtr);
        document.webGLMicrophone.audioContext = new (window.AudioContext || window.webkitAudioContext)();
        document.webGLMicrophone.source = null;
        document.webGLMicrophone.scriptNode = null;

        // Observe the state of audio context for microphone and resume if disabled
        setInterval(function() {
            if (document.webGLMicrophone.audioContext.state === "suspended" || document.webGLMicrophone.audioContext.state === "interrupted") {
                console.log("Resuming AudioContext: " + document.webGLMicrophone.audioContext.state);
                document.webGLMicrophone.audioContext.resume();
            }
        }, 300);
    },

    StartWebGLMicrophone: function() {
        document.webGLMicrophone.isRecording = 1;

        if (navigator.mediaDevices.getUserMedia) {
            navigator.mediaDevices.getUserMedia({ audio: true })
            .then(function(stream) {
                // Setup nodes
                var audioContext = document.webGLMicrophone.audioContext;
                var source = audioContext.createMediaStreamSource(stream);
                var scriptNode = audioContext.createScriptProcessor(4096, 1, 1);
                scriptNode.onaudioprocess = function (event) {
                    SendMessage(document.webGLMicrophone.targetObjectName, "SetSamplingData", event.inputBuffer.getChannelData(0).join(','));
                };
                // Connect nodes;
                source.connect(scriptNode);
                scriptNode.connect(audioContext.destination);

                document.webGLMicrophone.scriptNode = scriptNode;
                document.webGLMicrophone.source = source;

                console.log("WebGLMicrophone started recording");
            })
            .catch(function(err) {
                console.log("Failed in GetUserMedia: " + error);
                document.webGLMicrophone.isRecording = 0;
            });
        }
    },

    EndWebGLMicrophone: function() {
        console.log("EndWebGLMicrophone");
        if (document.webGLMicrophone.source != null) {
            document.webGLMicrophone.source.disconnect(document.webGLMicrophone.scriptNode);
            document.webGLMicrophone.source = null;
        }
        if (document.webGLMicrophone.scriptNode != null) {
            document.webGLMicrophone.scriptNode.disconnect();
            document.webGLMicrophone.scriptNode = null;
        }
        document.webGLMicrophone.isRecording = 0;
    },

    IsWebGLMicrophoneRecording: function() {
        return document.webGLMicrophone.isRecording == 1;
    },
});
