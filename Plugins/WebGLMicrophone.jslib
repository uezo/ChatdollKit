mergeInto(LibraryManager.library, {
  InitWebGLMicrophone: function(targetObjectNamePtr) {
    var mic = document.webGLMicrophone = {};
    mic.isRecording = 0;
    mic.targetObjectName = UTF8ToString(targetObjectNamePtr);
    mic.audioContext = new (window.AudioContext || window.webkitAudioContext)();
    mic.source = null;
    mic.workletNode = null;
    mic.processorModuleUrl = "StreamingAssets/WebGLMicrophoneProcessor.js";
    mic._buffer = [];
    mic._bufferSize = 4096;

    setInterval(function() {
      var ac = mic.audioContext;
      if (ac.state === "suspended" || ac.state === "interrupted") {
        console.log("Resuming AudioContext:", ac.state);
        ac.resume();
      }
    }, 300);
  },

  StartWebGLMicrophone: function() {
    var mic = document.webGLMicrophone;
    mic.isRecording = 1;

    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      console.error("getUserMedia not supported");
      mic.isRecording = 0;
      return;
    }

    navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 }
    })
    .then(function(stream) {
      var ac = mic.audioContext;
      ac.audioWorklet.addModule(mic.processorModuleUrl)
        .then(function() {
          mic.source = ac.createMediaStreamSource(stream);
          mic.workletNode = new AudioWorkletNode(ac, "webgl-microphone-processor");

          mic.workletNode.port.onmessage = function(event) {
            var float32Array = event.data;
            for (var i = 0; i < float32Array.length; i++) {
              mic._buffer.push(float32Array[i]);
            }
            if (mic._buffer.length >= mic._bufferSize) {
              var chunk = mic._buffer.slice(0, mic._bufferSize);
              mic._buffer = mic._buffer.slice(mic._bufferSize);
              var csv = Array.prototype.join.call(chunk, ",");
              SendMessage(mic.targetObjectName, "SetSamplingData", csv);
            }
          };

          mic.source.connect(mic.workletNode);
          mic.workletNode.connect(ac.destination);

          console.log("WebGLMicrophone started recording");
        })
        .catch(function(err) {
          console.error("Failed to load AudioWorklet module:", err);
          mic.isRecording = 0;
        });
    })
    .catch(function(err) {
      console.error("Failed in getUserMedia:", err);
      mic.isRecording = 0;
    });
  },

  EndWebGLMicrophone: function() {
    var mic = document.webGLMicrophone;
    console.log("EndWebGLMicrophone");
    if (mic.source && mic.workletNode) {
      mic.source.disconnect(mic.workletNode);
      mic.workletNode.disconnect();
    }
    mic.source = null;
    mic.workletNode = null;
    mic._buffer = [];
    mic.isRecording = 0;
  },

  IsWebGLMicrophoneRecording: function() {
    return document.webGLMicrophone &&
           document.webGLMicrophone.isRecording === 1;
  },
});
