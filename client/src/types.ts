export type Region = 'left' | 'center' | 'right' | 'sensei_side' | 'learner_hand';
export interface Support {show_japanese: boolean; show_reading: boolean; show_romaji: boolean; show_translation: boolean; pace: 'normal'|'slow'; correction_style: string; language_mode: string}
export interface Instance {id: string; catalog_id: string; region: Region; slot: number; container_id: string|null; japanese: string; reading: string; english: string}
export interface Task {task_id: string; status: string; family: string; skill_targets: string[]; instruction: {japanese?: string; english?: string}}
export interface Snapshot {revision: number; selected_id: string|null; instances: Instance[]; active_task: Task|null; support: Support}
export interface Chunk {type: 'speak_chunk'; generation_id: number; utterance_id: string; segment_index: number; chunk_index: number; lang: 'ja'|'en'; text: string; reading?: string; romaji?: string; translation?: string; captions: Pick<Support,'show_japanese'|'show_reading'|'show_romaji'|'show_translation'>; sample_rate: number; channels: number; format: string; final: boolean}
export interface Hint {kind: string; content: {japanese?: string; reading?: string; romaji?: string; english?: string}}
export interface State {snapshot: Snapshot|null; support: Support|null; task: Task|null; hint: Hint|null; caption: Chunk|null; captionError: string; said: string; connection: string; llm: string; voice: string; recognizer: string; sensei: 'idle'|'thinking'|'speaking'; pose: string|null; poseTarget: string|null; playing: boolean; talking: boolean; menu: boolean; summary: {practiced?: {label: string; independent: number; supported: number}[]; next_suggestion?: string|null}|null; highlights: Record<string, number>}
export class Store {
  state: State = {snapshot:null,support:null,task:null,hint:null,caption:null,captionError:'',said:'',connection:'Connecting…',llm:'Connecting…',voice:'Waiting for server',recognizer:'Waiting for server',sensei:'idle',pose:null,poseTarget:null,playing:false,talking:false,menu:false,summary:null,highlights:{}};
  private listeners = new Set<(state: State)=>void>();
  update(patch: Partial<State>) {this.state = {...this.state,...patch}; this.listeners.forEach(fn=>fn(this.state));}
  subscribe(fn: (state: State)=>void) {this.listeners.add(fn); fn(this.state); return ()=>this.listeners.delete(fn);}
}
