using System.ComponentModel;
namespace DontBlink;
internal sealed class SettingsTabLink:LinkLabel
{
    private bool selected,hovered;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string? IconName{get;set;}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public ThemePalette? Palette{get;set;}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected{get=>selected;set{selected=value;RefreshColors();}}
    public SettingsTabLink(){AutoSize=true;Padding=new Padding(10,10,10,12);Margin=new Padding(0,0,8,0);LinkBehavior=LinkBehavior.NeverUnderline;Cursor=Cursors.Hand;}
    public void RefreshColors(){if(Palette is not {} p)return;LinkColor=p.ColorOf(selected||hovered?p.Accent:p.Text);ActiveLinkColor=p.ColorOf(p.Accent);VisitedLinkColor=LinkColor;Invalidate();}
    protected override void OnMouseEnter(EventArgs e){hovered=true;RefreshColors();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=false;RefreshColors();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(IconName!=null&&Palette is {} iconPalette)ConsoleGlyph.Draw(e.Graphics,IconName,new Rectangle(10,14,19,19),iconPalette.ColorOf(selected||hovered?iconPalette.Accent:iconPalette.Text));if(selected&&Palette is {} p){using var pen=new Pen(p.ColorOf(p.Accent),2);e.Graphics.DrawLine(pen,Padding.Left,Height-2,Width-Padding.Right,Height-2);}}
}