import {afterEach,describe,expect,it,vi} from 'vitest';
import {captionRows,LinePresenter} from './captions';
import type {Line} from './types';

const line=(generation_id:number,text='りんご'):Line=>({type:'line',generation_id,segments:[{lang:'ja',text,reading:'りんご',romaji:'ringo',translation:'apple'},{lang:'en',text:'Hello'}],captions:{show_japanese:true,show_reading:false,show_romaji:true,show_translation:false}});
afterEach(()=>vi.useRealTimers());
describe('caption rows',()=>{
  it('preserves segment order, applies each event flag, and shows English',()=>{
    expect(captionRows(line(1))).toEqual([{text:'りんご',large:true},{text:'ringo',large:false},{text:'Hello',large:false}]);
    const all=line(1);all.captions={show_japanese:false,show_reading:true,show_romaji:false,show_translation:true};
    expect(captionRows(all)).toEqual([{text:'りんご',large:false},{text:'apple',large:false},{text:'Hello',large:false}]);
  });
});
describe('line presentation',()=>{
  it('replaces lines and drops stale or interrupted generations',()=>{
    const shown:(Line|null)[]=[];const p=new LinePresenter(value=>shown.push(value));
    p.show(line(1));p.start(1);p.show(line(1,'first'));p.show(line(1,'second'));p.start(2);p.show(line(1,'stale'));p.show(line(2,'new'));p.interrupt();p.show(line(2,'interrupted'));p.allowRepeat();p.show(line(2,'repeat'));p.start(3);p.show(line(3,'next'));
    expect(shown.map(x=>x?.segments[0].text??null)).toEqual(['first','second','new',null,'repeat','next']);
  });
  it('clears four seconds after the matching turn ends, unless a newer line arrives',()=>{
    vi.useFakeTimers();const shown:(Line|null)[]=[];const p=new LinePresenter(value=>shown.push(value));
    p.start(1);p.show(line(1));p.end(1);vi.advanceTimersByTime(3999);expect(shown.at(-1)).not.toBeNull();vi.advanceTimersByTime(1);expect(shown.at(-1)).toBeNull();
    p.show(line(1,'repeat'));p.end(1);vi.advanceTimersByTime(2000);p.show(line(1,'replacement'));vi.advanceTimersByTime(3000);expect(shown.at(-1)?.segments[0].text).toBe('replacement');
    p.end(1);p.start(2);p.show(line(2,'next turn'));p.end(1);vi.advanceTimersByTime(4000);expect(shown.at(-1)?.segments[0].text).toBe('next turn');
    p.end(2);vi.advanceTimersByTime(4000);expect(shown.at(-1)).toBeNull();
  });
  it('still clears an old line if a new turn starts without presenting one',()=>{
    vi.useFakeTimers();const shown:(Line|null)[]=[];const p=new LinePresenter(value=>shown.push(value));p.start(1);p.show(line(1));p.end(1);p.start(2);vi.advanceTimersByTime(4000);expect(shown.at(-1)).toBeNull();
  });
  it('resets on disconnect and accepts a reused generation in a new session',()=>{
    const shown:(Line|null)[]=[];const p=new LinePresenter(value=>shown.push(value));p.start(1);p.reset();p.show(line(1));p.start(1);p.show(line(1));expect(shown).toEqual([null,line(1)]);
  });
});
