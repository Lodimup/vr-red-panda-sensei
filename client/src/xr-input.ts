export interface XRActions {
  talkStart(): void; talkEnd(): void; repeat(): void; hint(): void; menu(): void;
  grabStart(index: number): void; grabEnd(index: number): void; grabCancel(index: number): void;
}

/** Edge-trigger controller buttons and keep simultaneous talk holds independent. */
export class XRInput {
  private previous = new Map<number, boolean[]>();
  private talking = new Set<string>();
  constructor(private actions: XRActions) {}
  private talk(key: string, down: boolean) {
    const was = this.talking.size > 0;
    if (down) this.talking.add(key); else this.talking.delete(key);
    if (!was && this.talking.size) this.actions.talkStart();
    if (was && !this.talking.size) this.actions.talkEnd();
  }
  private handGrabs = new Set<number>();
  /** A hand pinch that starts on an object grabs it; any other pinch holds talk. */
  hand(index: number, down: boolean, onObject = false) {
    if (down && onObject && !this.talking.has(`hand${index}`)) { this.handGrabs.add(index); this.actions.grabStart(index); return; }
    if (!down && this.handGrabs.delete(index)) { this.actions.grabEnd(index); return; }
    this.talk(`hand${index}`, down);
  }
  poll(index: number, source: XRInputSource | null) {
    const old = this.previous.get(index) ?? [];
    const buttons = source && !source.hand ? source.gamepad?.buttons : undefined;
    const now = Array.from({length: 6}, (_, i) => buttons?.[i]?.pressed ?? false);
    const edge = (i: number) => now[i] && !old[i];
    if (source?.handedness === 'right' || old[4]) {
      if (source?.handedness === 'right' && edge(5)) this.actions.repeat();
      if (source?.handedness === 'right' || !now[4]) this.talk(`button${index}`, !!(source?.handedness === 'right' && now[4]));
    }
    if (source?.handedness === 'left') {
      if (edge(4)) this.actions.hint();
      if (edge(5)) this.actions.menu();
    }
    if (edge(1)) this.actions.grabStart(index);
    if (old[1] && !now[1]) this.actions.grabEnd(index);
    this.previous.set(index, now);
  }
  disconnect(index: number) {
    const old = this.previous.get(index);
    if (old?.[1] || this.handGrabs.delete(index)) this.actions.grabCancel(index);
    this.previous.delete(index); this.talk(`button${index}`, false); this.hand(index, false);
  }
}
