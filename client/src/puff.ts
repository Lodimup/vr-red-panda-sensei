import * as THREE from 'three';

/** Cartoon smoke puff played where Sensei conjures an object: soft white blobs burst out, swell and fade. */
const LIFE_MS = 700;
const COUNT = 18;

interface Particle {sprite: THREE.Sprite; origin: THREE.Vector3; velocity: THREE.Vector3; born: number; size: number}

let sharedTexture: THREE.Texture | null = null;
function puffTexture(): THREE.Texture {
  if (sharedTexture) return sharedTexture;
  const canvas = typeof document !== 'undefined' ? document.createElement('canvas') : null;
  if (!canvas || !canvas.getContext('2d')) return (sharedTexture = new THREE.Texture());
  canvas.width = canvas.height = 64;
  const g = canvas.getContext('2d')!;
  const gradient = g.createRadialGradient(32, 32, 2, 32, 32, 32);
  gradient.addColorStop(0, 'rgba(255,255,255,1)');
  gradient.addColorStop(.5, 'rgba(236,232,226,.9)');
  gradient.addColorStop(.8, 'rgba(200,194,186,.45)');
  gradient.addColorStop(1, 'rgba(190,184,176,0)');
  g.fillStyle = gradient;
  g.fillRect(0, 0, 64, 64);
  return (sharedTexture = new THREE.CanvasTexture(canvas));
}

export class PuffSystem {
  private particles: Particle[] = [];
  constructor(private parent: THREE.Object3D, private random: () => number = Math.random) {}

  /** Start a puff centred on `at` (parent-local coordinates). */
  spawn(at: THREE.Vector3, now = performance.now()) {
    for (let i = 0; i < COUNT; i++) {
      const angle = (i / COUNT) * Math.PI * 2 + this.random() * .4;
      const speed = .18 + this.random() * .22;
      const velocity = new THREE.Vector3(Math.cos(angle) * speed, .08 + this.random() * .22, Math.sin(angle) * speed * .6);
      const material = new THREE.SpriteMaterial({map: puffTexture(), transparent: true, depthWrite: false, depthTest: false, opacity: 1});
      const sprite = new THREE.Sprite(material);
      sprite.renderOrder = 10; // over the object it reveals
      sprite.position.copy(at);
      const size = .08 + this.random() * .06;
      sprite.scale.setScalar(size);
      this.parent.add(sprite);
      this.particles.push({sprite, origin: at.clone(), velocity, born: now, size});
    }
  }

  /** Advance every particle; finished ones are removed and disposed. */
  update(now = performance.now()) {
    this.particles = this.particles.filter(p => {
      const t = (now - p.born) / LIFE_MS;
      if (t >= 1) {
        this.parent.remove(p.sprite);
        p.sprite.material.dispose();
        return false;
      }
      const eased = 1 - (1 - t) * (1 - t); // fast burst, slow drift
      const seconds = eased * LIFE_MS / 1000;
      p.sprite.position.copy(p.origin).addScaledVector(p.velocity, seconds);
      p.sprite.scale.setScalar(p.size * (1 + 2.2 * eased));
      p.sprite.material.opacity = Math.min(1, 1.6 * (1 - t)) * (1 - t * .3);
      return true;
    });
  }

  get active(): number { return this.particles.length; }
}
