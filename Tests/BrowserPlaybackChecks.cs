using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

internal static class BrowserPlaybackChecks
{
    public static int Run(string videoPath)
    {
        int result = 1; bool finished = false;
        string profile = Path.Combine(Path.GetTempPath(), "DontBlink-BrowserCheck-" + Guid.NewGuid().ToString("N"));
        using var form = new Form { Text = "DontBlink playback regression", Size = new Size(640,480), StartPosition = FormStartPosition.Manual, Location = new Point(-2000,-2000), ShowInTaskbar = false };
        using var browser = new WebView2 { Dock = DockStyle.Fill }; form.Controls.Add(browser);
        using var timeout = new System.Windows.Forms.Timer { Interval = 20000 };
        timeout.Tick += (_, _) => { Console.WriteLine("FAIL Chromium playback timed out"); form.Close(); };
        form.Shown += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null,profile,new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding"));
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.WebMessageReceived += (_, message) =>
                {
                    using var data = JsonDocument.Parse(message.TryGetWebMessageAsString());
                    string kind = data.RootElement.GetProperty("kind").GetString()!;
                    Console.WriteLine(data.RootElement.GetProperty("message").GetString());
                    if (!finished && kind is "pass" or "fail") { finished = true; result = kind == "pass" ? 0 : 1; form.BeginInvoke(form.Close); }
                };
                string script = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Mobile","live-player.js"));
                string bytes = Convert.ToBase64String(await File.ReadAllBytesAsync(videoPath));
                string html = "<!doctype html><meta charset='utf-8'><video id='v' muted autoplay controls></video><script>" + script + "</script><script>"
                    + "function report(kind,message){chrome.webview.postMessage(JSON.stringify({kind,message}));}"
                    + "const video=document.getElementById('v');const bytes=Uint8Array.from(atob('" + bytes + "'),c=>c.charCodeAt(0));"
                    + "window.fetch=async()=>({ok:true,body:{getReader:()=>{let offset=0;return{read:async()=>{if(offset>=bytes.length)return{done:true};const value=bytes.slice(offset,offset+4096);offset+=value.length;return{value,done:false};}}}}});"
                    + "video.addEventListener('error',()=>report('fail','FAIL Chromium decoder '+video.error?.code+': '+video.error?.message));"
                    + "const player=DontBlinkLive.start(video,'/fixture',{diagnostic:m=>report('log',m),error:e=>report('fail','FAIL '+e.message)});"
                    + "player.ready.then(()=>report('pass','PASS Chromium decoded live fragments: '+video.videoWidth+'x'+video.videoHeight)).catch(e=>{if(e.name!=='AbortError')report('fail','FAIL '+e.message);});</script>";
                timeout.Start(); browser.NavigateToString(html);
            }
            catch (Exception ex) { Console.WriteLine(ex); form.Close(); }
        };
        Application.Run(form);
        return result;
    }
}
