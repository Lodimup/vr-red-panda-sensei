import {afterEach, describe, expect, it, vi} from 'vitest';
import {GenerationGuard, SpeechPlayer} from './audio';
import type {Chunk, Line} from './types';

const chunk=(generation_id:number,text='First'):Chunk=>({type:'speak_chunk',utterance_id:'u1',generation_id,segment_index:0,chunk_index:0,lang:'en',text,format:'pcm_s16le',sample_rate:24000,channels:1,final:false});
const line=(generation_id:number):Line=>({type:'line',generation_id,segments:[{lang:'en',text:'utterance'}],captions:{show_japanese:true,show_reading:false,show_romaji:false,show_translation:true}});
const pcm=new Int16Array(24000).buffer;
let sources: FakeSource[]=[];
class FakeSource {
  buffer: unknown; onended: null|(()=>void)=null; stopped=false; startTime=-1;
  connect() {} disconnect() {} start(time:number) {this.startTime=time;} stop() {this.stopped=true;}
}
class FakeContext {
  state='running'; currentTime=10; destination={};
  resume=async()=>{};
  createAnalyser(){return {fftSize:2048,connect(){},getFloatTimeDomainData(data:Float32Array){data.fill(.1);}};}
  createBuffer(_channels:number,length:number){return {getChannelData:()=>new Float32Array(length)};}
  createBufferSource(){const source=new FakeSource();sources.push(source);return source;}
}
const original=globalThis.AudioContext;
afterEach(()=>{globalThis.AudioContext=original;vi.useRealTimers();sources=[];});

describe('generation guard',()=>{
  it('drops stale and interrupted chunks until a newer generation',()=>{const g=new GenerationGuard();g.advance(2);expect(g.accepts(1)).toBe(false);expect(g.accepts(2)).toBe(true);g.interrupt();expect(g.accepts(2)).toBe(false);g.advance(3);g.stop(2);expect(g.accepts(3)).toBe(true);g.stop(3);expect(g.accepts(3)).toBe(false);g.reset();g.advance(1);expect(g.accepts(1)).toBe(true);});
});
describe('speech playback',()=>{
  it('schedules adjacent PCM buffers on the audio clock and displays each sentence when it starts',async()=>{
    vi.useFakeTimers();globalThis.AudioContext=FakeContext as unknown as typeof AudioContext;
    const shown:Line[]=[];const ended:number[]=[];const player=new SpeechPlayer(c=>shown.push(c),id=>ended.push(id));
    await player.unlock();player.advance(1);player.enqueue(chunk(1),pcm,line(1));player.enqueue(chunk(1,'Second'),pcm,line(1));
    expect(sources.map(s=>s.startTime)).toEqual([10,11]);expect(shown.map(c=>c.segments[0].text)).toEqual(['First']);expect(player.level()).toBeCloseTo(.1);
    vi.advanceTimersByTime(1000);expect(shown.map(c=>c.segments[0].text)).toEqual(['First','Second']);
    sources[0].onended?.();expect(ended).toEqual([]);sources[1].onended?.();expect(ended).toEqual([1]);
  });
  it('stops scheduled sources immediately and rejects old, interrupted, and mismatched chunks',async()=>{
    vi.useFakeTimers();globalThis.AudioContext=FakeContext as unknown as typeof AudioContext;
    const shown:Line[]=[];const player=new SpeechPlayer(c=>shown.push(c),()=>{});await player.unlock();player.advance(2);
    player.enqueue(chunk(1),pcm,line(1));player.enqueue(chunk(2),pcm,line(1));expect(sources).toHaveLength(0);
    player.enqueue(chunk(2),pcm,line(2));player.enqueue(chunk(2,'Queued'),pcm,line(2));player.interrupt();
    expect(sources.every(s=>s.stopped)).toBe(true);expect(player.level()).toBe(0);vi.advanceTimersByTime(2000);expect(shown.map(c=>c.segments[0].text)).toEqual(['First']);
    player.enqueue(chunk(2),pcm,line(2));expect(sources).toHaveLength(2);player.advance(3);player.stop(2);player.enqueue(chunk(3),pcm,line(3));expect(sources).toHaveLength(3);player.stop(3);expect(sources[2].stopped).toBe(true);
  });
  it('queues until audio is unlocked and accepts a new session after reset',async()=>{
    globalThis.AudioContext=FakeContext as unknown as typeof AudioContext;const shown:Line[]=[];const player=new SpeechPlayer(c=>shown.push(c),()=>{});player.advance(9);player.enqueue(chunk(9),pcm,line(9));expect(sources).toHaveLength(0);await player.unlock();expect(sources).toHaveLength(1);player.reset();expect(sources[0].stopped).toBe(true);player.advance(1);player.enqueue(chunk(1),pcm,line(1));expect(shown.map(c=>c.generation_id)).toEqual([9,1]);
  });
});
