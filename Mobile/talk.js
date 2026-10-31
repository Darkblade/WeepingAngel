/* Hold-to-talk. Every release, navigation, blur and transport error stops capture. */
(()=>{
let current=null,generation=0;
function stop(){generation++;const old=current;current=null;if(!old)return;
 clearTimeout(old.timer);old.source?.disconnect();old.processor?.disconnect();old.gain?.disconnect();
 old.stream?.getTracks().forEach(t=>t.stop());old.context?.close().catch(()=>{});
 if(old.socket){old.socket.onclose=null;old.socket.onerror=null;old.socket.close();}
 if(old.player)old.player.muted=old.wasMuted;
 old.button?.classList.remove('talking');old.status.textContent='Microphone off.';
}
function bind(button,status,session,player){
 button.disabled=!window.isSecureContext||!navigator.mediaDevices?.getUserMedia;
 status.textContent=button.disabled?'Microphone needs trusted HTTPS on the phone. Use Secure phone setup in Windows.':'Hold to talk · maximum 30 seconds per press';
 const release=()=>stop();
 button.onpointerdown=async event=>{if(button.disabled||!window.isSecureContext||!navigator.mediaDevices?.getUserMedia||event.button>0)return;event.preventDefault();stop();if(event.pointerId!==undefined)button.setPointerCapture(event.pointerId);
 const version=generation,info=session();if(!info){status.textContent='Start live view before talking.';return;}
 const state={button,status,player,wasMuted:player.muted};current=state;
 try{
  status.textContent='Requesting microphone…';
  const stream=await navigator.mediaDevices.getUserMedia({audio:{channelCount:1,echoCancellation:true,noiseSuppression:true},video:false});
  if(version!==generation){stream.getTracks().forEach(t=>t.stop());return;}state.stream=stream;
  state.context=new AudioContext();await state.context.resume();if(version!==generation)return;
  const ws=new WebSocket((location.protocol==='https:'?'wss://':'ws://')+location.host+'/api/talk/'+info.network+'/'+info.camera+'?session='+encodeURIComponent(info.session)+'&rate='+state.context.sampleRate);state.socket=ws;
  ws.onclose=ws.onerror=()=>{if(current===state){stop();status.textContent='Talk channel closed. Check the Windows debug log.';}};
  await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.addEventListener('error',()=>reject(Error('Could not connect microphone.')),{once:true});ws.addEventListener('close',()=>reject(Error('Microphone channel closed.')),{once:true});});if(version!==generation)return;
  state.source=state.context.createMediaStreamSource(stream);
  state.processor=state.context.createScriptProcessor(2048,1,1);state.gain=state.context.createGain();state.gain.gain.value=0;
  state.processor.onaudioprocess=e=>{if(current!==state||ws.readyState!==WebSocket.OPEN)return;if(ws.bufferedAmount>64000){stop();status.textContent='Talk stopped: connection too slow.';return;}
   const samples=e.inputBuffer.getChannelData(0),pcm=new Int16Array(samples.length);for(let i=0;i<samples.length;i++)pcm[i]=Math.max(-1,Math.min(1,samples[i]))*32767;
   ws.send(pcm.buffer);
  };
  state.source.connect(state.processor);state.processor.connect(state.gain);state.gain.connect(state.context.destination);
  player.muted=true;button.classList.add('talking');status.textContent='Microphone transmitting. Release to stop.';state.timer=setTimeout(stop,30000);
 }catch(error){if(current===state){stop();status.textContent='Microphone: '+error.message;}}
 };
 for(const name of ['pointerup','pointercancel','lostpointercapture'])button.addEventListener(name,release);
 button.onkeydown=e=>{if((e.key===' '||e.key==='Enter')&&!e.repeat)button.onpointerdown(e);};button.onkeyup=e=>{if(e.key===' '||e.key==='Enter'){e.preventDefault();stop();}};
 button.style.touchAction='none';button.style.userSelect='none';
}
addEventListener('pagehide',stop);addEventListener('blur',stop);document.addEventListener('visibilitychange',()=>{if(document.hidden)stop();});
window.DontBlinkTalk={bind,stop,nonce:()=>{const a=crypto.getRandomValues(new Uint8Array(24));return [...a].map(v=>v.toString(16).padStart(2,'0')).join('');}};
})();
