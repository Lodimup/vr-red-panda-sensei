// Development-only protocol peer. Never imported by the client bundle.
import {WebSocketServer} from 'ws';
const server=new WebSocketServer({port:Number(process.env.PORT||8787),path:'/ws'});
const defaults={show_japanese:true,show_reading:true,show_romaji:true,show_translation:true,pace:'normal',correction_style:'light',language_mode:'english_explanations'};
server.on('connection',ws=>{
  let support={...defaults},generation=0,revision=0,selected=null,instances=[],task=null,timers=[],lastSpeech='こんにちは！ 一緒に練習しましょう。', audioIn=false;
  const send=(type,data={})=>{if(ws.readyState===1)ws.send(JSON.stringify({type,v:1,...data}));};
  const snapshot=()=>({revision,selected_id:selected,instances,active_task:task,support});
  const scene=()=>send('scene',{snapshot:snapshot()});
  const later=(fn,ms)=>{const id=setTimeout(fn,ms);timers.push(id);};
  const cancel=()=>{timers.forEach(clearTimeout);timers=[];};
  const present=(text=lastSpeech, repeat=false)=>{cancel();lastSpeech=text;if(!repeat){const id=++generation;send('turn_started',{turn_id:id,generation_id:id});}send('sensei_state',{state:'presenting'});send('line',{generation_id:generation,segments:[{lang:'ja',text,reading:'こんにちは',romaji:'Konnichiwa',translation:'Hello! Let’s practice together.'}],captions:{show_japanese:support.show_japanese,show_reading:support.show_reading,show_romaji:support.show_romaji,show_translation:support.show_translation}});if(!repeat)later(()=>{send('sensei_state',{state:'idle'});send('turn_ended',{result:{turn_id:generation,generation_id:generation,status:'completed',error_code:null}});},3100);};
  ws.on('message',(data,binary)=>{if(binary){if(audioIn)audioIn=true;return;}let m;try{m=JSON.parse(data.toString());}catch{return;}if(m.v!==1){send('error',{code:'protocol_version',detail:'Unsupported version'});ws.close();return;}switch(m.type){
  case 'hello':send('welcome',{session_id:'s-mock',scene:snapshot(),support,task,llm:'ready'});break;
  case 'player_event':if(m.kind==='session_start'){present();later(()=>{instances=[{id:'apple_1',catalog_id:'apple',region:'left',slot:0,container_id:null,japanese:'りんご',reading:'りんご',english:'apple'}];revision++;scene();send('highlight',{instance_ids:['apple_1'],duration_ms:1800});},600);later(()=>{instances[0].region='center';revision++;scene();},1800);later(()=>{selected='apple_1';revision++;scene();},2600);}else if(m.kind==='help_hint'){send('hint',{card:{kind:'vocabulary',content:{japanese:'りんご',romaji:'ringo',english:'apple'}}});}else if(m.kind==='help_repeat')present();break;
  case 'player_select':if(instances.some(i=>i.id===m.instance_id)){selected=m.instance_id;revision++;send('action_result',{ok:true,error_code:null,revision});scene();}else send('action_result',{ok:false,error_code:'unknown_object',revision});break;
  case 'player_move':{const item=instances.find(i=>i.id===m.instance_id);const valid=(m.region!==null)!==(m.container_id!==null);if(item&&valid){item.region=m.region??item.region;item.container_id=m.container_id;revision++;send('action_result',{ok:true,error_code:null,revision});scene();}else send('action_result',{ok:false,error_code:'invalid_destination',revision});break;}
  case 'talk_start':cancel();break;
  case 'audio_begin':audioIn=true;break;
  case 'talk_end':audioIn=false;if(m.hold_ms<300)send('notice',{code:'didnt_catch'});else{send('transcript',{text:'りんご',final:true});present('りんごですね。');}break;
  case 'repeat':if(generation)present(lastSpeech,true);else send('notice',{code:'nothing_to_repeat'});break;
  case 'set_preference':support={...support,...m.patch};send('support',{support});scene();break;
  case 'session_summary':send('summary',{summary:{session_id:'s-mock',practiced:[{key:'apple',dimension:'vocabulary',label:'Apple',independent:1,supported:0,unresolved:0}],unresolved_keys:[],next_suggestion:null,preference_changes:[]}});break;
  case 'reset_profile':support={...defaults};instances=[];selected=null;revision++;scene();break;
  case 'cancel':cancel();break;
  }});ws.on('close',cancel);
});
console.log('Development mock listening on ws://localhost:8787/ws');
