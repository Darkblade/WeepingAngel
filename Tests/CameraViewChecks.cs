using DontBlink.Services;
using System.Reflection;

internal static class CameraViewChecks
{
    private sealed class PreviewForm : DontBlink.Form1 { protected override void OnShown(EventArgs e) { } }
    public static int Run()
    {
        using var form = new PreviewForm { StartPosition = FormStartPosition.Manual, Location = new Point(-2000,-2000), ShowInTaskbar = false };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(DontBlink.Form1).GetField(name,flags)!.GetValue(form)!;
        void View(bool grid) => typeof(DontBlink.Form1).GetMethod("ApplyCameraView",flags)!.Invoke(form,[grid]);
        var cards = Field<FlowLayoutPanel>("_cameraCards"); var list = Field<DataGridView>("_cameras");
        var images = Field<Dictionary<(int,int),Image>>("_cameraImages");
        var preview=new Bitmap(640,360);using(var g=Graphics.FromImage(preview)){g.Clear(Color.FromArgb(16,47,64));using var font=new Font("Segoe UI",26);g.DrawString("Test camera preview",font,Brushes.White,125,155);}images[(2,1)]=preview;
        var cameras = new[] { new CameraOverview(1,2,"Thumbnail test","Outdoor","online","ok","Enabled") };
        typeof(DontBlink.Form1).GetField("_cameraItems",flags)!.SetValue(form,cameras);
        list.DataSource = cameras.ToList(); list.Columns.Insert(0,new DataGridViewImageColumn { Name="Preview" });
        typeof(DontBlink.Form1).GetMethod("BuildCameraViews",flags)!.Invoke(form,null);
        View(true); // Apply desired Grid state while the form and tab are hidden.
        var cameraTab = (TabPage)Field<Panel>("_cameraHost").Parent!.Parent!;
        var tabs = (TabControl)cameraTab.Parent!; tabs.SelectedTab = cameraTab;
        form.Show(); Application.DoEvents();
        Check(form.Text=="Weeping Angel","project branding is Weeping Angel");
        Check(tabs.TabPages.Cast<TabPage>().Select(p=>p.Text).Order().SequenceEqual(new[]{"Clips","Home","Settings","USB storage","Devices","Phone Access"}.Order()),"Devices and Phone Access have their own navigation pages");
        var theme=Field<ComboBox>("_themes");var log=Field<ListBox>("_log");
        var setLog=Field<Action<bool>>("_setDebugLogCollapsed");setLog(false);Application.DoEvents();
        Check(log.Height>=150,"expanded log is taller than the previous layout");
        var logToolbar=(FlowLayoutPanel)log.Parent!.Controls[0];
        Check(logToolbar.Controls.OfType<Label>().Any(l=>l.Text=="Diagnostics"),"collapsed diagnostics retain a clear heading");
        Check(!logToolbar.Controls.OfType<Button>().Any(b=>b.Text=="Copy debug log")&&log.ContextMenuStrip?.Items.OfType<ToolStripMenuItem>().Any(i=>i.Text=="Copy debug log")==true,"copy log is available only in the right-click menu");
        Check(logToolbar.Controls.OfType<Button>().Single().Text=="−","expanded log toggle is a minus symbol");
        int expandedLogHeight=log.Parent.Height;setLog(true);Application.DoEvents();
        Check(!log.Visible&&log.Parent.Height<expandedLogHeight&&logToolbar.Controls.OfType<Button>().Any(b=>b.Text=="+"&&b.Visible),"collapsed log frees space while retaining the toolbar");
        setLog(false);Application.DoEvents();Check(log.Visible,"log expands again");
        string logSettingsPath=Path.Combine(Path.GetTempPath(),"WeepingAngel-log-"+Guid.NewGuid().ToString("N")+".json");
        try{new AppSettings(DebugLogCollapsed:true).Save(logSettingsPath);Check(AppSettings.Load(logSettingsPath).DebugLogCollapsed,"collapsed log preference survives a settings reload");new AppSettings(DebugLogCollapsed:false).Save(logSettingsPath);Check(!AppSettings.Load(logSettingsPath).DebugLogCollapsed,"expanded log preference survives a settings reload");}
        finally{if(File.Exists(logSettingsPath))File.Delete(logSettingsPath);}

        Check(theme.Visible&&log.Visible,"theme dropdown and debug log are persistent on Home");
        Check(cards.Controls.Cast<Control>().SelectMany(c=>c.Controls.OfType<DontBlink.CameraPreview>()).All(p=>!p.LiveAvailable),"signed-out cameras retain disabled live access while allowing local arrangement");
        var panel=cards.Controls[0];var photo=panel.Controls.OfType<PictureBox>().Single();typeof(DontBlink.Form1).GetMethod("BuildCameraViews",flags)!.Invoke(form,null);Check(ReferenceEquals(panel,cards.Controls[0])&&ReferenceEquals(photo,cards.Controls[0].Controls.OfType<PictureBox>().Single()),"overview refresh reuses existing camera panels and previews");
        Check(panel.Region!=null&&photo.Region!=null&&!photo.Region.IsVisible(0,0),"camera panels and thumbnails have rounded corners");
        Check(photo.Controls.OfType<LinkLabel>().Single().Text=="…"&&!panel.Controls.OfType<Button>().Any(),"camera options are a link over the thumbnail with no footer buttons");
        bool wasEnabled=panel.Enabled;panel.Enabled=true;var optionsLink=photo.Controls.OfType<LinkLabel>().Single();
        for(int attempt=0;attempt<3;attempt++){
            typeof(LinkLabel).GetMethod("OnLinkClicked",flags)!.Invoke(optionsLink,[new LinkLabelLinkClickedEventArgs(optionsLink.Links[0])]);Application.DoEvents();
            var menu=optionsLink.ContextMenuStrip!;Check(!menu.IsDisposed&&menu.Items.Count==2&&menu.Items[0].Text=="Update image"&&menu.Items[1].Text=="Settings"&&!optionsLink.Parent!.Controls.Cast<Control>().Any(c=>c.Name=="camera-settings"),"camera menu contains Update image and Settings without a separate Settings button");
            menu.Close(ToolStripDropDownCloseReason.AppClicked);Application.DoEvents();Check(!menu.IsDisposed&&!menu.Visible&&!form.IsDisposed,"outside-click dismissal keeps the camera menu and window alive");
        }
        panel.Enabled=wasEnabled;
        Check(photo.Controls.OfType<LinkLabel>().Single().Bottom<=photo.ClientSize.Height,"options link stays inside the thumbnail overlay");
        Check(DontBlink.CameraBattery.Level(cameras[0])==(100,true)&&DontBlink.CameraBattery.Level(cameras[0] with{Battery="low"})==(10,true)&&DontBlink.CameraBattery.Level(cameras[0] with{Battery="72%"})==(72,false),"battery estimates are distinguished from measured percentages");
        Check(DontBlink.CameraPreview.MotionLabel(cameras[0])=="Motion recording: On"&&DontBlink.CameraPreview.MotionLabel(cameras[0] with{Motion="Disabled"})=="Motion recording: Off","motion labels describe triggered recording rather than camera power");
        Check(cards.Visible && !list.Visible,"Grid remains selected when its previously hidden tab is shown");
        Check(cards.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).OfType<PictureBox>().Any(p=>p.Image!=null),"camera grid contains bound thumbnail images");
        View(false); Application.DoEvents();
        Check(list.Visible && !cards.Visible && list.Rows[0].Cells["Preview"].FormattedValue is Image,"List view displays thumbnail images and hides cards");
        tabs.SelectedIndex=0; View(true); tabs.SelectedTab=cameraTab; Application.DoEvents();
        Check(cards.Visible && !list.Visible && cards.Parent!.Controls.GetChildIndex(cards)==0,"hidden-tab view updates preserve Grid and correct stacking order");
        string previews=Path.Combine(Directory.GetCurrentDirectory(),"docs","previews");Directory.CreateDirectory(previews);
        using(var snapshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));snapshot.Save(Path.Combine(previews,"desktop-home.png"),System.Drawing.Imaging.ImageFormat.Png);}
        var settings=tabs.TabPages.Cast<TabPage>().Single(p=>p.Text=="Settings");tabs.SelectedTab=settings;Application.DoEvents();
        TabControl SettingsTabs(Control control){foreach(Control child in control.Controls){if(child is TabControl found)return found;try{return SettingsTabs(child);}catch(InvalidOperationException){}}throw new InvalidOperationException("Settings tabs missing");}
        var devices=tabs.TabPages.Cast<TabPage>().Single(p=>p.Text=="Devices");tabs.SelectedTab=devices;var settingsTabs=SettingsTabs(devices);
        var devicePage=settingsTabs.TabPages.Cast<TabPage>().Single(p=>p.Text=="LAN devices");settingsTabs.SelectedTab=devicePage;Application.DoEvents();
        var deviceGrid=form.Controls.Find("lan-device-list",true).OfType<DataGridView>().Single();Check(deviceGrid.Visible&&deviceGrid.Rows.Count>=2,"LAN device settings show the prefilled module and camera while signed out");
        var captureStatus=form.Controls.Find("live-capture-status",true).OfType<TextBox>().Single();var captureArea=form.Controls.Find("live-capture-status-area",true).Single();var deviceLayout=form.Controls.Find("device-settings-layout",true).Single();var captureScroll=deviceLayout.Controls.OfType<FlowLayoutPanel>().Single();
        bool CaptureReadable(){var bounds=captureStatus.RectangleToScreen(captureStatus.ClientRectangle);var pageBounds=devicePage.RectangleToScreen(devicePage.ClientRectangle);var diagnostics=log.Parent!.RectangleToScreen(log.Parent.ClientRectangle);return pageBounds.Contains(bounds)&&!diagnostics.IntersectsWith(bounds)&&captureStatus.Height>=50;}
        captureStatus.Text=string.Join(Environment.NewLine,Enumerable.Repeat("Garden camera: Possible recording · 1,000,000 observed bytes",12));
        Check(CaptureReadable()&&captureStatus.Multiline&&captureStatus.ScrollBars==ScrollBars.Vertical,"capture status has reserved scrollable space above Diagnostics");
        var advanced=form.Controls.Find("advanced-diagnostics-content",true).Single();Check(!advanced.Visible,"Advanced Diagnostics is collapsed by default");var advancedLink=form.Controls.Find("advanced-diagnostics-toggle",true).OfType<LinkLabel>().Single();typeof(Control).GetMethod("OnClick",flags)!.Invoke(advancedLink,[EventArgs.Empty]);Application.DoEvents();Check(advanced.Visible,"Advanced Diagnostics expands replay and fallback tools");
        var fallbackToggle=form.Controls.Find("toggle-capture-file-fallback",true).OfType<Button>().Single();Check(!form.Controls.Find("capture-file-fallback",true).Single().Visible,"growing-file capture is an optional collapsed fallback");fallbackToggle.PerformClick();Application.DoEvents();
        var followCapture=form.Controls.Find("follow-growing-capture",true).Single();captureScroll.ScrollControlIntoView(followCapture.Parent!);Application.DoEvents();
        Check(followCapture.RectangleToScreen(followCapture.ClientRectangle).Bottom<=captureArea.RectangleToScreen(captureArea.ClientRectangle).Top,"capture controls scroll into view without covering status");
        using(var snapshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));snapshot.Save(Path.Combine(previews,"desktop-devices.png"),System.Drawing.Imaging.ImageFormat.Png);}
        var originalDeviceSize=form.Size;form.Size=form.MinimumSize;Application.DoEvents();Check(CaptureReadable(),"capture status remains above Diagnostics at minimum size");setLog(true);Application.DoEvents();Check(CaptureReadable(),"capture status remains readable with Diagnostics collapsed");setLog(false);Application.DoEvents();
        using(var snapshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));snapshot.Save(Path.Combine(previews,"desktop-devices-small.png"),System.Drawing.Imaging.ImageFormat.Png);}form.Size=originalDeviceSize;Application.DoEvents();
        tabs.SelectedTab=settings;Application.DoEvents();Check(form.Controls.Find("accessibility-section",true).Single().Visible&&form.Controls.Find("recording-overlays-section",true).Single().Parent==form.Controls.Find("accessibility-section",true).Single().Parent,"Settings stacks framed accessibility and recording sections");tabs.SelectedTab=devices;settingsTabs.SelectedTab=settingsTabs.TabPages.Cast<TabPage>().Single(p=>p.Text=="Connection");Application.DoEvents();
        Check(theme.Visible&&log.Visible,"debug log and theme selector remain visible in Settings");
        var split=form.Controls.Find("workspace-log-split",true).OfType<SplitContainer>().Single();
        Check(log.Parent!.Parent!.Parent==split.Panel2,"debug log has its own isolated container");
        bool Separated()=>!split.Panel1.RectangleToScreen(split.Panel1.ClientRectangle).IntersectsWith(split.Panel2.RectangleToScreen(split.Panel2.ClientRectangle));
        Check(Separated(),"expanded log is separated from Connection content");
        var normalSize=form.Size;form.Size=form.MinimumSize;Application.DoEvents();Check(Separated(),"log and Connection stay separated at the minimum window size");
        setLog(true);Application.DoEvents();Check(Separated()&&!log.Visible,"collapsed log remains separated from Connection");
        setLog(false);form.Size=normalSize;Application.DoEvents();

        Field<TextBox>("_email").Text="";Field<TextBox>("_password").Text="";
        tabs.SelectedTab=tabs.TabPages.Cast<TabPage>().Single(p=>p.Text=="Phone Access");Application.DoEvents();var lan=Field<CheckBox>("_lan");lan.Checked=false;var phoneOptions=form.Controls.Find("phone-options",true).Single();
        Check(!phoneOptions.Visible,"phone controls are hidden until enabled");lan.Checked=true;Application.DoEvents();
        var actions=phoneOptions.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).OfType<Button>().ToArray();
        Check(actions.Single(b=>b.Text=="Start phone interface").Visible&&!actions.Single(b=>b.Text=="Stop phone interface").Visible,"only Start is shown while phone interface is stopped");
        var simulatedServer=new MobileServer(new ClipService(new EmptySource(),Path.GetTempPath()),0);
        typeof(DontBlink.Form1).GetField("_server",flags)!.SetValue(form,simulatedServer);Field<Action>("_phoneUiChanged")();
        Check(!actions.Single(b=>b.Text=="Start phone interface").Visible&&actions.Single(b=>b.Text=="Stop phone interface").Visible,"Start and Stop share their slot based on server state");
        typeof(DontBlink.Form1).GetField("_server",flags)!.SetValue(form,null);Field<Action>("_phoneUiChanged")();simulatedServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        typeof(DontBlink.Form1).GetMethod("ApplyTheme",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[form,DontBlink.ThemePalette.All["Gold"]]);
        using(var snapshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));snapshot.Save(Path.Combine(previews,"desktop-connection-gold.png"),System.Drawing.Imaging.ImageFormat.Png);}
        settingsTabs.SelectedTab=devicePage;Application.DoEvents();
        typeof(DontBlink.Form1).GetMethod("ApplyTheme",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[form,DontBlink.ThemePalette.All["Autumn"]]);
        using(var snapshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));snapshot.Save(Path.Combine(previews,"desktop-devices-autumn.png"),System.Drawing.Imaging.ImageFormat.Png);}
        Check(!theme.Items.Contains("System")&&theme.Items.Contains("Autumn"),"Windows theme choices include Autumn and exclude System");
        tabs.SelectedTab=tabs.TabPages.Cast<TabPage>().Single(p=>p.Text=="USB storage");Application.DoEvents();var usbFilter=Field<ComboBox>("_filter");Check(usbFilter.Items.Count==0&&usbFilter.DropDownStyle==ComboBoxStyle.DropDownList,"USB camera dropdown remains empty before clips load");
        typeof(DontBlink.Form1).GetField("_items",flags)!.SetValue(form,new List<ClipItem>{new("a",1,2,"Garden",DateTime.UtcNow,"12"),new("b",1,2,"Door",DateTime.UtcNow,"12")});typeof(DontBlink.Form1).GetMethod("BindSource",flags)!.Invoke(form,null);usbFilter.SelectedItem="Garden";Application.DoEvents();Check(Field<DataGridView>("_source").Rows.Count==1,"USB dropdown filters loaded clips by exact camera name");Check(usbFilter.RectangleToScreen(usbFilter.ClientRectangle).Top>Field<ComboBox>("_modules").RectangleToScreen(Field<ComboBox>("_modules").ClientRectangle).Bottom,"USB camera dropdown is below module selection");
        using(var snapshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));snapshot.Save(Path.Combine(previews,"desktop-usb-navigation.png"),System.Drawing.Imaging.ImageFormat.Png);}
        tabs.SelectedTab=settings;Application.DoEvents();using(var snapshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));snapshot.Save(Path.Combine(previews,"desktop-settings-stacked.png"),System.Drawing.Imaging.ImageFormat.Png);}
        form.Dispose();
        return 0;
    }
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS "+text);}
    private sealed class EmptySource:IClipSource
    {
        public Task<Blink.Models.SyncModule[]> ModulesAsync()=>Task.FromResult(Array.Empty<Blink.Models.SyncModule>());
        public Task<IEnumerable<Blink.Models.BlinkVideoInfo>> ListAsync(Blink.Models.SyncModule module)=>Task.FromResult(Enumerable.Empty<Blink.Models.BlinkVideoInfo>());
        public Task<byte[]> DownloadAsync(Blink.Models.BlinkVideoInfo video)=>throw new NotSupportedException();
        public Task DeleteAsync(Blink.Models.BlinkVideoInfo video)=>throw new NotSupportedException();
    }
}

