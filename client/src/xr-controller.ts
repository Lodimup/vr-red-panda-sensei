import * as THREE from 'three';

/** A small, depth-tested controller attached to an XR grip space. */
export function controllerModel(): THREE.Group {
  const group = new THREE.Group();
  const body = new THREE.Mesh(new THREE.CapsuleGeometry(.025,.075,4,12), new THREE.MeshBasicMaterial({color:0x75482e}));
  body.rotation.x = -.28; group.add(body);
  const ring = new THREE.Mesh(new THREE.TorusGeometry(.035,.004,8,24),new THREE.MeshBasicMaterial({color:0xf2ddc0}));
  ring.rotation.x = Math.PI/2; ring.position.set(0,.052,-.018); group.add(ring);
  const stick = new THREE.Mesh(new THREE.SphereGeometry(.009,12,8),new THREE.MeshBasicMaterial({color:0x302a24}));
  stick.position.set(0,.052,-.018);group.add(stick);
  const trigger = new THREE.Mesh(new THREE.BoxGeometry(.018,.018,.012),new THREE.MeshBasicMaterial({color:0xf2ddc0}));
  trigger.position.set(0,.014,-.029);group.add(trigger);
  return group;
}
export function controllerRay(): THREE.Line {
  const geometry=new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(),new THREE.Vector3(0,0,-2)]);
  return new THREE.Line(geometry,new THREE.LineBasicMaterial({color:0xf2ddc0,transparent:true,opacity:.75}));
}
