class SileroVAD {
    constructor(chunkSize = 512, threshold = 0.5) {
        this.session = null;
        this.chunkSize = chunkSize;
        this.sampleRate = 16000;
        this.threshold = threshold;
        this.lastProbability = 0.0;
        this.state = new Float32Array(256);
        this.audioBuffer = [];
        this.isReady = false;
    }
    
    async initialize(modelPath) {
        try {
            const response = await fetch(modelPath);
            const arrayBuffer = await response.arrayBuffer();
            this.session = await ort.InferenceSession.create(arrayBuffer);
            
            this.resetStates();
            this.isReady = true;
            console.log(`VAD Initialized. Expecting ${this.sampleRate}Hz audio. Processing chunk size: ${this.chunkSize}.`);
            return true;
        } catch (error) {
            console.error('VAD initialization failed:', error);
            this.isReady = false;
            return false;
        }
    }
    
    async isVoiced(samples) {
        if (!this.session || !samples) return false;
        
        this.audioBuffer.push(...samples);
        
        if (this.audioBuffer.length < this.chunkSize) {
            return false;
        }
        
        while (this.audioBuffer.length >= this.chunkSize) {
            const chunkToProcess = this.audioBuffer.slice(0, this.chunkSize);
            this.audioBuffer.splice(0, this.chunkSize);
            
            const hasVoice = await this.runInference(chunkToProcess);
            
            if (hasVoice) {
                this.audioBuffer = [];
                this.resetStates();
                return true;
            }
        }
        
        return false;
    }
    
    async runInference(samples) {
        try {
            const inputTensor = new ort.Tensor(
                'float32', 
                new Float32Array(samples), 
                [1, this.chunkSize]
            );
            
            const srTensor = new ort.Tensor(
                'int64', 
                BigInt64Array.from([BigInt(this.sampleRate)]), 
                [1]
            );
            
            const stateTensor = new ort.Tensor(
                'float32', 
                this.state, 
                [2, 1, 128]
            );
            
            const feeds = {
                'input': inputTensor,
                'sr': srTensor,
                'state': stateTensor
            };
            
            const results = await this.session.run(feeds);
            
            if (results.output) {
                this.lastProbability = results.output.data[0];
                
                if (results.stateN) {
                    this.state = new Float32Array(results.stateN.data);
                }
                
                return this.lastProbability > this.threshold;
            }
            
            return false;
            
        } catch (error) {
            console.error('VAD processing error:', error);
            return false;
        }
    }
    
    resetStates() {
        this.state.fill(0);
    }
    
    reset() {
        this.resetStates();
        this.audioBuffer = [];
        console.log('VAD state and buffer reset');
    }
    
    dispose() {
        if (this.session) {
            this.session.dispose();
            this.session = null;
            this.isReady = false;
        }
        this.audioBuffer = [];
        console.log('VAD disposed');
    }
    
    getInfo() {
        return {
            type: 'SileroVAD',
            threshold: this.threshold,
            lastProbability: this.lastProbability,
            isReady: this.isReady,
            chunkSize: this.chunkSize,
            sampleRate: this.sampleRate,
            bufferSize: this.audioBuffer.length
        };
    }
}
