import * as THREE from 'three';

export const loops = ['listen', 'think', 'speak', 'point', 'encourage'] as const;
export type Loop = typeof loops[number];

// Non-green bounds at 1 s in the 960 × 960 source frames, in pixels.
const bounds: Record<Loop, [number, number, number, number]> = {
  listen: [222, 118, 755, 914],
  think: [238, 111, 723, 913], speak: [200, 76, 745, 913],
  point: [174, 96, 825, 915], encourage: [248, 92, 713, 899],
};

export function poseLoop(pose: string): Loop {
  switch (pose) {
    case 'listen': case 'think': case 'speak': case 'point': case 'encourage': return pose;
    case 'demonstrate': return 'point';
    default: return 'listen'; // RULING(Handler 2026-10-31): idle uses the listen loop.
  }
}

export class SenseiVideo {
  readonly mesh: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial>;
  private videos = new Map<Loop, HTMLVideoElement>();
  private textures = new Map<Loop, THREE.VideoTexture>();
  private active: Loop | null = null;
  private generation = 0;

  constructor(private showVideo: (show: boolean) => void) {
    const material = new THREE.MeshBasicMaterial({transparent: true, depthWrite: false, side: THREE.DoubleSide});
    material.onBeforeCompile = shader => {
      shader.fragmentShader = shader.fragmentShader.replace('#include <map_fragment>', `#include <map_fragment>
        float greenExcess = diffuseColor.g - max(diffuseColor.r, diffuseColor.b);
        float foreground = 1.0 - smoothstep(0.025, 0.16, greenExcess);
        diffuseColor.a *= foreground;
        diffuseColor.g = min(diffuseColor.g, max(diffuseColor.r, diffuseColor.b) + 0.01);
      `);
    };
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), material);
    this.mesh.visible = false;
    for (const loop of loops) {
      const video = document.createElement('video');
      video.muted = true; video.loop = true; video.playsInline = true; video.preload = 'auto';
      video.src = `/sensei-loops/${loop}.mp4`;
      const texture = new THREE.VideoTexture(video);
      texture.colorSpace = THREE.SRGBColorSpace;
      this.videos.set(loop, video); this.textures.set(loop, texture);
      video.addEventListener('playing', () => { if (this.active === loop && !video.paused) this.showVideo(true); });
      for (const event of ['error', 'waiting']) video.addEventListener(event, () => { if (this.active === loop) this.showVideo(false); });
      video.load();
    }
  }

  retry() {
    if (this.active === null) return;
    const loop = this.active;
    this.active = null;
    this.setPose(loop);
  }

  setPose(pose: string) {
    const loop = poseLoop(pose);
    if (loop === this.active) return;
    this.active = loop;
    const generation = ++this.generation;
    this.showVideo(false);
    for (const video of this.videos.values()) video.pause();
    const video = this.videos.get(loop)!;
    const [left, top, right, bottom] = bounds[loop];
    const scale = .75 / (bottom - top + 1);
    this.mesh.scale.set(960 * scale, 960 * scale, 1);
    this.mesh.position.set((480 - (left + right) / 2) * scale, (bottom - 480) * scale, 0);
    this.mesh.material.map = this.textures.get(loop)!;
    this.mesh.material.needsUpdate = true;
    try {
      void video.play().then(() => {
        if (generation !== this.generation) { if (this.active !== loop) video.pause(); return; }
        if (!video.paused && video.readyState >= 2) this.showVideo(true);
      }).catch(() => { if (generation === this.generation) this.showVideo(false); });
    } catch { if (generation === this.generation) this.showVideo(false); }
  }
}
