using DontBlink.Services;
using System.Reflection;
internal static class DeviceTableLinkChecks
{
    private sealed class PreviewForm:DontBlink.Form1 { protected override void OnShown(EventArgs e){} }
    public static int Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"WA-table-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try{
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var store=new DeviceRegistry(Path.Combine(root,"devices.json"));
            for(int i=1;i<=10;i++)store.Save(new(Guid.Empty,"Extra "+i,"blink-outdoor-3","192.168.50."+(120+i),"02:00:00:00:00:"+i.ToString("X2"),store.List().Single(d=>d.Type=="blink-sync-2").Id));
            using var form=new PreviewForm();
            typeof(DontBlink.Form1).GetField("_lanDevices",flags)!.SetValue(form,store);
            typeof(DontBlink.Form1).GetField("_cameraItems",flags)!.SetValue(form,new[]{new CameraOverview(3,2,"Garden camera","Outdoor","online","ok","Enabled")});
            using var page=(TabPage)typeof(DontBlink.Form1).GetMethod("BuildDeviceSettings",flags)!.Invoke(form,null)!;
            using var host=new Form{Width=1100,Height=800,ShowInTaskbar=false,Location=new Point(-2000,-2000),StartPosition=FormStartPosition.Manual};
            var tabs=new TabControl{Dock=DockStyle.Fill};host.Controls.Add(tabs);tabs.TabPages.Add(page);host.Show();Application.DoEvents();
            var grid=page.Controls.Find("lan-device-list",true).OfType<DataGridView>().Single();
            var row=grid.Rows.Cast<DataGridViewRow>().Single(r=>r.DataBoundItem is LanDevice d&&d.Type=="blink-outdoor-3"&&d.Name.Contains("Garden"));
            grid.CurrentCell=row.Cells["GuardPostCamera"];grid.BeginEdit(true);Application.DoEvents();
            var editor=(ComboBox)grid.EditingControl!;editor.SelectedValue="blink:2:3";grid.CommitEdit(DataGridViewDataErrorContexts.Commit);grid.EndEdit();Application.DoEvents();
            Check(store.ActivityLinks().Single().CameraId==3,"LAN table dropdown saves the selected Guard Post camera");
            Check(new DeviceRegistry(Path.Combine(root,"devices.json")).ActivityLinks().Single().CameraId==3,"table camera mapping survives restart");
            typeof(DontBlink.Form1).GetField("_cameraItems",flags)!.SetValue(form,Array.Empty<CameraOverview>());
            grid.CurrentCell=row.Cells["GuardPostCamera"];grid.BeginEdit(true);grid.EndEdit();Application.DoEvents();
            Check((string)row.Cells["GuardPostCamera"].Value! =="blink:2:3","saved mapping remains available before camera discovery");
            row.Cells["GuardPostCamera"].Value="";Check(store.ActivityLinks().Length==0,"Not linked removes the saved camera mapping");
            Check(grid.Rows.Cast<DataGridViewRow>().Single(r=>r.DataBoundItem is LanDevice d&&d.Type=="blink-sync-2").Cells["GuardPostCamera"].ReadOnly,"module rows do not accept camera links");
            var sectionNames=new[]{"lan-device-table-section","lan-device-editor-section","capture-replay-section","router-monitoring-section","external-events-section","capture-file-section"};
            var sections=sectionNames.Select(n=>page.Controls.Find(n,true).Single()).ToArray();Check(sections.All(s=>s is DontBlink.ConsoleSection)&&sections[2].Parent?.Name=="advanced-diagnostics-content"&&sections[5].Parent==sections[2].Parent,"replay and fallback use framed containers inside Advanced Diagnostics");
            Check(page.Controls.Find("activity-camera-link",true).Length==0,"separate camera-link controls are removed");
                        Check(grid.Height>=320&&grid.Rows.Count==8,"LAN table shows a taller eight-row page");
            var next=page.Controls.Find("lan-page-next",true).OfType<Button>().Single();next.PerformClick();Application.DoEvents();Check(grid.Rows.Count==4&&page.Controls.Find("lan-page-previous",true).Single().Enabled,"Next page displays the remaining LAN devices");
            var sizes=page.Controls.Find("lan-page-size",true).OfType<ComboBox>().Single();sizes.SelectedIndex=0;Application.DoEvents();Check(grid.Rows.Count==5&&page.Controls.Find("lan-page-label",true).Single().Text.Contains("Page 1 of 3"),"page size resets pagination and updates total pages: "+grid.Rows.Count+" / "+page.Controls.Find("lan-page-label",true).Single().Text);
            var before=store.List();var hub=before.Single(d=>d.Type=="blink-sync-2");
            var moduleRow=grid.Rows.Cast<DataGridViewRow>().Single(r=>r.DataBoundItem is LanDevice d&&d.Id==hub.Id);grid.CurrentCell=moduleRow.Cells[0];moduleRow.Selected=true;Application.DoEvents();
            IEnumerable<Control> Walk(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Walk(child)));
            Walk(page).OfType<Button>().Single(b=>b.Text=="New device").PerformClick();Application.DoEvents();
            var parentBox=page.Controls.Find("lan-device-parent",true).OfType<ComboBox>().Single();var typeBox=page.Controls.Find("lan-device-type",true).OfType<ComboBox>().Single();
            Check(parentBox.Enabled&&parentBox.Items.Count==2&&parentBox.SelectedIndex==1&&((LanDeviceType)typeBox.SelectedItem!).Id=="blink-outdoor-3","New from a selected module retains that module as an available parent");
            page.Controls.Find("lan-device-name",true).OfType<TextBox>().Single().Text="New camera regression";page.Controls.Find("lan-device-ip",true).OfType<TextBox>().Single().Text="192.168.50.230";page.Controls.Find("lan-device-mac",true).OfType<TextBox>().Single().Text="02:AA:00:00:00:01";
            Walk(page).OfType<Button>().Single(b=>b.Text=="Save device").PerformClick();Application.DoEvents();
            var added=store.List().Single(d=>d.Name=="New camera regression");Check(store.List().Length==before.Length+1&&added.ParentId==hub.Id&&before.All(old=>store.List().Contains(old)),"New camera saves a new ID without overwriting any registered device");
            Walk(page).OfType<Button>().Single(b=>b.Text=="New device").PerformClick();Application.DoEvents();typeBox.SelectedValue="blink-sync-2";Check(!parentBox.Enabled,"module creation disables parent selection");typeBox.SelectedValue="blink-outdoor-3";Check(parentBox.Enabled&&parentBox.Items.Count==2,"switching back to camera restores parent selection");
            Check(page.Controls.Find("lan-device-export",true).Length==1&&page.Controls.Find("lan-device-import",true).Length==1,"Windows editor exposes device/link export and import controls");
            return 0;
        }finally{Directory.Delete(root,true);}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
}