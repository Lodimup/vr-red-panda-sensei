declare module 'troika-three-text' {
  import {Object3D, Material} from 'three';
  export class Text extends Object3D {material:Material; font:string; fontSize:number; maxWidth:number; textAlign:string; anchorX:string; anchorY:string; color:number; text:string; sync():void;}
}
