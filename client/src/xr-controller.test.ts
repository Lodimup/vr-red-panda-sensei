import type * as THREE from 'three';
import {describe, expect, it} from 'vitest';
import {controllerModel, controllerRay} from './xr-controller';
describe('XR controller visuals', () => {
  it('uses depth-tested geometry for the grip and pointing ray', () => {
    const model=controllerModel();
    expect(model.children).toHaveLength(4);
    for(const mesh of model.children) expect((mesh as THREE.Mesh).material).toHaveProperty('depthTest',true);
    expect((controllerRay().material as THREE.Material).depthTest).toBe(true);
  });
});
