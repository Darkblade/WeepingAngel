using DontBlink.Services;

namespace DontBlink;

public partial class Form1
{
    private readonly Queue<(string Text,int Level)> _diagnosticEntries=new();
    private int _diagnosticLevel=DiagnosticLevel(AppSettings.Load().DiagnosticVerbosity);
    private readonly Label _transferStatus=new(){AutoSize=true,AutoEllipsis=true,AccessibleName="Archive progress",Padding=new Padding(12,0,12,0)};
    private readonly Label _systemSummary=new(){AutoSize=true,MaximumSize=new Size(900,0),AccessibleName="Service and system status",Margin=new Padding(8)};
    private readonly ComboBox _systemScope=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=220,AccessibleName="Camera service and system",DisplayMember="Name"};
    private bool _bindingSystemScope;
    private sealed record SystemChoice(string ServiceId,int? Id,string Name);
    private static int DiagnosticLevel(string name)=>name switch{"Errors"=>0,"Detailed"=>2,"Trace"=>3,_=>1};
    private void LogAt(string text,int level)
    {
        if(IsDisposed||_closing)return;if(InvokeRequired){BeginInvoke(()=>LogAt(text,level));return;}
        string line=$"[{DateTime.Now:T}] {text}";_diagnosticEntries.Enqueue((line,level));while(_diagnosticEntries.Count>5000)_diagnosticEntries.Dequeue();
        if(level<=_diagnosticLevel){_log.Items.Insert(0,line);while(_log.Items.Count>5000)_log.Items.RemoveAt(5000);}
    }
    private void SetDiagnosticLevel(string name)
    {
        _diagnosticLevel=DiagnosticLevel(name);_log.BeginUpdate();try{_log.Items.Clear();foreach(var entry in _diagnosticEntries.Reverse().Where(e=>e.Level<=_diagnosticLevel))_log.Items.Add(entry.Text);}finally{_log.EndUpdate();}
        (AppSettings.Load() with{DiagnosticVerbosity=name}).Save();
    }
    private void CopyProgress(ArchiveProgress progress)
    {
        if(IsDisposed||_closing)return;if(InvokeRequired){BeginInvoke(()=>CopyProgress(progress));return;}
        _transferStatus.Text=progress.Total==0?"Archive: no clips queued":$"Archive {progress.Processed}/{progress.Total} · {progress.Stage} · ETA "+(progress.EtaSeconds is double eta?"~"+TimeSpan.FromSeconds(eta).ToString(@"hh\:mm\:ss"):"estimating");
        _cameraTips.SetToolTip(_transferStatus,progress.Summary+(string.IsNullOrEmpty(progress.Camera)?"":" · "+progress.Camera));
    }
    private async Task UpdateSystemSummaryAsync()
    {
        try{
            var snapshot=await _clips.ManagementAsync(false,Token);var selected=_systemScope.SelectedItem as SystemChoice;_bindingSystemScope=true;
            try{_systemScope.Items.Clear();_systemScope.Items.Add(new SystemChoice("blink",null,"Blink / All systems"));foreach(var system in snapshot.Systems)_systemScope.Items.Add(new SystemChoice(system.ServiceId,system.Id,system.ServiceId+" / "+system.Name));_systemScope.SelectedItem=_systemScope.Items.Cast<SystemChoice>().FirstOrDefault(s=>s.Id==selected?.Id&&s.ServiceId==(selected?.ServiceId??"blink"))??_systemScope.Items[0];}finally{_bindingSystemScope=false;}
            _systemSummary.Text=snapshot.Systems.Length==0?"System status not reported. Open Services & devices to refresh.":string.Join("   ·   ",snapshot.Systems.Select(s=>$"{s.Name}: {(s.Armed==true?"Armed":s.Armed==false?"Disarmed":"State unknown")} / Sync Module {s.ModuleStatus}"))+(snapshot.Cached?" (cached)":"");
        }catch(Exception ex){_systemSummary.Text="System details unavailable. Open Services & devices to retry.";LogAt("System status: "+ex.Message,2);}
    }
    private bool CameraInScope(CameraOverview camera)=>_systemScope.SelectedItem is not SystemChoice scope||scope.Id==null||camera.ServiceId==scope.ServiceId&&camera.NetworkId==scope.Id;
    private async Task OpenManagementAsync(CameraOverview? camera=null)
    {
        if(_server==null)await StartServerAsync();string route="/management";if(camera!=null)route+=$"?service={Uri.EscapeDataString(camera.ServiceId)}&network={camera.NetworkId}&camera={camera.Id}";await OpenPlayerWindowAsync(camera==null?"Services and devices":camera.Name+" settings",route);
    }
}
