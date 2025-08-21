mergeInto(LibraryManager.library, {
  JsFree: function (p) { _free(p); },

  InitWebGLMicrophone: function(targetObjectNamePtr, useMalloc) {
    var mic = document.webGLMicrophone = {};
    mic.isRecording = 0;
    mic.targetObjectName = UTF8ToString(targetObjectNamePtr);
    mic.audioContext = new (window.AudioContext || window.webkitAudioContext)();
    mic.source = null;
    mic.workletNode = null;
    mic.processorModuleUrl = "StreamingAssets/WebGLMicrophoneProcessor.js";
    mic.useMalloc = useMalloc;
    mic._buffer = [];
    mic._bufferSize = 2048;
    
    // Properties for VAD
    mic.vadBuffer = [];
    mic.isVoiceDetected = false;
    mic.voiceProbability = 0.0;
    
    // Downsampling function for VAD
    mic.downsampleTo16kHz = function(buffer, fromSampleRate) {
      if (fromSampleRate === 16000) {
        return Array.from(buffer);
      }
      
      var sampleRateRatio = 16000 / fromSampleRate;
      var newLength = Math.round(buffer.length * sampleRateRatio);
      var result = new Array(newLength);
      var offsetResult = 0;
      var offsetBuffer = 0;
      
      while (offsetResult < result.length) {
        var nextOffsetBuffer = Math.round((offsetResult + 1) * (1 / sampleRateRatio));
        var accum = 0;
        var count = 0;
        
        for (var i = offsetBuffer; i < nextOffsetBuffer && i < buffer.length; i++) {
          accum += buffer[i];
          count++;
        }
        
        result[offsetResult] = accum / count;
        offsetResult++;
        offsetBuffer = nextOffsetBuffer;
      }
      
      return result;
    };

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
            
            // VAD
            if (window.vad && window.vad.isReady) {
              try {
                // Downsample to 16kHz
                var downsampled = mic.downsampleTo16kHz(float32Array, ac.sampleRate);
                mic.vadBuffer.push(...downsampled);
                
                // Process each 512 sample
                var chunkSize = window.vad.chunkSize || 512;
                while (mic.vadBuffer.length >= chunkSize) {
                  var vadChunk = mic.vadBuffer.slice(0, chunkSize);
                  mic.vadBuffer = mic.vadBuffer.slice(chunkSize);
                  
                  (function(chunk) {
                    window.vad.isVoiced(chunk).then(function(voiced) {
                      mic.isVoiceDetected = voiced;
                      mic.voiceProbability = window.vad.lastProbability || 0;
                    }).catch(function(error) {
                      console.error("VAD processing error:", error);
                    });
                  })(vadChunk);
                }
              } catch (error) {
                console.error("VAD error:", error);
              }
            }
            
            for (var i = 0; i < float32Array.length; i++) {
              mic._buffer.push(float32Array[i]);
            }

            if (mic._buffer.length >= mic._bufferSize) {
              if (mic.useMalloc) {
                while (mic._buffer.length >= mic._bufferSize) {
                  var chunk = mic._buffer.splice(0, mic._bufferSize);
                  var ptr = _malloc(mic._bufferSize * 4);
                  Module.HEAPF32.set(chunk, ptr >> 2);
                  SendMessage(mic.targetObjectName, "SetSamplingData", (ptr|0) + ":" + mic._bufferSize);
                }
              } else {
                var chunk = mic._buffer.slice(0, mic._bufferSize);
                mic._buffer = mic._buffer.slice(mic._bufferSize);
                var csv = Array.prototype.join.call(chunk, ",");
                SendMessage(mic.targetObjectName, "SetSamplingData", csv);
              }
            }
          };

          mic.source.connect(mic.workletNode);
          mic.workletNode.connect(ac.destination);

          console.log("WebGLMicrophone started recording");
          
          if (window.vad && window.vad.reset) {
            window.vad.reset();
            console.log("VAD reset on microphone start");
          }
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
    mic.vadBuffer = [];
    mic.isVoiceDetected = false;
    mic.voiceProbability = 0.0;
    
    if (window.vad && window.vad.dispose) {
      window.vad.dispose();
      console.log("VAD disposed");
    }
  },

  IsWebGLMicrophoneRecording: function() {
    return document.webGLMicrophone &&
           document.webGLMicrophone.isRecording === 1;
  },
  
  // Function for VAD
  IsVoiceDetected: function() {
    var mic = document.webGLMicrophone;
    return mic && mic.isVoiceDetected ? 1 : 0;
  },
  
  GetVoiceProbability: function() {
    var mic = document.webGLMicrophone;
    return mic ? mic.voiceProbability : 0.0;
  },
  
  IsVADEnabled: function() {
    return window.vad && window.vad.isReady ? 1 : 0;
  },
  
  SetVADThreshold: function(threshold) {
    if (window.vad && typeof window.vad.threshold !== 'undefined') {
      window.vad.threshold = threshold;
      console.log("VAD threshold set to:", threshold);
    }
  },
  
  GetVADThreshold: function() {
    if (window.vad && typeof window.vad.threshold !== 'undefined') {
      return window.vad.threshold;
    }
    return 0.5;
  }
});
