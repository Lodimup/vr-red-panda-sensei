import {describe, expect, it} from 'vitest';
import * as THREE from 'three';
import {PuffSystem} from './puff';
describe('spawn puff', () => {
  it('bursts outward, grows, fades and cleans up after its life', () => {
    const parent = new THREE.Group(); const puffs = new PuffSystem(parent, () => .5);
    puffs.spawn(new THREE.Vector3(1, .9, 0), 0);
    expect(puffs.active).toBe(18); expect(parent.children.length).toBe(18);
    const first = parent.children[0] as THREE.Sprite; const startScale = first.scale.x;
    puffs.update(350);
    expect(first.position.distanceTo(new THREE.Vector3(1, .9, 0))).toBeGreaterThan(.05);
    expect(first.scale.x).toBeGreaterThan(startScale); expect(first.material.opacity).toBeLessThan(1);
    puffs.update(700);
    expect(puffs.active).toBe(0); expect(parent.children.length).toBe(0);
  });
});
