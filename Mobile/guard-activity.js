(()=>{
let busy=false;
function apply(snapshot){
 const now=Date.now(),activities=!document.hidden&&snapshot?.running?snapshot.activities||[]:[];
 for(const card of document.querySelectorAll('#wall .camera')){
  const camera=card.dataset.activityCamera,network=card.dataset.activityNetwork,service=card.dataset.activityService;
  const matches=activities.filter(a=>a.serviceId===service&&String(a.networkId)===network&&String(a.cameraId)===camera).sort((a,b)=>Date.parse(b.lastTrafficUtc)-Date.parse(a.lastTrafficUtc));
  const activity=matches[0],fresh=activity&&now-Date.parse(activity.lastTrafficUtc)<8000&&now>=Date.parse(activity.lastTrafficUtc)-15000;
  const pulsing=!!(fresh&&activity.pulsing&&card.dataset.live!=='true');card.classList.toggle('possible-recording',pulsing);
  let label=card.querySelector('.local-activity-label');if(!label){label=document.createElement('span');label.className='local-activity-label';label.setAttribute('role','status');card.append(label);}
  label.hidden=!fresh;label.textContent=fresh?(pulsing?'Possible recording':activity.state):'';label.title=fresh?'Inferred activity · '+(activity.source||'Capture')+' · '+activity.observedBytes.toLocaleString()+' unique upload bytes · does not confirm motion or a saved clip':'';
 }
 const status=document.getElementById('capture-monitoring');if(status){const unlinked=activities.filter(a=>!a.cameraId&&now-Date.parse(a.lastTrafficUtc)<8000).map(a=>a.name);status.textContent='Local activity: '+(snapshot?.state||'Feed unavailable')+(unlinked.length?' · Not linked to panels: '+unlinked.join(', '):'');}
}
async function update(){if(busy)return;if(document.hidden){apply(null);return;}busy=true;try{const response=await fetch('/api/local-activity',{cache:'no-store',signal:AbortSignal.timeout(3000)});if(!response.ok)throw Error('Unavailable');apply(await response.json());}catch{apply(null);}finally{busy=false;}}
document.addEventListener('visibilitychange',()=>{if(document.hidden)apply(null);else update();});setInterval(update,1000);update();
})();
