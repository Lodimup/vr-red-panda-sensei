import type { Page } from 'playwright';
import { strict as assert } from 'node:assert';

type Hand = 'left' | 'right';
type Button = 'a' | 'b' | 'x' | 'y' | 'grip' | 'trigger';

export class XR {
  constructor(private page: Page) {}
  async startAR() {
    return this.page.evaluate(async () => {
      const xr = (navigator as Navigator & { xr: { requestSession(mode: string, options: object): Promise<{ end(): Promise<void> }> } }).xr;
      const session = await xr.requestSession('immersive-ar', { optionalFeatures: ['local-floor', 'hand-tracking'] });
      const canvas = document.createElement('canvas');
      const gl = canvas.getContext('webgl', { xrCompatible: true } as WebGLContextAttributes);
      if (!gl) throw new Error('WebGL unavailable for XR session');
      (session as any).updateRenderState({baseLayer: new (window as any).XRWebGLLayer(session, gl)});
      const events: string[] = []; for (const type of ['selectstart','selectend','squeezestart','squeezeend']) (session as any).addEventListener(type,(e:any)=>events.push(`${type}:${e.inputSource.handedness}`));
      Object.assign(window, { __testSession: session, __xrEvents: events });
      return true;
    });
  }
  private async frame() { await this.page.evaluate(() => new Promise<void>((resolve,reject) => { const timer=setTimeout(() => reject(new Error('XR frame timed out')), 5000); (window as any).__testSession.requestAnimationFrame(() => {clearTimeout(timer);resolve();}); })); }
  async events() { return this.page.evaluate(() => (window as any).__xrEvents as string[]); }
  async endAR() { await this.page.evaluate(async () => { const s = (window as any).__testSession; if (s) await s.end(); }); }
  async headPose(position: [number, number, number], quaternion: [number, number, number, number]) {
    await this.page.evaluate(({position, quaternion}) => { const d = (window as any).__senseiXR; d.position.set(...position); d.quaternion.set(...quaternion); }, { position, quaternion });
  }
  async inputMode(mode: 'hand' | 'controller') { await this.page.evaluate(mode => { (window as any).__senseiXR.primaryInputMode = mode; }, mode); }
  async button(hand: Hand, button: Button, pressed: boolean) {
    const allowed = button === 'a' || button === 'b' ? 'right' : button === 'x' || button === 'y' ? 'left' : hand;
    assert.equal(hand, allowed, `${button} is not on the ${hand} controller`);
    const key = button === 'grip' ? 'squeeze' : ['a','b','x','y'].includes(button) ? `${button}-button` : button;
    await this.page.evaluate(({ hand, key, pressed }) => (window as any).__senseiXR.controllers[hand].updateButtonValue(key, pressed ? 1 : 0), { hand, key, pressed });
    await this.frame();
  }
  async pinch(hand: Hand, pressed: boolean) { await this.page.evaluate(({hand, pressed}) => (window as any).__senseiXR.hands[hand].updatePinchValue(pressed ? 1 : 0), {hand, pressed}); await this.frame(); }
  async handPose(hand: Hand, position: [number, number, number], quaternion: [number, number, number, number]) {
    await this.page.evaluate(({hand, position, quaternion}) => { const h = (window as any).__senseiXR.hands[hand]; h.position.set(...position); h.quaternion.set(...quaternion); }, {hand, position, quaternion});
  }
  async controllerPose(hand: Hand, position: [number, number, number], quaternion: [number, number, number, number]) {
    await this.page.evaluate(({hand, position, quaternion}) => { const c = (window as any).__senseiXR.controllers[hand]; c.position.set(...position); c.quaternion.set(...quaternion); }, {hand, position, quaternion});
  }
}

export async function audioMark(page: Page): Promise<number> { return page.evaluate(() => performance.now()); }
export async function assertAudio(page: Page, start: number, durationMs: number, threshold = 0.001) {
  await page.waitForTimeout(durationMs);
  const result = await page.evaluate(({start, durationMs}) => {
    const samples = (window as any).__senseiAudio.samples as {time: number; peak: number}[];
    const inWindow = samples.filter(s => s.time >= start && s.time <= start + durationMs);
    return { count: inWindow.length, peak: Math.max(0, ...inWindow.map(s => s.peak)) };
  }, {start, durationMs});
  assert.ok(result.count > 0, 'audio tap collected no samples');
  assert.ok(result.peak > threshold, `silent output: peak ${result.peak}, required > ${threshold}`);
  return result;
}
