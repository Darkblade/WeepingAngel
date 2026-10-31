using DontBlink;
using DontBlink.Services;
using Blink.Models;
using System.Reflection;

internal static class GridInteractionChecks
{
    private sealed class PreviewForm:Form1 { protected override void OnShown(EventArgs e){} }
    internal static int Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        string folder=Path.Combine(Path.GetTempPath(),"WeepingAngel-grid-"+Guid.NewGuid());
        try{
            var source=new Source();var clips=new ClipService(source,folder,Path.Combine(folder,"cameras.json"));
            using var form=new PreviewForm{Location=new Point(-4000,-4000),StartPosition=FormStartPosition.Manual,ShowInTaskbar=false};
            T Field<T>(string name)=>(T)typeof(Form1).GetField(name,flags)!.GetValue(form)!;
            typeof(Form1).GetField("_clips",flags)!.SetValue(form,clips);typeof(Form1).GetField("_cameraMedia",flags)!.SetValue(form,new CameraMediaService(clips));typeof(Form1).GetField("_cameraItems",flags)!.SetValue(form,source.CamerasAsync().Result);
            typeof(Form1).GetMethod("BindCameraOverview",flags)!.Invoke(form,null);
            var host=Field<Panel>("_cameraHost");var tab=(TabPage)host.Parent!.Parent!;((TabControl)tab.Parent!).SelectedTab=tab;form.Show();Wait(()=>Field<CancellationTokenSource?>("_operation")==null);
            var cards=Field<FlowLayoutPanel>("_cameraCards");var panel=cards.Controls[0];var picture=panel.Controls.OfType<CameraPreview>().Single();var motion=picture.Controls.OfType<CameraMotionButton>().Single();
            Check(cards.AllowDrop&&panel.AllowDrop&&picture.AllowDrop,"grid panels accept camera drag data");
            motion.PerformClick();Wait(()=>source.Changes==1&&Field<CancellationTokenSource?>("_operation")==null);Check(source.Enabled&&motion.Camera?.Motion=="Enabled","motion-icon click enables recording and refreshes the icon");
            motion.PerformClick();Wait(()=>source.Changes==2&&Field<CancellationTokenSource?>("_operation")==null);Check(!source.Enabled&&motion.Camera?.Motion=="Disabled","second motion-icon click disables recording");
            Check(Field<HashSet<Form>>("_players").Count==0&&Field<MobileServer?>("_server")==null,"motion-icon clicks do not start live view or the phone server");
            var options=picture.Controls.OfType<LinkLabel>().Single();var menu=options.ContextMenuStrip!;
            for(int attempt=1;attempt<=2;attempt++){menu.Show(options,new Point(0,options.Height));menu.Items[0].PerformClick();menu.Close();int expected=attempt;Wait(()=>source.Captures==expected&&Field<CancellationTokenSource?>("_operation")==null);}
            Check(Field<ListBox>("_log").Items.Cast<object>().Count(x=>x.ToString()!.Contains("Update image complete"))==2,"repeated image menu clicks capture fresh images and log completion");
            Check(picture.Camera?.ThumbnailCapturedUtc!=null&&picture.Camera.ThumbnailTimeEstimated,"successful image update immediately refreshes the panel age");
            var refreshed=clips.CameraOverviewAsync(default).GetAwaiter().GetResult().Cameras.Single();
            Check(refreshed.ThumbnailCapturedUtc!=null&&refreshed.ThumbnailTimeEstimated,"missing provider timestamp does not erase the confirmed update time");
            var restored=new ClipService(source,folder,Path.Combine(folder,"cameras.json"));
            Check(restored.LastKnownCameras.Single().ThumbnailCapturedUtc==refreshed.ThumbnailCapturedUtc,"confirmed thumbnail age survives application restart");
            using(var busy=new CancellationTokenSource()){typeof(Form1).GetField("_operation",flags)!.SetValue(form,busy);menu.Show(options,new Point(0,options.Height));menu.Items[0].PerformClick();menu.Close();Application.DoEvents();Check(source.Captures==2&&Field<ListBox>("_log").Items.Cast<object>().Any(x=>x.ToString()!.Contains("Update image not started")),"busy image update is explained in diagnostics");typeof(Form1).GetField("_operation",flags)!.SetValue(form,null);}
            var archiveHold=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var background=(Task)typeof(Form1).GetMethod("RunArchiveAsync",flags)!.Invoke(form,new object[]{(Func<Task>)(()=>archiveHold.Task)})!;
            try{Check(Field<CancellationTokenSource?>("_operation")==null&&motion.Enabled,"manual archive leaves foreground controls available");int changes=source.Changes;motion.PerformClick();Wait(()=>source.Changes==changes+1&&Field<CancellationTokenSource?>("_operation")==null);Check(!background.IsCompleted,"camera action completes while manual archive remains active");}
            finally{archiveHold.SetResult();Wait(()=>background.IsCompleted);background.GetAwaiter().GetResult();}
            using var drag=new CameraPreview();int drags=0,clicks=0;drag.ReorderDragRequested+=(_,_)=>drags++;drag.Click+=(_,_)=>clicks++;
            typeof(CameraPreview).GetMethod("OnMouseDown",flags)!.Invoke(drag,[new MouseEventArgs(MouseButtons.Left,1,10,10,0)]);
            typeof(CameraPreview).GetMethod("OnMouseMove",flags)!.Invoke(drag,[new MouseEventArgs(MouseButtons.Left,0,100,100,0)]);
            typeof(CameraPreview).GetMethod("OnClick",flags)!.Invoke(drag,[EventArgs.Empty]);Check(drags==1&&clicks==0,"dragging a panel suppresses its live-view click");
            typeof(CameraPreview).GetMethod("OnKeyDown",flags)!.Invoke(drag,[new KeyEventArgs(Keys.Enter)]);Check(clicks==1,"keyboard activation still works after dragging");
            return 0;
        }finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    private static void Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(5);while(!condition()){if(DateTime.UtcNow>end)throw new Exception("Grid interaction timed out");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS "+text);}
    private sealed class Source:IClipSource
    {
        public bool Enabled;public int Changes;public int Captures;
        public Task SetMotionAsync(int network,int camera,bool enabled){Enabled=enabled;Changes++;return Task.CompletedTask;}
        public Task<CameraOverview[]> CamerasAsync()=>Task.FromResult(new[]{new CameraOverview(99001,99002,"Interaction fixture","Outdoor","online","ok",Enabled?"Enabled":"Disabled")});
        public Task<byte[]> CameraImageAsync(int network,int camera,bool capture){if(capture)Captures++;using var bitmap=new Bitmap(16,16);using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Jpeg);return Task.FromResult(stream.ToArray());}
        public Task<SyncModule[]> ModulesAsync()=>Task.FromResult(Array.Empty<SyncModule>());
        public Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module)=>throw new NotSupportedException();
        public Task<byte[]> DownloadAsync(BlinkVideoInfo video)=>throw new NotSupportedException();
        public Task DeleteAsync(BlinkVideoInfo video)=>throw new NotSupportedException();
    }
}
