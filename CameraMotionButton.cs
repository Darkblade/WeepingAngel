using System.ComponentModel;
using DontBlink.Services;

namespace DontBlink;

internal sealed class CameraMotionButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CameraOverview? Camera { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette? Palette { get; set; }
    public CameraMotionButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(CameraPreview.StatusBackground);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var palette=Palette??ThemePalette.Resolve(AppSettings.Load().Theme);
        CameraPreview.DrawMotion(e.Graphics,(Width-44)/2,(Height-20)/2,Camera?.Motion=="Enabled",Enabled?palette:palette with{Muted=palette.Muted});
        if(!Enabled){using var dim=new SolidBrush(Color.FromArgb(100,CameraPreview.StatusBackground));e.Graphics.FillRectangle(dim,ClientRectangle);}
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-2,-2),palette.ColorOf(palette.Accent),CameraPreview.StatusBackground);
    }
}
