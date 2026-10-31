import {afterEach, describe, expect, it, vi} from 'vitest';
import {loops, poseLoop, SenseiVideo} from './sensei-video';

class Video extends EventTarget {
  muted = false; loop = false; playsInline = false; preload = ''; src = '';
  paused = true; readyState = 2; rejected = false;
  load = vi.fn();
  pause = vi.fn(() => { this.paused = true; });
  play = vi.fn(() => {
    if (this.rejected) return Promise.reject(new Error('playback blocked'));
    this.paused = false;
    return Promise.resolve();
  });
}

afterEach(() => vi.unstubAllGlobals());

describe('video Sensei', () => {
  it('maps every supported pose to the intended loop', () => {
    expect(['idle_float', 'listen', 'think', 'speak', 'point', 'demonstrate', 'encourage', 'unknown'].map(poseLoop))
      .toEqual(['listen', 'listen', 'think', 'speak', 'point', 'point', 'encourage', 'listen']); // RULING(Handler 2026-10-31): idle uses the listen loop.
  });

  it('preloads all loops, plays only the selected video and falls back on failure', async () => {
    const videos: Video[] = [];
    vi.stubGlobal('document', {createElement: () => { const video = new Video(); videos.push(video); return video; }});
    const show = vi.fn();
    const sensei = new SenseiVideo(show);
    expect(loops).toEqual(['listen', 'think', 'speak', 'point', 'encourage']);
    expect(videos.map(v => v.src)).toEqual(loops.map(name => `/sensei-loops/${name}.mp4`));
    expect(videos.every(v => v.muted && v.loop && v.playsInline && v.preload === 'auto' && v.load.mock.calls.length === 1)).toBe(true);
    sensei.setPose('idle_float'); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(true);
    expect(videos.filter(v => !v.paused)).toEqual([videos[0]]);
    sensei.setPose('listen'); await Promise.resolve();
    expect(videos[0].play).toHaveBeenCalledTimes(1);
    expect(videos.filter(v => !v.paused)).toEqual([videos[0]]);
    videos[0].dispatchEvent(new Event('error'));
    expect(show).toHaveBeenLastCalledWith(false);
    videos[1].rejected = true;
    sensei.setPose('think'); await Promise.resolve(); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(false);
    videos[1].rejected = false;
    sensei.retry(); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(true);
    expect(videos.filter(v => !v.paused)).toEqual([videos[1]]);
  });
});
