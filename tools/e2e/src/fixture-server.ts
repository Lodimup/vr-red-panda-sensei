import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { WebSocketServer } from 'ws';

const html = await readFile(fileURLToPath(new URL('../fixtures/index.html', import.meta.url)));
const events: unknown[] = [];
const server = createServer((req, res) => {
  if (req.url === '/events') { res.writeHead(200, {'content-type':'application/json', 'access-control-allow-origin':'*'}); res.end(JSON.stringify(events)); return; }
  res.writeHead(200, {'content-type':'text/html'}); res.end(html);
});
const wss = new WebSocketServer({server, path:'/ws'});
const send = (ws: import('ws').WebSocket, data: object) => ws.send(JSON.stringify({v:1, ...data}));
const support = { show_japanese:true, show_reading:true, show_romaji:false, show_translation:true, pace:'normal', correction_style:'light', language_mode:'english_explanations' };
wss.on('connection', ws => {
  ws.on('message', (raw, binary) => {
    if (binary) { events.push({type:'binary', bytes:Buffer.from(raw as Buffer).length, nonzero:Buffer.from(raw as Buffer).some(b => b !== 0)}); return; }
    const msg = JSON.parse(raw.toString()); events.push(msg);
    if (msg.type === 'hello') send(ws,{type:'welcome',session_id:'fixture',llm:'ready',scene:{revision:0,selected_id:null,instances:[],active_task:null,support}, support, task:null});
    if (msg.type === 'player_event' && msg.kind === 'session_start') {
      send(ws,{type:'turn_started',turn_id:1,generation_id:1});
      send(ws,{type:'speak_chunk',utterance_id:'u1',generation_id:1,segment_index:0,chunk_index:0,lang:'en',text:'Hello, learner.',captions:{show_japanese:true,show_reading:true,show_romaji:false,show_translation:true},format:'pcm_s16le',sample_rate:24000,channels:1,final:true});
      const pcm=Buffer.alloc(24000); for(let i=0;i<12000;i++) pcm.writeInt16LE(Math.round(Math.sin(2*Math.PI*440*i/24000)*8000),i*2); ws.send(pcm);
      send(ws,{type:'turn_ended',result:{turn_id:1,generation_id:1,status:'completed',error_code:null}});
    }
    if (msg.type === 'talk_end') send(ws,{type:'transcript',text:'fixture speech',final:true});
    if (msg.type === 'player_select' || msg.type === 'player_move') send(ws,{type:'action_result',ok:true,error_code:null,revision:1});
  });
});
server.listen(0,'127.0.0.1',() => { const address=server.address(); if (!address || typeof address==='string') throw new Error('No port'); console.log(JSON.stringify({client:`http://127.0.0.1:${address.port}`,ws:`ws://127.0.0.1:${address.port}/ws`})); });
const close = () => { for(const client of wss.clients) client.terminate(); wss.close(); server.close(); };
process.on('SIGTERM',close); process.on('SIGINT',close);
