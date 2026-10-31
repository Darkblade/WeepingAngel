using DontBlink.Services;
namespace DontBlink;
public partial class Form1
{
    private Control AddLiveCaptureSettings(FlowLayoutPanel content)
    {
        var status=new TextBox{Name="live-capture-status",ReadOnly=true,Multiline=true,WordWrap=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,AccessibleName="Live capture status",Margin=Padding.Empty};
        AddRouterMonitoringControls(content,status);
        var fallback=AddDeviceSection(content,"capture-file-section");fallback.Controls.Add(new Label{Text="Growing-file fallback",AutoSize=true,Font=new Font("Segoe UI Semibold",14)});
        var actions=new FlowLayoutPanel{Name="capture-file-fallback",Visible=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(0,0,0,12),Margin=new Padding(3,3,3,20)};
        var follow=new ThemedButton{Name="follow-growing-capture",Text="Follow growing capture",AutoSize=true};var stop=new ThemedButton{Text="Stop following",AutoSize=true};actions.Controls.AddRange([follow,stop]);var toggle=new ThemedButton{Name="toggle-capture-file-fallback",Text="Show growing-file fallback",AutoSize=true};toggle.Click+=(_,_)=>{actions.Visible=!actions.Visible;toggle.Text=actions.Visible?"Hide growing-file fallback":"Show growing-file fallback";};fallback.Controls.Add(toggle);fallback.Controls.Add(actions);
        follow.Click+=async(_,_)=>{using var dialog=new OpenFileDialog{Filter="Growing captures|*.crdownload;*.eth;*.pcap|All files|*.*",Title="Follow the active ath0 download (new packets only)"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;follow.Enabled=false;try{await _lanDevices.ActivityMonitor.StartAsync(dialog.FileName);Log("Live capture following: "+Path.GetFileName(dialog.FileName)+"; old packets ignored.");}catch(Exception ex){status.Text=ex.Message;Log("Live capture error: "+ex.Message);}finally{follow.Enabled=true;}};
        stop.Click+=async(_,_)=>{await _lanDevices.ActivityMonitor.StopAsync();Log("Live capture following stopped.");};
        var timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=(_,_)=>{var state=_lanDevices.ActivityMonitor.Snapshot();var linked=_lanDevices.ActivityLinks();status.Text=state.State+(string.IsNullOrEmpty(state.FileName)?"":" · "+state.FileName)+"\r\n"+string.Join("\r\n",state.Activities.Select(a=>a.Name+": "+a.State+" · "+a.ObservedBytes.ToString("N0")+" bytes"+(linked.Any(l=>l.DeviceId==a.DeviceId)?"":" · NOT LINKED TO GUARD POST")));};timer.Start();content.Disposed+=(_,_)=>timer.Dispose();return status;
    }
}