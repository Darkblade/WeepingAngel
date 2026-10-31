using System.ComponentModel;
using System.Net;

namespace DontBlink;

internal sealed class SortableRows<T>(IEnumerable<T> rows) : BindingList<T>(rows.ToList())
{
    private PropertyDescriptor? _property;
    private ListSortDirection _direction;
    protected override bool SupportsSortingCore=>true;
    protected override bool IsSortedCore=>_property!=null;
    protected override PropertyDescriptor? SortPropertyCore=>_property;
    protected override ListSortDirection SortDirectionCore=>_direction;
    protected override void ApplySortCore(PropertyDescriptor property,ListSortDirection direction)
    {
        var ordered=Items.Select((item,index)=>(item,index)).ToList();
        ordered.Sort((a,b)=>{int result=CompareValues(property.GetValue(a.item),property.GetValue(b.item),property.Name);if(direction==ListSortDirection.Descending)result=-result;return result==0?a.index.CompareTo(b.index):result;});
        for(int i=0;i<ordered.Count;i++)Items[i]=ordered[i].item;
        _property=property;_direction=direction;OnListChanged(new ListChangedEventArgs(ListChangedType.Reset,-1));
    }
    protected override void RemoveSortCore(){_property=null;}
    internal static int CompareValues(object? left,object? right,string property)
    {
        if(ReferenceEquals(left,right))return 0;if(left==null)return -1;if(right==null)return 1;
        if(left is string a&&right is string b){
            if((property.Contains("Address")||property=="Ip")&&IPAddress.TryParse(a,out var x)&&IPAddress.TryParse(b,out var y)){var xb=x.GetAddressBytes();var yb=y.GetAddressBytes();int n=xb.Length.CompareTo(yb.Length);if(n!=0)return n;for(int i=0;i<xb.Length;i++){n=xb[i].CompareTo(yb[i]);if(n!=0)return n;}return 0;}
            if(decimal.TryParse(a,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var an)&&decimal.TryParse(b,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var bn))return an.CompareTo(bn);
            if(property=="Type"){a=Services.DeviceRegistry.Types.FirstOrDefault(t=>t.Id==a)?.Label??a;b=Services.DeviceRegistry.Types.FirstOrDefault(t=>t.Id==b)?.Label??b;}
            return StringComparer.CurrentCultureIgnoreCase.Compare(a,b);
        }
        return left is IComparable comparable?comparable.CompareTo(right):StringComparer.CurrentCultureIgnoreCase.Compare(left.ToString(),right.ToString());
    }
}

internal sealed class SortableGrid : DataGridView
{
    private string? _sortProperty;
    private ListSortDirection _direction;
    protected override void OnSorted(EventArgs e){_sortProperty=SortedColumn?.DataPropertyName;_direction=SortOrder==SortOrder.Descending?ListSortDirection.Descending:ListSortDirection.Ascending;base.OnSorted(e);}
    protected override void OnColumnAdded(DataGridViewColumnEventArgs e){if(e.Column is DataGridViewTextBoxColumn or DataGridViewCheckBoxColumn)e.Column.SortMode=DataGridViewColumnSortMode.Automatic;base.OnColumnAdded(e);}
    public void Bind<T>(IEnumerable<T> rows)
    {
        string? property=_sortProperty;var direction=_direction;
        var selected=SelectedRows.Cast<DataGridViewRow>().Select(r=>r.DataBoundItem).Where(x=>x!=null).ToHashSet();
        var list=new SortableRows<T>(rows);
        if(property!=null&&TypeDescriptor.GetProperties(typeof(T))[property] is { } descriptor)((IBindingList)list).ApplySort(descriptor,direction);
        DataSource=list;
        var column=Columns.Cast<DataGridViewColumn>().FirstOrDefault(c=>c.DataPropertyName==property);
        if(column!=null){Sort(column,direction);_sortProperty=property;_direction=direction;}
        if(selected.Count>0){ClearSelection();foreach(DataGridViewRow row in Rows)if(selected.Contains(row.DataBoundItem))row.Selected=true;}
    }
}
