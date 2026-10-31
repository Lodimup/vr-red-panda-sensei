import * as THREE from 'three';

type AssetModule = {createAsset: () => THREE.Group};
type AssetLoader = (url: string) => Promise<AssetModule>;

const props = [
  {url: 'https://polyfork.dev/cdn/andon-floor-lantern-b1b7ea.mjs', x: -2, z: -.9},
  {url: 'https://polyfork.dev/cdn/floor-houseplant-8fad17.mjs', x: 1.35, z: -1.1},
  {url: 'https://polyfork.dev/cdn/coffee-table-acc698.mjs', x: 1.65, z: -.35},
  {url: 'https://polyfork.dev/cdn/bookshelf-5e881a.mjs', x: 1.65, z: -.35, y: .4},
] as const;

// Native imports keep the asset programs on Polyfork's CDN, outside the bundle.
const loadAsset: AssetLoader = url => import(/* @vite-ignore */ url);

export function createEnvironment(load: AssetLoader = loadAsset): THREE.Group {
  const environment = new THREE.Group();
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(8, 8), new THREE.MeshBasicMaterial({color: 0xd9d0c1}));
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = -.01;
  environment.add(floor, new THREE.HemisphereLight(0xffffff, 0x998877, 1.6));
  const light = new THREE.DirectionalLight(0xfff2e0, 1.5);
  light.position.set(4, 7, 5);
  environment.add(light);

  for (const prop of props) {
    void load(prop.url).then(module => {
      const object = module.createAsset();
      object.position.set(prop.x, 'y' in prop ? prop.y : 0, prop.z);
      environment.add(object);
    }).catch(() => {
      // The plain floor remains usable if the CDN or an individual asset fails.
    });
  }
  return environment;
}
