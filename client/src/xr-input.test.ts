import {describe, expect, it, vi} from 'vitest';
import {XRInput} from './xr-input';
function source(handedness: 'left'|'right', pressed: number[] = []): XRInputSource {
  return {handedness, gamepad: {buttons: Array.from({length: 6}, (_, i) => ({pressed: pressed.includes(i)}))}} as unknown as XRInputSource;
}
describe('XR inputs', () => {
  it('maps controller edges and releases holds on disconnect', () => {
    const a={talkStart:vi.fn(),talkEnd:vi.fn(),repeat:vi.fn(),hint:vi.fn(),menu:vi.fn(),grabStart:vi.fn(),grabEnd:vi.fn(),grabCancel:vi.fn()};const input=new XRInput(a);
    input.poll(0,source('right',[1,4,5]));input.poll(0,source('right',[1,4,5]));
    expect(a.talkStart).toHaveBeenCalledTimes(1);expect(a.repeat).toHaveBeenCalledTimes(1);expect(a.grabStart).toHaveBeenCalledWith(0);
    input.poll(1,source('left',[4,5]));expect(a.hint).toHaveBeenCalledTimes(1);expect(a.menu).toHaveBeenCalledTimes(1);
    input.disconnect(0);expect(a.talkEnd).toHaveBeenCalledTimes(1);expect(a.grabCancel).toHaveBeenCalledWith(0);expect(a.grabEnd).not.toHaveBeenCalled();
  });
  it('keeps talking until both hand pinches end', () => {
    const a={talkStart:vi.fn(),talkEnd:vi.fn(),repeat:vi.fn(),hint:vi.fn(),menu:vi.fn(),grabStart:vi.fn(),grabEnd:vi.fn(),grabCancel:vi.fn()};const input=new XRInput(a);
    input.hand(0,true);input.hand(1,true);input.hand(0,false);expect(a.talkEnd).not.toHaveBeenCalled();input.hand(1,false);
    expect(a.talkStart).toHaveBeenCalledTimes(1);expect(a.talkEnd).toHaveBeenCalledTimes(1);
  });
});
