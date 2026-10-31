import {describe, expect, it} from 'vitest';
import * as THREE from 'three';
import {createEnvironment} from './environment';

describe('desktop environment', () => {
  it('keeps the floor when the CDN fails', async () => {
    const environment = createEnvironment(async () => { throw new Error('offline'); });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(environment.children.filter(child => child instanceof THREE.Mesh)).toHaveLength(1);
    expect(environment.children[0].visible).toBe(true);
  });

  it('keeps late-loading props hidden with the floor in AR', async () => {
    let resolve!: (module: {createAsset: () => THREE.Group}) => void;
    const pending = new Promise<{createAsset: () => THREE.Group}>(done => { resolve = done; });
    const environment = createEnvironment(() => pending);
    environment.visible = false;
    resolve({createAsset: () => new THREE.Group()});
    await pending;
    await Promise.resolve();
    const props = environment.children.filter(child => child instanceof THREE.Group);
    expect(props).toHaveLength(4);
    expect(props.every(prop => prop.parent === environment && !prop.parent.visible)).toBe(true);
    environment.visible = true;
    expect(props.every(prop => prop.parent?.visible)).toBe(true);
  });
});
