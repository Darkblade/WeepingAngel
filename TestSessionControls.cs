using DontBlink.Services;
namespace DontBlink;
public partial class Form1
{
    private void AddTestSessionControls(FlowLayoutPanel content)
    {
        var section=AddDeviceSection(content,"test-session-section");section.Controls.Add(new Label{Text="Live test session",AutoSize=true,Font=new Font("Segoe UI Semibold",14)});
        section.Controls.Add(new Label{Text="Records packet headers, activity summaries and action markers for live analysis. No packet payload. Router monitoring must be running separately. Starting a test replaces the previous rolling log.",AutoSize=true,MaximumSize=new Size(650,0)});
        var label=new TextBox{Width=400,MaxLength=300,PlaceholderText="Test name (e.g. Garden camera motion baseline)",AccessibleName="Test session name"};section.Controls.Add(label);
        var actions=new FlowLayoutPanel{AutoSize=true,WrapContents=true};var start=new ThemedButton{Text="Start test session",AutoSize=true,Presentation="Compact"};var stop=new ThemedButton{Text="Stop test session",AutoSize=true,Presentation="Compact"};var copy=new ThemedButton{Text="Copy log path",AutoSize=true,Presentation="Compact"};var open=new ThemedButton{Text="Open log folder",AutoSize=true,Presentation="Compact"};actions.Controls.AddRange([start,stop,copy,open]);section.Controls.Add(actions);
        var marker=new TextBox{Width=400,MaxLength=300,PlaceholderText="Physical action (e.g. Garden camera motion triggered)",AccessibleName="Test event marker"};section.Controls.Add(marker);var mark=new ThemedButton{Text="Mark test event",AutoSize=true,Presentation="Compact"};section.Controls.Add(mark);
        var status=new TextBox{Name="test-session-status",Width=650,Height=90,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};section.Controls.Add(status);
        var log=_lanDevices.ActivityMonitor.TestLog;string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DontBlink","diagnostics");
        void Show(){var state=log.Snapshot();start.Enabled=!state.Active;stop.Enabled=mark.Enabled=state.Active;copy.Enabled=open.Enabled=state.Path!=null;status.Text=(state.Active?"Recording":"Stopped")+" · Session "+(state.SessionId??"none")+"\r\n"+(state.Path??Path.Combine(folder,"current.jsonl"))+"\r\nDropped metadata records: "+state.Dropped+(state.Error==null?"":"\r\n"+state.Error);}
        start.Click+=(_,_)=>{try{log.Start(folder,label.Text);log.Record("inventory",new{devices=_lanDevices.List(),links=_lanDevices.ActivityLinks()});log.Record("monitor",_lanDevices.ActivityMonitor.Snapshot());Log("Test session started: "+log.Snapshot().Path);}catch(Exception ex){Log("Test session: "+ex.Message);}Show();};
        stop.Click+=async(_,_)=>{await log.StopAsync();Show();Log("Test session stopped and flushed.");};mark.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(marker.Text))return;log.Marker(marker.Text);Log("Test marker: "+marker.Text);marker.Clear();};copy.Click+=(_,_)=>CopyConnectionText(log.Snapshot().Path??"");open.Click+=(_,_)=>Open(folder);
        var timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=(_,_)=>{if(log.Snapshot().Active)log.Record("monitor",_lanDevices.ActivityMonitor.Snapshot());Show();};timer.Start();section.Disposed+=(_,_)=>timer.Dispose();Show();
    }
}