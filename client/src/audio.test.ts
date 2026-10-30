import {describe,it,expect} from 'vitest';
import {GenerationGuard,captionLines} from './audio';
import type {Chunk} from './types';
describe('generation guard',()=>{it('drops stale and interrupted chunks until a newer generation',()=>{const g=new GenerationGuard();g.advance(2);expect(g.accepts(1)).toBe(false);expect(g.accepts(2)).toBe(true);g.interrupt();expect(g.accepts(2)).toBe(false);g.advance(3);g.stop(2);expect(g.accepts(3)).toBe(true);g.stop(3);expect(g.accepts(3)).toBe(false);});});
describe('captions',()=>{it('uses the current chunk visibility and always shows English text',()=>{const c={lang:'ja',text:'りんご',reading:'りんご',romaji:'ringo',translation:'apple',captions:{show_japanese:true,show_reading:false,show_romaji:true,show_translation:false}} as Chunk;expect(captionLines(c)).toEqual(['りんご','ringo']);expect(captionLines({...c,lang:'en',text:'Hello'})).toEqual(['Hello']);});});

describe('speech playback',()=>{
  it('updates captions when each queued chunk starts and stops the current source on interruption',async()=>{
    const sources: {buffer: unknown; onended: null|(()=>void); start:()=>void; stop:()=>void; connect:()=>void; stopped:boolean}[]=[];
    class FakeContext {state='running';destination={};resume=async()=>{};createAnalyser(){return {fftSize:2048,connect(){},disconnect(){},getFloatTimeDomainData(){}};}createBuffer(_channels:number,length:number){return {getChannelData:()=>new Float32Array(length)};}createBufferSource(){const source={buffer:null,onended:null as null|(()=>void),stopped:false,start(){},stop(){this.stopped=true;},connect(){}};sources.push(source);return source;}}
    const original=globalThis.AudioContext;globalThis.AudioContext=FakeContext as unknown as typeof AudioContext;
    try {const {SpeechPlayer}=await import('./audio');const shown: (Chunk|null)[]=[];const player=new SpeechPlayer(c=>shown.push(c),()=>{});await player.unlock();player.advance(1);shown.length=0;const base={generation_id:1,lang:'en',text:'First',sample_rate:24000,captions:{},final:false} as Chunk;player.enqueue(base,new Int16Array([100]).buffer);player.enqueue({...base,text:'Second'},new Int16Array([100]).buffer);expect(shown.map(c=>c?.text)).toEqual(['First']);sources[0].onended?.();expect(shown.map(c=>c?.text)).toEqual(['First','Second']);player.interrupt();expect(sources[1].stopped).toBe(true);expect(shown.at(-1)).toBeNull();player.enqueue(base,new Int16Array([100]).buffer);expect(sources).toHaveLength(2);}finally{globalThis.AudioContext=original;}
  });
});
