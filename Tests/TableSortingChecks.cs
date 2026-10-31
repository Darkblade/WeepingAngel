using System.ComponentModel;
using DontBlink;
internal static class TableSortingChecks
{
    private sealed record Row(string Name,string IpAddress,long Bytes,DateTime Recorded,string ReportedBytes);
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS "+text);}
    public static void Run()
    {
        var rows=new[]{new Row("Zulu","192.168.50.100",100,new DateTime(2026,10,29),"100"),new Row("Alpha","192.168.50.9",9,new DateTime(2025,12,31),"9"),new Row("Bravo","192.168.50.20",20,new DateTime(2026,1,1),"20")};
        var list=new SortableRows<Row>(rows);var source=(IBindingList)list;
        void Sort(string key,ListSortDirection direction=ListSortDirection.Ascending)=>source.ApplySort(TypeDescriptor.GetProperties(typeof(Row))[key]!,direction);
        Sort("IpAddress");Check(list.Select(x=>x.IpAddress).SequenceEqual(new[]{"192.168.50.9","192.168.50.20","192.168.50.100"}),"IP column sorting is numeric by address");
        Sort("Bytes",ListSortDirection.Descending);Check(list.Select(x=>x.Bytes).SequenceEqual(new long[]{100,20,9}),"byte counts sort numerically descending");
        Sort("ReportedBytes");Check(list.Select(x=>x.ReportedBytes).SequenceEqual(new[]{"9","20","100"}),"USB reported sizes sort numerically");
        Sort("Recorded");Check(list[0].Name=="Alpha"&&list[2].Name=="Zulu","dates sort chronologically");
        using var form=new Form();using var grid=new SortableGrid{Dock=DockStyle.Fill,AllowUserToAddRows=false};form.Controls.Add(grid);form.CreateControl();grid.CreateControl();grid.Bind(rows);
        Check(grid.Columns["Name"]!.SortMode==DataGridViewColumnSortMode.Automatic,"Windows data headers enable standard click sorting");grid.Sort(grid.Columns["Name"]!,ListSortDirection.Descending);
        grid.ClearSelection();var chosen=grid.Rows.Cast<DataGridViewRow>().Single(r=>((Row)r.DataBoundItem!).Name=="Alpha");chosen.Selected=true;
        grid.Bind(rows.Reverse());Check(((Row)grid.Rows[0].DataBoundItem!).Name=="Zulu"&&grid.Columns["Name"]!.HeaderCell.SortGlyphDirection==SortOrder.Descending,"grid refresh preserves sort order and visible indicator");
        Check(grid.SelectedRows.Cast<DataGridViewRow>().Any(r=>((Row)r.DataBoundItem!).Name=="Alpha"),"grid refresh retains selected row identity");
        Check(ThemePalette.All.ContainsKey("Autumn")&&ThemePalette.Migrate("Autumn")=="Autumn"&&ThemePalette.Migrate("System")=="Steel"&&!ThemePalette.All.ContainsKey("System"),"Autumn is available and legacy System migrates to Steel");
    }
}
