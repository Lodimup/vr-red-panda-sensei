import type {Chunk} from './types';
export function captionLines(chunk: Chunk): string[] {
  if(chunk.lang==='en') return [chunk.text];
  return [chunk.captions.show_japanese?chunk.text:'',chunk.captions.show_reading?chunk.reading:'',chunk.captions.show_romaji?chunk.romaji:'',chunk.captions.show_translation?chunk.translation:''].filter((s): s is string=>!!s);
}
export class GenerationGuard {
  current = -1; interrupted = false;
  advance(id:number) {if(id>this.current){this.current=id;this.interrupted=false; return true;} return false;}
  accepts(id:number) {return id===this.current&&!this.interrupted;}
  stop(id:number) {if(id>=this.current){this.current=id;this.interrupted=true;}}
  interrupt(){this.interrupted=true;}
}
export class SpeechPlayer {
  readonly guard=new GenerationGuard();
  private context: AudioContext|null=null; private analyser: AnalyserNode|null=null; private source: AudioBufferSourceNode|null=null; private queue: {chunk:Chunk; bytes:ArrayBuffer}[]=[]; private clearing: ReturnType<typeof setTimeout>|null=null;
  constructor(private caption:(chunk:Chunk|null)=>void, private activity:(playing:boolean)=>void) {}
  async unlock() {this.context ??= new AudioContext(); await this.context.resume(); this.playNext();}
  level(){if(!this.source||!this.analyser)return 0;const data=new Float32Array(this.analyser.fftSize);this.analyser.getFloatTimeDomainData(data);return Math.sqrt(data.reduce((sum,n)=>sum+n*n,0)/data.length);}
  advance(id:number){if(this.guard.advance(id)){this.silence();}}
  stop(id:number){if(id>=this.guard.current){this.guard.stop(id);this.silence();}}
  interrupt(){this.guard.interrupt();this.silence();}
  enqueue(chunk:Chunk,bytes:ArrayBuffer){if(!this.guard.accepts(chunk.generation_id))return; this.queue.push({chunk,bytes});this.playNext();}
  private silence(){this.queue=[]; if(this.source){const source=this.source; this.source=null; source.onended=null; try{source.stop();}catch{} } this.analyser?.disconnect();this.activity(false);this.caption(null);if(this.clearing)clearTimeout(this.clearing);}
  private playNext(){
    if(!this.context||this.context.state!=='running'||this.source) return;
    let item=this.queue.shift(); while(item&&!this.guard.accepts(item.chunk.generation_id))item=this.queue.shift(); if(!item)return;
    if(this.clearing)clearTimeout(this.clearing);
    const {chunk,bytes}=item; const data=new DataView(bytes); const samples=Math.floor(bytes.byteLength/2); if(!samples){this.playNext();return;}
    const buffer=this.context.createBuffer(1,samples,chunk.sample_rate); const channel=buffer.getChannelData(0); for(let i=0;i<samples;i++)channel[i]=data.getInt16(i*2,true)/32768;
    const source=this.context.createBufferSource();source.buffer=buffer;this.analyser ??= this.context.createAnalyser();this.analyser.fftSize=2048;source.connect(this.analyser);this.analyser.connect(this.context.destination);this.source=source;this.caption(chunk);this.activity(true);
    source.onended=()=>{if(this.source!==source)return; this.source=null;this.analyser?.disconnect();this.activity(false); if(this.queue.length)this.playNext();else this.clearing=setTimeout(()=>this.caption(null),4000);};source.start();
  }
}
