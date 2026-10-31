namespace DontBlink;
public partial class Form1
{
    private void ConfigureArchiveMenu()
    {
        _archive.MultiSelect=true;_archive.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        var menu=new ContextMenuStrip{ShowImageMargin=false};var delete=new ToolStripMenuItem("Delete");menu.Items.Add(delete);_archive.ContextMenuStrip=menu;_archive.Disposed+=(_,_)=>menu.Dispose();
        _archive.CellMouseDown+=(_,e)=>{if(e.Button!=MouseButtons.Right||e.RowIndex<0)return;if(!_archive.Rows[e.RowIndex].Selected){_archive.ClearSelection();_archive.CurrentCell=_archive.Rows[e.RowIndex].Cells[Math.Max(0,e.ColumnIndex)];_archive.Rows[e.RowIndex].Selected=true;}};
        menu.Opening+=(_,_)=>{var palette=ThemePalette.Resolve((string)_themes.SelectedItem!);menu.BackColor=palette.ColorOf(palette.Surface);menu.ForeColor=palette.ColorOf(palette.Text);delete.Enabled=_archive.SelectedRows.Count>0;delete.Text=_archive.SelectedRows.Count>1?$"Delete {_archive.SelectedRows.Count} recordings":"Delete";};
        delete.Click+=async(_,_)=>{
            var rows=_archive.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.DataBoundItem).OfType<ArchiveRow>().ToArray();if(rows.Length==0)return;
            if(MessageBox.Show(this,$"Permanently delete {rows.Length} selected recording(s) from the network archive?","Delete recordings",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            await RunAsync(async()=>{await Task.Run(()=>{foreach(var row in rows){try{_clips.DeleteArchive(row.Key);Log("Deleted archive recording: "+row.Key);}catch(Exception ex){Log("Archive delete failed: "+ex.Message);}}});await ReloadArchiveAsync();});
        };
    }
}