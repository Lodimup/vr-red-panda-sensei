import {afterEach, describe, expect, it, vi} from 'vitest';
import {loops, poseLoop, SenseiVideo} from './sensei-video';

class Video extends EventTarget {
  muted = false; loop = false; playsInline = false; preload = ''; src = '';
  paused = true; readyState = 2; rejected = false; currentTime = 0; duration = 10;
  load = vi.fn();
  pause = vi.fn(() => { this.paused = true; });
  play = vi.fn(() => {
    if (this.rejected) return Promise.reject(new Error('playback blocked'));
    this.paused = false;
    return Promise.resolve();
  });
}

function setup() {
  const videos: Video[] = [];
  vi.stubGlobal('document', {createElement: () => { const video = new Video(); videos.push(video); return video; }});
  const show = vi.fn();
  return {videos, show, sensei: new SenseiVideo(show)};
}

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe('video Sensei', () => {
  it('maps every supported pose to the intended loop', () => {
    expect(['idle_float', 'listen', 'think', 'speak', 'point', 'demonstrate', 'encourage', 'unknown'].map(poseLoop))
      .toEqual(['listen', 'listen', 'think', 'speak', 'point', 'point', 'encourage', 'listen']); // RULING(Handler 2026-10-31): idle uses the listen loop.
  });

  it('cross-dissolves pose changes immediately and pauses the outgoing player after 300 ms', async () => {
    vi.spyOn(performance, 'now').mockReturnValue(1000);
    const {videos, show, sensei} = setup();
    expect(videos.map(v => v.src)).toEqual(loops.flatMap(name => Array(2).fill(`/sensei-loops/${name}.mp4`)));
    expect(videos.every(v => v.muted && v.loop && v.playsInline && v.preload === 'auto' && v.load.mock.calls.length === 1)).toBe(true);
    sensei.setPose('idle_float'); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(true);
    sensei.setPose('listen');
    expect(videos[0].play).toHaveBeenCalledTimes(1);
    sensei.setPose('speak'); await Promise.resolve();
    expect(videos.filter(v => !v.paused)).toEqual([videos[0], videos[4]]);
    const layers = sensei.mesh.children as import('three').Mesh<import('three').PlaneGeometry, import('three').MeshBasicMaterial>[];
    sensei.update(1150);
    expect(layers[1].material.opacity).toBeCloseTo(.5);
    sensei.update(1300);
    expect(videos.filter(v => !v.paused)).toEqual([videos[4]]);
    expect(layers.filter(layer => layer.visible)).toHaveLength(1);
    expect(layers[1].material.opacity).toBe(1);
  });

  it('overlaps the same clip before its loop seam', async () => {
    vi.spyOn(performance, 'now').mockReturnValue(2000);
    const {videos, sensei} = setup();
    sensei.setPose('listen'); await Promise.resolve();
    videos[0].currentTime = 9.7;
    sensei.update(2000); await Promise.resolve();
    expect(videos.filter(v => !v.paused)).toEqual([videos[0], videos[1]]);
    expect(videos[1].currentTime).toBe(0);
    sensei.update(2300);
    expect(videos.filter(v => !v.paused)).toEqual([videos[1]]);
  });

  it('handles interrupted fades and playback failure, then retries', async () => {
    vi.spyOn(performance, 'now').mockReturnValue(1000);
    const {videos, show, sensei} = setup();
    sensei.setPose('listen'); await Promise.resolve();
    sensei.setPose('think'); await Promise.resolve();
    sensei.setPose('speak'); await Promise.resolve();
    expect(videos.filter(v => !v.paused)).toEqual([videos[0], videos[4]]);
    sensei.update(1300);
    expect(videos.filter(v => !v.paused)).toEqual([videos[4]]);
    videos[6].rejected = true;
    sensei.setPose('point'); await Promise.resolve(); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(false);
    videos[6].rejected = false;
    sensei.retry(); await Promise.resolve();
    expect(show).toHaveBeenLastCalledWith(true);
    sensei.update(1300);
    expect(videos.filter(v => !v.paused)).toEqual([videos[6]]);
    videos[6].dispatchEvent(new Event('error'));
    expect(show).toHaveBeenLastCalledWith(false);
  });
});
