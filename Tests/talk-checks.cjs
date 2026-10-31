const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),{webcrypto}=require('node:crypto');
const listeners={};let tracks=[],contexts=[],sockets=[],resolvePermission;
class Button{constructor(){this.handlers={};this.style={};this.classList={add(){},remove(){}};}addEventListener(n,f){this.handlers[n]=f;}setPointerCapture(){}}
class Context{constructor(){contexts.push(this);this.sampleRate=48000;this.destination={};}resume(){return Promise.resolve();}close(){this.closed=true;return Promise.resolve();}createMediaStreamSource(){return{connect(){},disconnect(){}};}createScriptProcessor(){return{connect(){},disconnect(){}};}createGain(){return{gain:{},connect(){},disconnect(){}};}}
class Socket{static OPEN=1;constructor(){sockets.push(this);this.readyState=1;this.bufferedAmount=0;queueMicrotask(()=>this.onopen?.());}addEventListener(){}close(){this.closed=true;}send(){}}
const stream=()=>{const track={stop(){this.stopped=true;}};tracks.push(track);return{getTracks:()=>[track]};};
const context={window:null,navigator:{mediaDevices:{getUserMedia:async()=>stream()}},isSecureContext:true,location:{protocol:'https:',host:'test'},crypto:webcrypto,AudioContext:Context,WebSocket:Socket,setTimeout,clearTimeout,Uint8Array,Int16Array,addEventListener:(n,f)=>listeners[n]=f,document:{addEventListener:(n,f)=>listeners[n]=f},console};context.window=context;vm.createContext(context);vm.runInContext(fs.readFileSync('Mobile/talk.js','utf8'),context);
(async()=>{
 const button=new Button(),status={},player={muted:false};context.DontBlinkTalk.bind(button,status,()=>({network:2,camera:3,session:'test'}),player);
 const press=()=>button.onpointerdown({button:0,pointerId:1,preventDefault(){}});
 await press();assert.equal(player.muted,true);button.handlers.pointerup();assert.ok(tracks.at(-1).stopped&&sockets.at(-1).closed&&contexts.at(-1).closed);assert.equal(player.muted,false);
 context.navigator.mediaDevices.getUserMedia=()=>new Promise(r=>resolvePermission=r);const pending=press();button.handlers.pointercancel();resolvePermission(stream());await pending;assert.ok(tracks.at(-1).stopped);
 context.navigator.mediaDevices.getUserMedia=async()=>stream();await press();listeners.blur();assert.ok(tracks.at(-1).stopped&&sockets.at(-1).closed);
 await press();context.document.hidden=true;listeners.visibilitychange();assert.ok(tracks.at(-1).stopped);
 context.isSecureContext=false;const count=tracks.length;await press();assert.equal(tracks.length,count);
 assert.equal(context.DontBlinkTalk.nonce().length,48);
 console.log('PASS microphone release, cancelled permission, window blur, hidden page, secure-context guard and session nonce');
})().catch(e=>{console.error(e);process.exitCode=1;});
