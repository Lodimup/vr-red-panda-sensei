import type {Chunk, Line} from './types';

export class GenerationGuard {
  current = -1;
  interrupted = false;
  advance(id: number) {if (id > this.current) {this.current = id; this.interrupted = false; return true;} return false;}
  accepts(id: number) {return id === this.current && !this.interrupted;}
  stop(id: number) {if (id >= this.current) {this.current = id; this.interrupted = true;}}
  interrupt() {this.interrupted = true;}
  reset() {this.current = -1; this.interrupted = false;}
}

/** Schedules PCM on the audio clock so already received chunks meet without gaps. */
export class SpeechPlayer {
  readonly guard = new GenerationGuard();
  private context: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private sources = new Set<AudioBufferSourceNode>();
  private timers = new Set<ReturnType<typeof setTimeout>>();
  private queue: {chunk: Chunk; bytes: ArrayBuffer; line: Line}[] = [];
  private nextTime = 0;
  constructor(private caption: (line: Line) => void, private ended: (generation: number) => void) {}
  async unlock() {this.context ??= new AudioContext(); await this.context.resume(); this.schedule();}
  isPlaying() {return this.sources.size > 0;}
  level() {if (!this.sources.size || !this.analyser) return 0; const data = new Float32Array(this.analyser.fftSize); this.analyser.getFloatTimeDomainData(data); return Math.sqrt(data.reduce((sum, n) => sum + n*n, 0) / data.length);}
  advance(id: number) {if (!this.guard.advance(id)) return false; this.silence(); return true;}
  stop(id: number) {if (id >= this.guard.current) {this.guard.stop(id); this.silence();}}
  interrupt() {this.guard.interrupt(); this.silence();}
  allowRepeat() {this.guard.interrupted = false;}
  reset() {this.silence(); this.guard.reset();}
  enqueue(chunk: Chunk, bytes: ArrayBuffer, line: Line) {if (!this.guard.accepts(chunk.generation_id) || line.generation_id !== chunk.generation_id) return; this.queue.push({chunk, bytes, line}); this.schedule();}
  private silence() {
    this.queue = []; this.nextTime = 0;
    for (const timer of this.timers) clearTimeout(timer); this.timers.clear();
    for (const source of this.sources) {source.onended = null; try {source.stop();} catch {} source.disconnect();} this.sources.clear();
  }
  private schedule() {
    const context = this.context; if (!context || context.state !== 'running') return;
    while (this.queue.length) {
      const {chunk, bytes, line} = this.queue.shift()!;
      if (!this.guard.accepts(chunk.generation_id)) continue;
      const samples = Math.floor(bytes.byteLength / 2); if (!samples) continue;
      const buffer = context.createBuffer(1, samples, chunk.sample_rate), channel = buffer.getChannelData(0), data = new DataView(bytes);
      for (let i = 0; i < samples; i++) channel[i] = data.getInt16(i * 2, true) / 32768;
      this.analyser ??= context.createAnalyser(); this.analyser.fftSize = 2048; this.analyser.connect(context.destination);
      const source = context.createBufferSource(); source.buffer = buffer; source.connect(this.analyser); this.sources.add(source);
      const start = Math.max(context.currentTime, this.nextTime); this.nextTime = start + samples / chunk.sample_rate;
      const display = () => {if (this.guard.accepts(chunk.generation_id)) this.caption({...line, segments: [{lang: chunk.lang, text: chunk.text, reading: chunk.reading, romaji: chunk.romaji, translation: chunk.translation}]});};
      const delay = Math.max(0, (start - context.currentTime) * 1000);
      if (delay === 0) display(); else {const timer = setTimeout(() => {this.timers.delete(timer); display();}, delay); this.timers.add(timer);}
      source.onended = () => {this.sources.delete(source); source.disconnect(); if (!this.sources.size && !this.queue.length && this.guard.accepts(chunk.generation_id)) this.ended(chunk.generation_id);};
      source.start(start);
    }
  }
}
