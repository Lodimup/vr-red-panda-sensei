import type { Snapshot, Region } from './types';
export interface Placement {x:number; y:number; z:number; scale:number}
const centres: Record<Region, [number,number,number]> = {left:[-.45,.8,0],center:[0,.8,0],right:[.45,.8,0],sensei_side:[-.95,.8,.15],learner_hand:[0,.82,.08]};
export function layout(snapshot: Snapshot): Record<string, Placement> {
  const out: Record<string, Placement> = {};
  for (const region of Object.keys(centres) as Region[]) {
    const loose = snapshot.instances.filter(i=>!i.container_id && i.region===region).sort((a,b)=>a.slot-b.slot || a.id.localeCompare(b.id));
    const [x,y,z] = centres[region];
    if (region==='learner_hand' || region==='sensei_side') {loose.forEach(i=>out[i.id]={x,y,z,scale:1}); continue;}
    for (let row=0;row<Math.ceil(loose.length/4);row++) {
      const items=loose.slice(row*4,row*4+4);
      items.forEach((item,i)=>out[item.id]={x:x+(i-(items.length-1)/2)*.16,y,z:z+row*.14,scale:1});
    }
  }
  // Resolve containers after their own placement; bounded passes tolerate nested containers.
  for(let pass=0;pass<snapshot.instances.length;pass++) {
    for(const parent of snapshot.instances) {
      const p=out[parent.id]; if(!p) continue;
      const children=snapshot.instances.filter(i=>i.container_id===parent.id).sort((a,b)=>a.slot-b.slot || a.id.localeCompare(b.id));
      children.forEach((child,k)=>out[child.id]={x:p.x+(k-(children.length-1)/2)*.05,y:p.y+.06,z:p.z-.03,scale:.6});
    }
  }
  return out;
}
export function regionAt(x:number,y:number,z:number): Region|null {
  if(y<.75||y>1.1||z<-.25||z>.25||x<-.675||x>.675)return null;
  return x<=-.225?'left':x<=.225?'center':'right';
}
