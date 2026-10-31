using Blink.Models;
using DontBlink.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

internal static class BrowserUiChecks
{
    public static int Run()
    {
        string folder=Path.Combine(Path.GetTempPath(),"DontBlink-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string key=new('A',64);File.WriteAllBytes(Path.Combine(folder,"sample.mp4"),new byte[1024]);
        File.WriteAllText(Path.Combine(folder,key+".json"),JsonSerializer.Serialize(new ArchiveItem(key,"Garden",DateTime.UtcNow,"sample.mp4",1024)));
        var source=new UiSource();var clips=new ClipService(source,folder);var server=new MobileServer(clips,0,devices:new DeviceRegistry(Path.Combine(folder,"devices.json")));
        using var form=new Form {ClientSize=new Size(390,780),StartPosition=FormStartPosition.Manual,Location=new Point(-2000,-2000),ShowInTaskbar=false};
        using var browser=new WebView2 {Dock=DockStyle.Fill};form.Controls.Add(browser);int result=1;
        using var timeout=new System.Windows.Forms.Timer {Interval=45000};timeout.Tick+=(_,_)=>{Console.Error.WriteLine("Browser UI timeout");form.Close();};
        form.Shown+=async(_,_)=>{
            try
            {
                await server.StartAsync(false,default);
                var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(folder,"webview"));await browser.EnsureCoreWebView2Async(env);
                var cookie=browser.CoreWebView2.CookieManager.CreateCookie("dontblink",server.DesktopToken(),"127.0.0.1","/");browser.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                int stage=0;
                browser.CoreWebView2.WebMessageReceived+=async(_,e)=>{
                    string message=e.TryGetWebMessageAsString();
                    if(message=="SIGN_OUT"||message=="SIGN_IN"){source.IsAuthenticated=message=="SIGN_IN";await browser.ExecuteScriptAsync("window.sessionChanged()");return;}
                    if(message=="RELOAD_GUARD"){source.CameraCount=6;source.GuardStatusFixtures=true;form.ClientSize=new Size(1600,900);browser.CoreWebView2.Navigate(server.LocalAddress+"/guard-post");return;}
                    if(message=="NAV_DEVICES"){browser.CoreWebView2.Navigate(server.LocalAddress+"/devices");return;}
                    if(message=="GUARD_NARROW"){form.ClientSize=new Size(480,900);await browser.ExecuteScriptAsync("window.resized()");return;}
                    if(message=="FINALIZE_RECORDING"){
                        await Task.Delay(1300);string lateKey=new('B',64);File.WriteAllBytes(Path.Combine(folder,"phone-recording.mp4"),new byte[1024]);
                        File.WriteAllText(Path.Combine(folder,lateKey+".json"),JsonSerializer.Serialize(new ArchiveItem(lateKey,"Phone recording",DateTime.UtcNow,"phone-recording.mp4",1024)));return;
                    }
                    if(message.StartsWith("SNAPSHOT ")){
                        string name=message[9..];if(!new[]{"clips","settings","cameras","guard-post","guard-post-narrow","guard-post-local-activity","devices"}.Contains(name))return;
                        string previews=Path.Combine(Directory.GetCurrentDirectory(),"docs","previews");Directory.CreateDirectory(previews);
                        using var image=File.Create(Path.Combine(previews,"mobile-"+name+".png"));
                        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
                        await browser.ExecuteScriptAsync("window.snapshotDone()");return;
                    }
                    if(message=="RELOAD_SETTINGS"){browser.CoreWebView2.Navigate(server.LocalAddress+"/?ui-reload=1#settings");return;}
                    Console.WriteLine(message);Console.WriteLine("Browser USB list calls: "+source.Lists);result=message.StartsWith("PASS")&&source.Lists==1?0:1;form.BeginInvoke(form.Close);
                };
                browser.NavigationCompleted+=async(_,_)=>{
                    if(stage==3){stage++;await browser.ExecuteScriptAsync("""
                        (async()=>{try{
                          const sleep=ms=>new Promise(r=>setTimeout(r,ms)),check=(v,m)=>{if(!v)throw Error(m);};
                          const wait=async f=>{for(let n=0;n<200;n++){if(f())return;await sleep(50);}throw Error('Device editor condition timed out');};
                          await wait(()=>document.querySelectorAll('#devices .device').length===2);
                          check(document.documentElement.scrollWidth<=innerWidth+1,'Device editor overflows the narrow display');
                          document.querySelector('#new').click();document.querySelector('#name').value='Patio test';document.querySelector('#type').value='rtsp-camera';document.querySelector('#type').dispatchEvent(new Event('change'));document.querySelector('#ip').value='192.168.50.210';document.querySelector('#mac').value='02:11:22:33:44:55';document.querySelector('#editor').requestSubmit();
                          await wait(()=>document.querySelectorAll('#devices .device').length===3);
                          check(document.querySelector('#status').textContent.includes('saved'),'Device save has no feedback');
                          document.querySelector('#remove').click();await wait(()=>document.querySelectorAll('#devices .device').length===2);
                          const configuration=await (await fetch('/api/devices/export')).json();check(configuration.schemaVersion===1&&configuration.devices.length===2,'export lost devices/schema');
                          document.querySelector('#new').click();document.querySelector('#name').value='Blink import fixture';document.querySelector('#type').value='blink-outdoor-3';document.querySelector('#type').dispatchEvent(new Event('change'));const parent=document.querySelector('#parent');check(!parent.disabled&&parent.options.length===2,'new Blink camera cannot select its module');parent.selectedIndex=1;document.querySelector('#ip').value='192.168.50.220';document.querySelector('#mac').value='02:11:22:33:44:66';document.querySelector('#editor').requestSubmit();await wait(()=>document.querySelectorAll('#devices .device').length===3);check(configuration.devices.every(old=>[...document.querySelectorAll('.device h3')].some(title=>title.textContent===old.name)),'new camera overwrote an existing device');
                          await wait(()=>document.querySelector('#status').textContent.includes('saved'));window.confirm=()=>true;document.querySelector('#import-mode').value='replace';const transfer=new DataTransfer();transfer.items.add(new File([JSON.stringify(configuration)],'configuration.json',{type:'application/json'}));const file=document.querySelector('#import-file');file.files=transfer.files;file.dispatchEvent(new Event('change'));await wait(()=>document.querySelector('#status').textContent.includes('Imported 2 devices'));check(document.querySelectorAll('#devices .device').length===2,'browser import did not restore the exported configuration');
                          await new Promise(resolve=>{window.snapshotDone=resolve;chrome.webview.postMessage('SNAPSHOT devices');});
                          chrome.webview.postMessage('PASS device editor and offline registry, Guard Post layout and existing browser flows');
                        }catch(e){chrome.webview.postMessage('FAIL '+e.message+' '+e.stack);}})();
                        """);return;}
                    if(stage==2){stage++;await browser.ExecuteScriptAsync("""
                        (async()=>{try{
                          const sleep=ms=>new Promise(r=>setTimeout(r,ms)),check=(v,m)=>{if(!v)throw Error(m);};
                          const wait=async f=>{for(let n=0;n<200;n++){if(f())return;await sleep(50);}throw Error('Guard Post condition timed out');};
                          const snapshot=async name=>{await sleep(100);await new Promise(resolve=>{window.snapshotDone=resolve;chrome.webview.postMessage('SNAPSHOT '+name);});};
                          await wait(()=>document.querySelectorAll('#wall .camera').length===6&&document.querySelectorAll('#events .event').length===2);
                          const original=[...document.querySelectorAll('#wall .camera')].map(c=>c.dataset.wallKey);const first=document.querySelector('#wall .camera');check(first.draggable,'Guard Post cards cannot be dragged');const imageBefore=first.querySelector('img').src;const videoCommand=[...first.querySelectorAll('.camera-menu button')].find(b=>b.textContent==='Request video clip');check(videoCommand&&!videoCommand.disabled,'Guard Post video capture menu unavailable');videoCommand.click();await wait(()=>document.querySelector('#notice').textContent.includes('capture command complete'));check(first.querySelector('img').src===imageBefore&&!first.classList.contains('offline'),'video request changed or disabled the thumbnail');const menuButtons=[...first.querySelectorAll('.camera-menu button')];check(menuButtons[0].textContent==='Update Image','Update Image menu missing');menuButtons.find(b=>b.textContent==='Move later').click();check(document.querySelectorAll('#wall .camera')[1]===first,'Move later did not reorder panels');check(JSON.parse(localStorage.getItem('weeping-angel-guard-order'))[1]===original[0],'Guard Post order was not saved');menuButtons.find(b=>b.textContent==='Move earlier').click();check(document.documentElement.scrollWidth<=innerWidth+1,'Guard Post overflows wide display');check(document.querySelector('.camera-status .battery-status[data-state=ok]')&&document.querySelector('.camera-status .motion-status[data-state=on]'),'Guard Post does not separate and classify battery/motion status');check(document.querySelector('.battery-status[data-state=low]')&&document.querySelector('.motion-status[data-state=off]'),'Guard Post does not classify low battery and disabled motion');check(document.querySelector('.battery-status svg')&&document.querySelector('.motion-status svg'),'Guard Post status icons are missing');
                          const captureFetch=window.fetch;let captures=0;window.fetch=async(input,options)=>{if(String(input)==='/api/camera-refresh'){const request=JSON.parse(options.body);await sleep(request.camera===3?250:50);captures++;return captureFetch('/camera-image/'+request.network+'/'+request.camera);}return captureFetch(input,options);};const updating=[...document.querySelectorAll('.camera')].slice(0,2);for(const card of updating)card.querySelector('.camera-menu button').click();await wait(()=>captures===2&&updating.every(card=>card.querySelector('img').src.startsWith('blob:')&&card.querySelector('.feed-error').hidden));check(updating.every(card=>card.querySelector('.thumbnail-age').textContent.includes('Just now')),'batch capture ages did not update');window.fetch=captureFetch;document.getElementById('refresh').click();await wait(()=>!document.getElementById('refresh').disabled);check(document.querySelectorAll('.camera.offline').length===0,'overview refresh after image capture greys out the wall');check(updating.every(card=>card.querySelector('img').src.startsWith('blob:')),'overview refresh replaced captured images');const realFetch=window.fetch;let activityMode='fresh';window.fetch=async(input,options)=>{if(String(input)==='/api/local-activity'){if(activityMode==='error')throw Error('fixture disconnected');return new Response(JSON.stringify({running:true,state:'Following test capture',activities:[{serviceId:'blink',networkId:2,cameraId:3,lastTrafficUtc:new Date(Date.now()-(activityMode==='stale'?20000:0)).toISOString(),pulsing:true,state:'Possible recording',observedBytes:40000}]}),{status:200,headers:{'Content-Type':'application/json'}});}return realFetch(input,options);};
                          await wait(()=>document.querySelectorAll('.camera.possible-recording').length===1);check(document.querySelector('.camera.possible-recording').dataset.activityCamera==='3','local activity pulse highlights wrong camera');check(document.querySelector('.camera.possible-recording .local-activity-label').textContent==='Possible recording','pulse lacks honest inference label');check(matchMedia('(prefers-reduced-motion:reduce)').matches||getComputedStyle(document.querySelector('.camera.possible-recording .camera-top')).animationName==='possible-upload','red activity animation is missing');check(getComputedStyle(document.querySelector('.camera.possible-recording .local-activity-label')).position==='absolute','activity label layout is missing');await snapshot('guard-post-local-activity');
                          activityMode='stale';await wait(()=>document.querySelectorAll('.camera.possible-recording').length===0);activityMode='fresh';await wait(()=>document.querySelectorAll('.camera.possible-recording').length===1);activityMode='error';await wait(()=>document.querySelectorAll('.camera.possible-recording').length===0);window.fetch=realFetch;
                          const cert=await fetch('/phone-certificate');check(cert.ok&&(await cert.text()).includes('Phone microphone setup'),'certificate link still fails');
                          DontBlinkLive.start=()=>({stop(){},ready:Promise.resolve()});
                          const buttons=document.querySelectorAll('.camera-bottom .live-icon');buttons[0].click();buttons[1].click();await sleep(100);
                          check(document.querySelector('#live-count').textContent.includes('2 / 2')&&buttons[2].disabled,'Guard Post does not cap live viewers');
                          buttons[0].click();buttons[1].click();await sleep(100);
                          await snapshot('guard-post');
                          await new Promise(resolve=>{window.sessionChanged=resolve;chrome.webview.postMessage('SIGN_OUT');});document.querySelector('#refresh').click();
                          await wait(()=>document.querySelectorAll('#wall .camera.offline').length===6);
                          check([...document.querySelectorAll('.camera-bottom .live-icon')].every(b=>b.disabled),'Guard Post controls remain active offline');
                          document.querySelector('.event').click();check(document.querySelector('#recording').open&&document.querySelector('#archive-player').getAttribute('src').startsWith('/media/'),'offline archive cannot open in Guard Post');document.querySelector('#close-recording').click();
                          await new Promise(resolve=>{window.resized=resolve;chrome.webview.postMessage('GUARD_NARROW');});await sleep(150);await snapshot('guard-post-narrow');
                          check(document.documentElement.scrollWidth<=innerWidth+1,'Guard Post overflows narrow display');check(document.querySelector('#timeline').getBoundingClientRect().top>=document.querySelector('#wall').getBoundingClientRect().bottom,'Guard Post timeline overlaps the camera wall');
                          check(document.querySelectorAll('#lan-list article').length===2,'Guard Post is missing offline registered devices');chrome.webview.postMessage('NAV_DEVICES');
                        }catch(e){chrome.webview.postMessage('FAIL '+e.message+' '+e.stack);}})();
                        """);return;}
                    if(stage++>0){await browser.ExecuteScriptAsync("""
                        (async()=>{for(let n=0;n<200;n++){if(!document.querySelector('#settings-page').hidden&&!document.querySelector('#main').hidden)break;await new Promise(r=>setTimeout(r,25));}
                        if(document.documentElement.dataset.theme!=='Ocean'||document.querySelector('#settings-page').hidden||document.documentElement.dataset.talkSide!=='left')chrome.webview.postMessage('FAIL theme, accessibility or Settings did not survive navigation');
                        else chrome.webview.postMessage('RELOAD_GUARD');})();
                        """);return;}
                    await browser.ExecuteScriptAsync("""
                    (async()=>{try{
                      const sleep=ms=>new Promise(r=>setTimeout(r,ms));
                      const wait=async f=>{for(let n=0;n<200;n++){if(f())return;await sleep(50);}throw Error('UI condition timed out');};
                      const check=(v,m)=>{if(!v)throw Error(m);};
                      const snapshot=async name=>{await sleep(100);await new Promise(resolve=>{window.snapshotDone=resolve;chrome.webview.postMessage('SNAPSHOT '+name);});};
                      await wait(()=>document.querySelector('#mode')?.textContent.includes('Downloaded archive')&&!document.querySelector('#main').hidden);
                      check(document.querySelector('#module').options.length===0,'startup discovered USB modules');
                      check(document.querySelector('#clips-table').getBoundingClientRect().height>250,'phone header hides clip table');
                      check(document.querySelectorAll('#list button').length===0,'clip overview still has Play buttons');
                      const originalRow=document.querySelector('#list tr'),extra=originalRow.cloneNode(true);extra.dataset.key='sort-check';extra.cells[2].textContent='Alpha';extra.cells[2].dataset.sortValue='Alpha';extra.cells[4].dataset.sortValue='10';originalRow.cells[4].dataset.sortValue='2';document.querySelector('#list').append(extra);
                      const sortCamera=document.querySelector('#recordings-table th[data-sort-key=camera] button'),sortSize=document.querySelector('#recordings-table th[data-sort-key=size] button');
                      sortCamera.click();await sleep(20);check(document.querySelector('#list tr')===extra,'camera header does not sort ascending');sortCamera.click();await sleep(20);check(document.querySelector('#list tr')===originalRow,'camera header does not toggle descending');
                      sortSize.click();await sleep(20);check(document.querySelector('#list tr')===originalRow,'size header sorts lexically instead of numerically');sortSize.click();await sleep(20);check(document.querySelector('#list tr')===extra&&sortSize.parentElement.getAttribute('aria-sort')==='descending','size sort indicator or descending order is missing');extra.remove();await sleep(20);
                      const video=document.querySelector('video');let plays=0;video.play=()=>{plays++;return Promise.resolve();};
                      await sleep(100);const row=document.querySelector('#list tr');row.click();
                      check(row.getAttribute('aria-selected')==='true','single tap did not select the clip');await wait(()=>!!video.getAttribute('src')&&plays===1);
                      row.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:2}));await sleep(50);check(plays===1,'single tap did not play exactly once or double click restarted playback');await snapshot('clips');
                      const panel=document.querySelector('#playback-panel'),table=document.querySelector('#clips-table');
                      check(!panel.hidden&&panel.open,'single tap did not open player');
                      check(panel.getBoundingClientRect().top>=document.querySelector('#clip-controls').getBoundingClientRect().bottom,'clip controls are not above player');check(document.querySelector('.brand-line').contains(document.querySelector('#clip-sources')),'clip source toggle is outside brand row');check(document.querySelector('#notifications').nextElementSibling===document.querySelector('#main-navigation'),'notifications are not directly above navigation');
                      check(table.getBoundingClientRect().top>=panel.getBoundingClientRect().bottom&&table.getBoundingClientRect().height>100,'player overlaps or hides clip list');
                      const retainedRow=document.querySelector('#list tr'),retainedImage=retainedRow.querySelector('img'),beforeTop=table.getBoundingClientRect().top;const enabledBefore=document.querySelector('#archive').disabled;retainedRow.click();check(document.querySelector('#list tr')===retainedRow&&retainedRow.querySelector('img')===retainedImage,'clip switch rebuilt the list or thumbnails');check(document.querySelector('#archive').disabled===enabledBefore,'clip switch disabled unrelated controls');check(!panel.hidden&&panel.open&&Math.abs(table.getBoundingClientRect().top-beforeTop)<2,'clip switch collapsed the player or shifted the list');await wait(()=>plays===2);
                      const playingTop=table.getBoundingClientRect().top;
                      document.querySelector('#close-playback').click();
                      check(panel.hidden&&!video.getAttribute('src')&&table.getBoundingClientRect().top<playingTop,'closing player did not restore list space');
                      await sleep(450);row.click();await sleep(60);row.click();await wait(()=>!!video.getAttribute('src'));
                      await sleep(100);document.querySelector('#nav-settings').click();
                      check(!video.getAttribute('src')&&video.hidden&&!document.querySelector('#settings-page').hidden,'Settings left playback running');
                      check(document.querySelectorAll('[data-theme-choice]').length===10,'Settings does not offer ten explicit themes');
                      check(!document.querySelector('#theme'),'redundant theme dropdown remains');
                      for(const name of ['Gold','Autumn','Steel','Security','Forest','Ocean','Galaxy','Contrast','Contrast Light','Comfort']){document.querySelector('[data-theme-choice="'+name+'"]').click();check(document.documentElement.dataset.theme===name,'theme did not apply');}
                      check(!document.querySelector('[data-theme-choice=System]'),'System theme is still selectable');
                      DontBlinkTheme.apply('System');check(document.documentElement.dataset.theme==='Steel','legacy System preference did not migrate to Steel');
                      document.querySelector('[data-theme-choice=Autumn]').click();check(getComputedStyle(document.body).backgroundColor==='rgb(36, 27, 22)','Autumn did not apply its brown palette');
                      document.querySelector('[data-theme-choice=Ocean]').click();check(localStorage.getItem('dontblink-theme')==='Ocean','theme preference not saved');
                      const textSize=document.querySelector('[data-preference=text-size]'),normalFont=parseFloat(getComputedStyle(document.body).fontSize);textSize.value='2';textSize.dispatchEvent(new Event('change'));check(parseFloat(getComputedStyle(document.body).fontSize)>=normalFont*1.9,'200% text size did not scale text');textSize.value='1';textSize.dispatchEvent(new Event('change'));
                      const hand=document.querySelector('[data-preference=talk-side]');hand.value='left';hand.dispatchEvent(new Event('change'));check(localStorage.getItem('dontblink-talk-side')==='left','talk placement not saved');
                      await snapshot('settings');
                      check(document.querySelector('.bottom-nav').getBoundingClientRect().bottom<=innerHeight,'navigation extends below viewport');
                      document.querySelector('#cameras').click();await wait(()=>!document.querySelector('#camera-table').hidden);
                      check(!video.getAttribute('src')&&video.hidden,'section switch left archive video loaded');
                      check(document.querySelectorAll('.camera-card').length===1,'camera grid was not rendered');const cameraCard=document.querySelector('.camera-card'),previewBefore=cameraCard.querySelector('img').src;const videoCommand=cameraCard.querySelector('[data-video-request]');check(videoCommand&&!videoCommand.disabled,'mobile video capture menu unavailable');videoCommand.click();await wait(()=>document.querySelector('#status').textContent.includes('capture command complete'));check(document.querySelector('.camera-card')===cameraCard&&cameraCard.querySelector('img').src===previewBefore,'capture request rebuilt mobile cameras');
                      check(document.querySelector('#clip-sources').closest('header'),'source switch not in header');
                      check(document.querySelector('#filter').tagName==='SELECT','camera filter is not a dropdown');
                      check(document.querySelector('.battery-indicator')&&document.querySelector('.motion-indicator'),'camera indicators missing');
                      const menu=document.querySelector('.camera-menu');menu.open=true;menu.querySelector('[role=switch]').click();await wait(()=>document.querySelector('.camera-menu [role=switch]').getAttribute('aria-checked')==='false');
                      await snapshot('cameras');
                      await wait(()=>!document.querySelector('#cameras').disabled);
                      await new Promise(resolve=>{window.sessionChanged=resolve;chrome.webview.postMessage('SIGN_OUT');});
                      document.querySelector('#nav-clips').click();await wait(()=>document.body.dataset.section==='clips'&&!document.querySelector('#cameras').disabled);
                      check(!document.querySelector('#main').hidden&&document.querySelector('#pair').hidden,'Blink sign-out blocked local archive');
                      const offlineRow=document.querySelector('#list tr');offlineRow.click();await sleep(60);offlineRow.click();await wait(()=>!!video.getAttribute('src'));check(!document.querySelector('#main').hidden,'offline archive playback blocked');
                      await wait(()=>!document.querySelector('#cameras').disabled);document.querySelector('#cameras').click();await wait(()=>document.querySelector('.camera-card')?.getAttribute('aria-disabled')==='true');
                      check(document.querySelector('.camera-preview').disabled,'saved camera panel still operational offline');
                      await new Promise(resolve=>{window.sessionChanged=resolve;chrome.webview.postMessage('SIGN_IN');});await wait(()=>!document.querySelector('#cameras').disabled);document.querySelector('#cameras').click();await wait(()=>document.querySelector('.camera-card')?.getAttribute('aria-disabled')==='false');

                      const realFetch=window.fetch;let liveStarted=Date.now();window.fetch=(url,options)=>String(url).includes('/api/live-status/')?Promise.resolve(new Response(JSON.stringify({active:Date.now()-liveStarted<1800,finalizing:Date.now()-liveStarted<1800,canRecord:true,recording:false}),{headers:{'content-type':'application/json'}})):realFetch(url,options);
                      DontBlinkLive.start=()=>({stop(){},ready:Promise.resolve()});
                      document.querySelector('.camera-preview').click();await wait(()=>document.querySelector('#playback-panel').classList.contains('live-view'));
                      check(document.querySelector('#talk svg')&&document.querySelector('#stop-live svg')&&document.querySelector('#record-live-button svg'),'live overlay icons missing');
                      check(document.querySelector('#talk').getBoundingClientRect().left<document.querySelector('#record-live-button').getBoundingClientRect().left,'left-handed talk placement not applied');
                      chrome.webview.postMessage('FINALIZE_RECORDING');await sleep(100);document.querySelector('#nav-clips').click();await wait(()=>document.querySelector('#list').textContent.includes('Phone recording'));
                      check(document.querySelectorAll('#filter option').length===3,'new recording camera missing from filter');window.fetch=realFetch;
                      check(isSecureContext&&!!navigator.mediaDevices?.getUserMedia,'loopback microphone API is unavailable');
                      await new Promise((resolve,reject)=>{const w=new WebSocket('ws://'+location.host+'/api/talk/2/3?session='+crypto.randomUUID()+'&rate=48000');const timeout=setTimeout(()=>reject(Error('same-origin microphone WebSocket timed out')),3000);w.onopen=()=>{clearTimeout(timeout);w.close();resolve();};w.onerror=()=>{clearTimeout(timeout);reject(Error('microphone WebSocket blocked'));};});
                      await sleep(100);document.querySelector('#nav-clips').click();await wait(()=>!document.querySelector('#refresh').disabled);document.querySelector('#refresh').click();await wait(()=>document.querySelector('#mode').textContent.includes('USB manifest'));
                      check(document.querySelector('#module').options.length===1,'explicit USB request did not discover module');
                      const usbRows=document.querySelectorAll('#list tr');check(usbRows.length===2,'USB clips missing');
                      usbRows[0].click();check(usbRows[0].getAttribute('aria-selected')==='true'&&usbRows[0].querySelector('input').checked,'USB row selection is not tied to batch selection');
                      usbRows[1].querySelector('input').click();check(document.querySelector('#selection-summary').textContent==='2 selected','USB checkbox selection broke row selection');
                      chrome.webview.postMessage('RELOAD_SETTINGS');
                    }catch(e){chrome.webview.postMessage('FAIL '+e.message+' '+location.href+' '+e.stack);}})();
                    """);
                };
                timeout.Start();browser.CoreWebView2.Navigate(server.LocalAddress);
            }catch(Exception ex){Console.Error.WriteLine(ex);form.Close();}
        };
        Application.Run(form);SynchronizationContext.SetSynchronizationContext(null);server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return result;
    }
    private sealed class UiSource:IClipSource
    {
        public int Lists;
        public bool SupportsVideoRequest=>true;
        public Task RequestVideoAsync(int network,int camera)=>Task.CompletedTask;
        public bool IsAuthenticated{get;set;}=true;
        public int CameraCount=1;
        public bool GuardStatusFixtures;private bool motion=true;
        public Task SetMotionAsync(int network,int camera,bool enabled){motion=enabled;return Task.CompletedTask;}
        public Task<SyncModule[]> ModulesAsync()=>Task.FromResult(new[]{new SyncModule{Id=1,NetworkId=2}});
        public Task<CameraOverview[]> CamerasAsync()=>Task.FromResult(Enumerable.Range(0,CameraCount).Select(i=>new CameraOverview(3+i,2+i,new[]{"Garden","Front door","Driveway","Side entrance","Back garden","Garage"}[i],CameraOverview.Parse(JsonSerializer.SerializeToElement(new{id=3+i,network_id=2+i,type="catalina"}))!.Model,"online",GuardStatusFixtures&&i==1?"low":"ok",GuardStatusFixtures&&i==0||motion?"Enabled":"Disabled")).ToArray());
        public Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module){Lists++;return Task.FromResult<IEnumerable<BlinkVideoInfo>>(new[]{1,2}.Select(id=>new BlinkVideoInfo{Id=id.ToString(),ManifestId="1",CameraName="Garden",ModuleId=1,NetworkId=2,CreatedAt=DateTime.UtcNow.AddSeconds(-id),Size="1024"}));}
        public Task<byte[]> CameraImageAsync(int network,int camera,bool capture)
        {
            using var bitmap=new Bitmap(640,360);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(16,47,64));using var font=new Font("Segoe UI",26);g.DrawString("Test camera preview",font,Brushes.White,125,155);}
            using var image=new MemoryStream();bitmap.Save(image,System.Drawing.Imaging.ImageFormat.Jpeg);return Task.FromResult(image.ToArray());
        }
        public Task<byte[]> DownloadAsync(BlinkVideoInfo video)=>throw new NotImplementedException();
        public Task DeleteAsync(BlinkVideoInfo video)=>throw new NotImplementedException();
    }
}






