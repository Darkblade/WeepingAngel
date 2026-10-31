using DontBlink.Services;
namespace DontBlink;
public partial class Form1
{
    private ConsoleSection AddDeviceSection(FlowLayoutPanel parent,string name)
    {
        var section=new ConsoleSection{Name=name,Margin=new Padding(0,0,0,14),Palette=ThemePalette.Resolve(AppSettings.Load().Theme)};parent.Controls.Add(section);
        void Fit(){int width=Math.Max(260,parent.ClientSize.Width-parent.Padding.Horizontal-24);if(parent.Name=="settings-sections")width=Math.Min(width,section.Name=="accessibility-section"?440:600);section.MinimumSize=new Size(width,0);section.MaximumSize=new Size(width,0);section.Width=width;int inner=Math.Max(200,width-section.Padding.Horizontal);foreach(Control child in section.Controls){if(child is Label label&&label.AutoSize)label.MaximumSize=new Size(inner,0);if(child is FlowLayoutPanel flow){flow.Width=inner;flow.MaximumSize=new Size(inner,0);}if(child is DataGridView||child is TextBox{Multiline:true})child.Width=inner;}}
        parent.SizeChanged+=(_,_)=>Fit();section.ControlAdded+=(_,_)=>Fit();Fit();return section;
    }
}