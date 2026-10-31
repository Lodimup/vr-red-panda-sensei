import * as THREE from 'three';

export const loops = ['listen', 'think', 'speak', 'point', 'encourage'] as const;
export type Loop = typeof loops[number];
const fadeMs = 300;

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

type Player = {video: HTMLVideoElement; texture: THREE.VideoTexture; loop: Loop};
type Layer = {mesh: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial>; player: Player | null};

export class SenseiVideo {
  readonly mesh = new THREE.Group();
  private players = new Map<Loop, [Player, Player]>();
  private layers: [Layer, Layer];
  private current: Layer | null = null;
  private incoming: Layer | null = null;
  private desired: Loop | null = null;
  private fadeStart: number | null = null;
  private generation = 0;

  constructor(private showVideo: (show: boolean) => void) {
    const geometry = new THREE.PlaneGeometry(1, 1);
    const makeLayer = (): Layer => {
      const material = new THREE.MeshBasicMaterial({transparent: true, depthWrite: false, side: THREE.DoubleSide});
      material.onBeforeCompile = shader => {
        shader.fragmentShader = shader.fragmentShader.replace('#include <map_fragment>', `#include <map_fragment>
          float greenExcess = diffuseColor.g - max(diffuseColor.r, diffuseColor.b);
          float matte = 1.0 - smoothstep(0.008, 0.085, greenExcess);
          // Inset the matte by less than one source pixel to remove contaminated rims.
          vec2 inset = vec2(0.75 / 960.0, 0.0);
          vec4 n1 = texture2D(map, vMapUv + inset.xy);
          vec4 n2 = texture2D(map, vMapUv - inset.xy);
          vec4 n3 = texture2D(map, vMapUv + inset.yx);
          vec4 n4 = texture2D(map, vMapUv - inset.yx);
          #ifdef DECODE_VIDEO_TEXTURE
            n1 = sRGBTransferEOTF(n1); n2 = sRGBTransferEOTF(n2);
            n3 = sRGBTransferEOTF(n3); n4 = sRGBTransferEOTF(n4);
          #endif
          float edgeGreen = max(max(n1.g - max(n1.r, n1.b), n2.g - max(n2.r, n2.b)),
                                max(n3.g - max(n3.r, n3.b), n4.g - max(n4.r, n4.b)));
          matte = min(matte, 1.0 - smoothstep(0.008, 0.085, edgeGreen));
          diffuseColor.a *= matte;
          float edgeSpill = smoothstep(0.0, 0.085, edgeGreen);
          float furGreen = 0.5 * (diffuseColor.r + diffuseColor.b);
          diffuseColor.g = min(diffuseColor.g, mix(max(diffuseColor.r, diffuseColor.b), furGreen, edgeSpill));
        `);
      };
      const mesh = new THREE.Mesh(geometry, material);
      mesh.visible = false;
      this.mesh.add(mesh);
      return {mesh, player: null};
    };
    this.layers = [makeLayer(), makeLayer()];
    this.mesh.visible = false;
    for (const loop of loops) {
      const makePlayer = (): Player => {
        const video = document.createElement('video');
        video.muted = true; video.loop = true; video.playsInline = true; video.preload = 'auto';
        video.src = `/sensei-loops/${loop}.mp4`;
        const texture = new THREE.VideoTexture(video);
        texture.colorSpace = THREE.SRGBColorSpace;
        const player = {video, texture, loop};
        for (const event of ['error', 'waiting']) video.addEventListener(event, () => {
          if (this.current?.player === player && !this.incoming) this.showVideo(false);
        });
        video.addEventListener('playing', () => {
          if (this.current?.player === player && !video.paused) this.showVideo(true);
        });
        video.load();
        return player;
      };
      this.players.set(loop, [makePlayer(), makePlayer()]);
    }
  }

  retry() {
    if (this.desired === null) return;
    const loop = this.desired;
    this.desired = null;
    this.setPose(loop);
  }

  setPose(pose: string) {
    const loop = poseLoop(pose);
    if (loop === this.desired) return;
    this.desired = loop;
    this.start(loop);
  }

  private clear(layer: Layer) {
    layer.player?.video.pause();
    layer.player = null;
    layer.mesh.visible = false;
  }

  private start(loop: Loop) {
    const generation = ++this.generation;
    // On interruption retain the more visible layer to minimize the visual jump.
    if (this.incoming) {
      if (this.fadeStart !== null && performance.now() - this.fadeStart >= fadeMs / 2) {
        if (this.current) this.clear(this.current);
        this.current = this.incoming;
        this.current.mesh.material.opacity = 1;
      } else this.clear(this.incoming);
      this.incoming = null;
      this.fadeStart = null;
    }
    const layer = this.layers.find(item => item !== this.current)!;
    const player = this.players.get(loop)!.find(item => item !== this.current?.player)!;
    const video = player.video;
    layer.player = player;
    const [left, top, right, bottom] = bounds[loop];
    const scale = .75 / (bottom - top + 1);
    layer.mesh.scale.set(960 * scale, 960 * scale, 1);
    layer.mesh.position.set((480 - (left + right) / 2) * scale, (bottom - 480) * scale, 0);
    layer.mesh.material.map = player.texture;
    layer.mesh.material.needsUpdate = true;
    layer.mesh.material.opacity = this.current ? 0 : 1;
    layer.mesh.renderOrder = 1;
    if (this.current) this.current.mesh.renderOrder = 0;
    this.incoming = layer;
    try {
      video.currentTime = 0;
      void video.play().then(() => {
        if (generation !== this.generation) return;
        if (video.paused) return;
        layer.mesh.visible = true;
        if (this.current) this.fadeStart = performance.now();
        else { this.current = layer; this.incoming = null; }
        if (video.readyState >= 2) this.showVideo(true);
      }).catch(() => {
        if (generation !== this.generation) return;
        this.clear(layer); this.incoming = null; this.showVideo(false);
      });
    } catch {
      if (generation === this.generation) { this.clear(layer); this.incoming = null; this.showVideo(false); }
    }
  }

  update(now: number) {
    if (this.incoming && this.fadeStart !== null) {
      const progress = Math.min(1, Math.max(0, (now - this.fadeStart) / fadeMs));
      this.incoming.mesh.material.opacity = progress;
      if (progress === 1) {
        if (this.current) this.clear(this.current);
        this.current = this.incoming;
        this.incoming = null;
        this.fadeStart = null;
      }
    }
    const video = this.current?.player?.video;
    if (!this.incoming && video && !video.paused && Number.isFinite(video.duration) &&
        video.duration > fadeMs / 1000 && video.currentTime >= video.duration - fadeMs / 1000) {
      this.start(this.current!.player!.loop);
    }
  }
}
