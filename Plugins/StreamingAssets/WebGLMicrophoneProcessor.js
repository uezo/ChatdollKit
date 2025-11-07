class WebGLMicrophoneProcessor extends AudioWorkletProcessor {
  process(inputs) {
    const input = inputs[0];
    if (input && input[0].length > 0) {
      this.port.postMessage(input[0]);
    }
    return true;
  }
}

registerProcessor("webgl-microphone-processor", WebGLMicrophoneProcessor);
