import { chromium, type Page } from 'playwright';
import { strict as assert } from 'node:assert';
import { mkdir, writeFile, rm } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createInterface } from 'node:readline';
import { XR, audioMark, assertAudio } from './helpers.js';

const root = fileURLToPath(new URL('..', import.meta.url));
const out = resolve(process.env.E2E_OUTPUT ?? resolve(root, '.e2e-out'));
const fixture = !process.env.CLIENT_URL && !process.env.MOCK_WS_URL;
if (!fixture && (!process.env.CLIENT_URL || !process.env.MOCK_WS_URL)) throw new Error('Set both CLIENT_URL and MOCK_WS_URL, or neither for the fixture');
let server: ReturnType<typeof spawn> | undefined;
let clientURL = process.env.CLIENT_URL ?? '', mockURL = process.env.MOCK_WS_URL ?? '';
if (fixture) {
  server = spawn(process.execPath, ['--import','tsx',resolve(root,'src/fixture-server.ts')], {cwd:root, stdio:['ignore','pipe','inherit']});
  try { const line=await new Promise<string>((ok,fail)=>{createInterface({input:server!.stdout!}).once('line',ok);server!.once('exit',code=>fail(new Error(`fixture exited ${code}`)));}); const urls=JSON.parse(line);clientURL=urls.client;mockURL=urls.ws; } catch(e) {server.kill(); throw e;}
}
function loopback(raw: string, protocols: string[]) { const u=new URL(raw); assert.ok(protocols.includes(u.protocol) && ['localhost','127.0.0.1','[::1]'].includes(u.hostname) && !u.username && !u.password, `Only local mock endpoints are permitted: ${raw}`); }
loopback(clientURL,['http:','https:']);loopback(mockURL,['ws:','wss:']);
await mkdir(out,{recursive:true});
const wav=resolve(out,'fake-microphone.wav');
const pcm=Buffer.alloc(16000*2);for(let i=0;i<16000;i++)pcm.writeInt16LE(Math.round(12000*Math.sin(2*Math.PI*440*i/16000)),i*2);
const header=Buffer.alloc(44);header.write('RIFF');header.writeUInt32LE(36+pcm.length,4);header.write('WAVEfmt ',8);header.writeUInt32LE(16,16);header.writeUInt16LE(1,20);header.writeUInt16LE(1,22);header.writeUInt32LE(16000,24);header.writeUInt32LE(32000,28);header.writeUInt16LE(2,32);header.writeUInt16LE(16,34);header.write('data',36);header.writeUInt32LE(pcm.length,40);await writeFile(wav,Buffer.concat([header,pcm]));
const summary: unknown[]=[];
let browser: Awaited<ReturnType<typeof chromium.launch>> | undefined;
try {
  browser=await chromium.launch({headless:false, ...(process.env.CHROMIUM_PATH?{executablePath:process.env.CHROMIUM_PATH}:{}), args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream',`--use-file-for-fake-audio-capture=${process.env.MIC_WAV ?? wav}`]});
  for(const scenario of ['A01','D1']) {
    const dir=resolve(out,scenario);await rm(dir,{recursive:true,force:true});await mkdir(dir,{recursive:true});
    const context=await browser.newContext({permissions:['microphone'], serviceWorkers:'block'});const page=await context.newPage();const errors:string[]=[];const logs:string[]=[]; const steps: {name:string,status:string,detail?:unknown}[]=[];
    page.on('console',m=>{logs.push(`${m.type()}: ${m.text()}`);if(m.type()==='error')errors.push(m.text());});page.on('pageerror',e=>errors.push(e.message));
    await context.route('**/*',route=>{const u=new URL(route.request().url());if(['localhost','127.0.0.1','[::1]'].includes(u.hostname))return route.continue();return route.abort();});
    await context.addInitScript(({mock})=>{
      const sent: any[]=[], received: any[]=[]; Object.assign(window,{__MOCK_WS_URL:mock,__wireSent:sent,__wireReceived:received});
      const Native=window.WebSocket;
      window.WebSocket=class extends Native {
        constructor(url:string|URL, protocols?:string|string[]){
          const requested=new URL(url.toString(),location.href), target=new URL(mock);
          if(requested.href!==target.href && !(requested.host===location.host && requested.pathname==='/ws' && !requested.search)) throw new Error('Blocked non-mock WebSocket');
          super(target.href,protocols);
          this.addEventListener('message', e => {if(typeof e.data==='string') {try {received.push(JSON.parse(e.data));} catch {received.push({type:'invalid-json'});}}});
        }
        send(data:string|ArrayBufferLike|Blob|ArrayBufferView){
          if(typeof data==='string') { try { sent.push(JSON.parse(data)); } catch { sent.push({type:'invalid-json'}); } }
          else if(data instanceof ArrayBuffer) { const b=new Uint8Array(data);sent.push({type:'binary',bytes:b.byteLength,nonzero:b.some(x=>x!==0)}); }
          else sent.push({type:'binary',bytes:('byteLength' in data ? data.byteLength : data.size)});
          super.send(data);
        }
      } as typeof WebSocket;
    },{mock:mockURL});
    await context.addInitScript({path:resolve(root,'dist/inject.js')});
    const fixtureOffset = fixture ? ((await (await fetch(new URL('/events',clientURL))).json()) as unknown[]).length : 0;
    let index=0;
    const step=async(name:string,fn:()=>Promise<unknown>)=>{index++;try {const detail=await fn();steps.push({name,status:'passed',...(detail===undefined?{}:{detail})});}catch(e){steps.push({name,status:'failed',detail:String(e)});throw e;}finally{await page.screenshot({path:resolve(dir,`${String(index).padStart(2,'0')}-${name}.png`)}).catch(()=>{});}};
    try {
      await step('load-and-connect',async()=>{await page.goto(clientURL);if(fixture) await page.getByText('Ready',{exact:true}).waitFor(); else await page.locator('#connection').getByText('Practice area placed').waitFor();});
      if(scenario==='A01') {
        await step('start-and-hear-greeting',async()=>{const mark=await audioMark(page);await page.getByRole('button',{name:fixture?'Begin session':'Start session'}).click();if(fixture)await page.getByText('Hello, learner.').waitFor();else await page.locator('#a11y').getByText('こんにちは！ 一緒に練習しましょう。').waitFor();return assertAudio(page,mark,2000);});
        await step('xr-session-and-input',async()=>{const xr=new XR(page);await xr.startAR();await xr.headPose([0,1.7,0],[0,0,0,1]);for(const [hand,button] of [['right','a'],['right','b'],['left','x'],['left','y'],['left','grip'],['right','grip'],['left','trigger'],['right','trigger']] as const){await xr.button(hand,button,true);await xr.button(hand,button,false);}await xr.inputMode('hand');await xr.pinch('left',true);await xr.pinch('left',false);await xr.pinch('right',true);await xr.pinch('right',false);const events=await xr.events();for(const hand of ['left','right']){assert.ok(events.includes(`squeezestart:${hand}`));assert.ok(events.filter(e=>e===`selectstart:${hand}`).length>=2);}await xr.endAR();});
      } else {
        await step('begin',async()=>{const mark=await audioMark(page);await page.getByRole('button',{name:fixture?'Begin session':'Start session'}).click();if(fixture)await page.getByText('Hello, learner.').waitFor();else await page.locator('#a11y').getByText('こんにちは！ 一緒に練習しましょう。').waitFor();return assertAudio(page,mark,1200);});
        await step('select-card',async()=>{if(fixture)await page.getByRole('button',{name:'Apple'}).click();else {await page.waitForTimeout(3100);const canvas=page.locator('canvas');const box=await canvas.boundingBox();assert.ok(box);await page.mouse.click(box.x+box.width*.5,box.y+box.height*.525);await page.waitForFunction(() => (window as any).__wireSent.some((e:any)=>e.type==='player_select'&&e.instance_id==='apple_1') && (window as any).__wireReceived.some((e:any)=>e.type==='action_result'&&e.ok));}});
        await step('move-card',async()=>{if(fixture)await page.locator('#card').dragTo(page.locator('#right'));else {const box=await page.locator('canvas').boundingBox();assert.ok(box);await page.mouse.move(box.x+box.width*.5,box.y+box.height*.525);await page.mouse.down();await page.mouse.move(box.x+box.width*.38,box.y+box.height*.59,{steps:10});await page.mouse.up();await page.waitForFunction(() => (window as any).__wireSent.some((e:any)=>e.type==='player_move'&&e.region==='left') && (window as any).__wireReceived.filter((e:any)=>e.type==='action_result'&&e.ok).length>=2);}});
        await step('repeat',async()=>{const mark=await audioMark(page);await page.keyboard.press('r');if(!fixture)return assertAudio(page,mark,1200);});
        await step('keyboard-controls',async()=>{for(const key of ['h','m','l'])await page.keyboard.press(key);if(fixture)await page.getByText('Menu open').waitFor();else await page.locator('#menu').waitFor({state:'visible'});});
        await step('push-to-talk',async()=>{await page.keyboard.down('Space');await page.waitForTimeout(1200);const mark=await audioMark(page);await page.keyboard.up('Space');if(fixture)await page.getByText('You: fixture speech').waitFor();else {await page.locator('#a11y').getByText('You: りんご').waitFor();return assertAudio(page,mark,1200);}});
      }
      if(fixture){await step('assert-mock-protocol',async()=>{const events=(await (await fetch(new URL('/events',clientURL))).json() as any[]).slice(fixtureOffset);const has=(type:string,extra:(e:any)=>boolean=()=>true)=>events.some(e=>e.type===type&&extra(e));assert.ok(has('hello'));assert.ok(has('player_event',e=>e.kind==='area_placed'));assert.ok(has('player_event',e=>e.kind==='session_start'));if(scenario==='D1'){assert.ok(has('player_select',e=>e.instance_id==='apple'));assert.ok(has('player_move',e=>e.region==='right'));assert.ok(has('repeat'));assert.ok(has('player_event',e=>e.kind==='help_hint'));assert.ok(has('set_preference',e=>e.patch.pace==='slow'));assert.ok(has('talk_start'));assert.ok(has('audio_begin'));assert.ok(has('binary',e=>e.nonzero&&e.bytes>0));assert.ok(has('talk_end',e=>e.hold_ms>=300));}});}
      if(!fixture){await step('assert-client-protocol',async()=>{const events=await page.evaluate(()=> (window as any).__wireSent as any[]);const has=(type:string,extra:(e:any)=>boolean=()=>true)=>events.some(e=>e.type===type&&extra(e));assert.ok(has('hello'));assert.ok(has('player_event',e=>e.kind==='session_start'));if(scenario==='D1'){assert.ok(has('player_select',e=>e.instance_id==='apple_1'),'card select');assert.ok(has('player_move',e=>e.instance_id==='apple_1'&&e.region==='left'),'card move');const received=await page.evaluate(()=> (window as any).__wireReceived as any[]);assert.ok(received.filter(e=>e.type==='action_result'&&e.ok).length>=2,'server accepted both actions');assert.ok(received.some(e=>e.type==='scene'&&e.snapshot.instances.some((i:any)=>i.id==='apple_1'&&i.region==='left')),'server snapshot reflects move');assert.ok(has('repeat'));assert.ok(has('player_event',e=>e.kind==='help_hint'));assert.ok(has('set_preference',e=>e.patch.pace==='slow'));assert.ok(has('talk_start'));assert.ok(has('audio_begin'));assert.ok(has('binary',e=>e.nonzero&&e.bytes>0));assert.ok(has('talk_end',e=>e.hold_ms>=300));}});}
      await step('console-errors',async()=>assert.deepEqual(errors,[]));
    }catch(e){console.error(`${scenario}: ${String(e)}`);}finally{await writeFile(resolve(dir,'console.log'),logs.join('\n')+'\n');const wire=await page.evaluate(()=>({sent:(window as any).__wireSent,received:(window as any).__wireReceived})).catch(()=>null);await writeFile(resolve(dir,'wire.json'),JSON.stringify(wire,null,2)+'\n');await context.close();}
    summary.push({scenario,target:fixture?'fixture':'client',steps,errors});
  }
}finally {if(browser)await browser.close();if(server){server.kill('SIGTERM');await new Promise<void>(resolve=>{if(server!.exitCode!==null)resolve();else server!.once('exit',()=>resolve());});}await writeFile(resolve(out,'summary.json'),JSON.stringify(summary,null,2)+'\n');}
if(summary.some((s:any)=>s.steps.some((step:any)=>step.status==='failed')||s.errors.length))process.exitCode=1;
