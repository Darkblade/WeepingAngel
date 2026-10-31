using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace DontBlink;

internal sealed class ConsoleSection : FlowLayoutPanel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette? Palette { get; set; }
    public ConsoleSection()
    {
        DoubleBuffered=true;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        FlowDirection=FlowDirection.TopDown;WrapContents=false;Padding=new Padding(20);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(Palette is not {} p)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var outline=RoundedGeometry.Path(new RectangleF(.5f,.5f,Width-1,Height-1),6);
        using var pen=new Pen(p.ColorOf(p.Token("inputs.border",p.Raised)));
        e.Graphics.DrawPath(pen,outline);
    }
}

// One stroke-based icon family; labels remain native text for keyboard and assistive access.
internal static class ConsoleGlyph
{
    public static void Draw(Graphics g,string name,Rectangle bounds,Color color)
    {
        var state=g.Save();g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/20f,bounds.Height/20f);
        using var pen=new Pen(color,1.3f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        switch(name)
        {
            case "Home":
                g.DrawLines(pen,[new(2,9),new(10,2),new(18,9),new(18,18),new(12,18),new(12,12),new(8,12),new(8,18),new(2,18),new(2,9)]);break;
            case "Clips":
                g.DrawRectangle(pen,2,6,16,12);g.DrawLine(pen,2,10,18,10);g.DrawLine(pen,6,3,8,6);g.DrawLine(pen,12,3,14,6);g.DrawLine(pen,2,6,17,2);break;
            case "USB storage":
                g.DrawRectangle(pen,2,6,16,12);g.DrawRectangle(pen,6,2,8,4);g.DrawLine(pen,5,14,15,14);break;
            case "Devices":g.DrawRectangle(pen,2,3,11,9);g.DrawLine(pen,7,12,7,17);g.DrawLine(pen,3,17,11,17);g.DrawEllipse(pen,13,11,5,5);break;
            case "Phone Access":g.DrawRectangle(pen,5,1,10,18);g.DrawLine(pen,8,16,12,16);break;
            case "Guard Post":g.DrawLines(pen,[new(10,1),new(18,4),new(17,12),new(10,19),new(3,12),new(2,4),new(10,1)]);g.DrawEllipse(pen,7,7,6,6);break;
            default:
                g.DrawLine(pen,2,5,18,5);g.DrawLine(pen,2,15,18,15);g.DrawEllipse(pen,5,2,6,6);g.DrawEllipse(pen,11,12,6,6);break;
        }
        g.Restore(state);
    }
}

internal sealed class ConsoleInput : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette? Palette { get; set; }
    public ConsoleInput(Control input)
    {
        DoubleBuffered=true;Padding=new Padding(10,8,10,8);Height=input.PreferredSize.Height+20;MinimumSize=new Size(0,Height);
        input.Dock=DockStyle.Top;if(input is TextBox text)text.BorderStyle=BorderStyle.None;
        Controls.Add(input);input.FontChanged+=(_,_)=>{Height=input.PreferredSize.Height+20;MinimumSize=new Size(0,Height);};
        input.Enter+=(_,_)=>Invalidate();input.Leave+=(_,_)=>Invalidate();
    }
    public override Size GetPreferredSize(Size proposedSize) => new(Width, Math.Max(MinimumSize.Height, Controls[0].PreferredSize.Height + Padding.Vertical + 4));
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(Palette is not {} p)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var shape=RoundedGeometry.Path(new RectangleF(.5f,.5f,Width-1,Height-1),5);
        using var pen=new Pen(p.ColorOf(ContainsFocus?p.Accent:p.Token("inputs.border",p.Raised)),ContainsFocus?2:1);
        e.Graphics.DrawPath(pen,shape);
    }
}
