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
      .toEqual(['idle', 'listen', 'think', 'speak', 'point', 'point', 'encourage', 'idle']);
  });

  it('preloads all loops, plays only the selected video and falls back on failure', async () => {
    const videos: Video[] = [];
    vi.stubGlobal('document', {createElement: () => { const video = new Video(); videos.push(video); return video; }});
    const show = vi.fn();
    const sensei = new SenseiVideo(show);
    expect(videos.map(v => v.src)).toEqual(loops.map(name => `/sensei-loops/${name}.mp4`));
    expect(videos.every(v => v.muted && v.loop && v.playsInline && v.preload === 'auto' && v.load.mock.calls.length === 1)).toBe(true);
    sensei.setPose('idle_float'); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(true);
    sensei.setPose('listen'); await Promise.resolve();
    expect(videos.filter(v => !v.paused)).toEqual([videos[1]]);
    videos[1].dispatchEvent(new Event('error'));
    expect(show).toHaveBeenLastCalledWith(false);
    videos[2].rejected = true;
    sensei.setPose('think'); await Promise.resolve(); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(false);
    videos[2].rejected = false;
    sensei.retry(); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(true);
    expect(videos.filter(v => !v.paused)).toEqual([videos[2]]);
  });
});
