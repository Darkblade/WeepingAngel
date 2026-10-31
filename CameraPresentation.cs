using System.Drawing.Drawing2D;
using DontBlink.Services;

namespace DontBlink;

internal static class RoundedGeometry
{
    public static GraphicsPath Path(RectangleF bounds,float radius)
    {
        var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
        if(d<=0)return path;path.AddArc(bounds.Left,bounds.Top,d,d,180,90);path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public static void Clip(Control control,int radius){if(control.Width<2||control.Height<2)return;using var path=Path(new RectangleF(0,0,control.Width,control.Height),radius);var old=control.Region;control.Region=new Region(path);old?.Dispose();}
}

internal sealed class RoundedPanel:Panel
{
    public RoundedPanel(){DoubleBuffered=true;}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);RoundedGeometry.Clip(this,14);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var shape=RoundedGeometry.Path(new RectangleF(.5f,.5f,Width-1,Height-1),14);using var pen=new Pen(ThemePalette.Resolve(AppSettings.Load().Theme).ColorOf(ThemePalette.Resolve(AppSettings.Load().Theme).Raised));e.Graphics.DrawPath(pen,shape);}
}

internal static class CameraBattery
{
    public static (int? Percent,bool Estimated) Level(CameraOverview camera)
    {
        if(int.TryParse(camera.Battery.Trim().TrimEnd('%'),out int n)&&n>=0&&n<=100)return(n,false);
        return camera.Battery.ToLowerInvariant() switch{"ok" or "good"=>(100,true),"low" or "bad"=>(10,true),_=>(null,false)};
    }
    public static string Label(CameraOverview camera){var level=Level(camera);return "Battery "+camera.Battery+(level.Percent is int n?" · "+(level.Estimated?"≈":"")+n+"%":"");}
    public static string Description(CameraOverview camera)=>Label(camera)+(Level(camera).Estimated?" (estimated from Blink's battery status; not a measured percentage)":"");
}

internal sealed class CameraPreview:PictureBox
{
    private CameraOverview? camera;
    internal CameraOverview? Camera=>camera;
    internal int StatusHeight=>Math.Max(32,(int)(36*DeviceDpi/96f));
    internal static Color StatusBackground=>Color.FromArgb(12,15,18);
    internal void UpdateCamera(CameraOverview value){if(camera!=value){camera=value;Invalidate();}}
    internal ThemePalette? Palette;
    internal bool LiveAvailable=true;
    internal event EventHandler? ReorderDragRequested;
    private Point dragOrigin;private bool dragStarted;
    protected override void OnMouseDown(MouseEventArgs e){dragOrigin=e.Location;dragStarted=false;base.OnMouseDown(e);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(e.Button!=MouseButtons.Left||dragStarted)return;var size=SystemInformation.DragSize;var zone=new Rectangle(dragOrigin.X-size.Width/2,dragOrigin.Y-size.Height/2,size.Width,size.Height);if(!zone.Contains(e.Location)){dragStarted=true;ReorderDragRequested?.Invoke(this,EventArgs.Empty);}}
    protected override void OnClick(EventArgs e){if(!dragStarted)base.OnClick(e);}
    public CameraPreview(){SetStyle(ControlStyles.Selectable,true);TabStop=true;AccessibleRole=AccessibleRole.PushButton;}
    public CameraPreview(CameraOverview value):this(){camera=value;}
    public static string MotionLabel(CameraOverview camera)=>"Motion recording: "+(camera.Motion=="Enabled"?"On":camera.Motion=="Disabled"?"Off":"Unknown");
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);RoundedGeometry.Clip(this,10);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode is Keys.Enter or Keys.Space){e.Handled=true;e.SuppressKeyPress=true;dragStarted=false;OnClick(EventArgs.Empty);}base.OnKeyDown(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int footer=StatusHeight;int x=Width/2,y=(Height-footer)/2;
        using var playBackground=new SolidBrush(Color.FromArgb(185,15,20,25));using var playBorder=new Pen(Color.White,Focused?3:1);e.Graphics.FillEllipse(playBackground,x-23,y-23,46,46);e.Graphics.DrawEllipse(playBorder,x-23,y-23,46,46);e.Graphics.FillPolygon(Brushes.White,[new Point(x-6,y-10),new Point(x+11,y),new Point(x-6,y+10)]);
        if(!LiveAvailable&&Image!=null){using var grey=new System.Drawing.Imaging.ImageAttributes();grey.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]{new float[]{.3f,.3f,.3f,0,0},new float[]{.59f,.59f,.59f,0,0},new float[]{.11f,.11f,.11f,0,0},new float[]{0,0,0,1,0},new float[]{0,0,0,0,1}}));float ratio=Math.Min((float)Width/Image.Width,(float)Height/Image.Height);int w=(int)(Image.Width*ratio),h=(int)(Image.Height*ratio);e.Graphics.DrawImage(Image,new Rectangle((Width-w)/2,(Height-h)/2,w,h),0,0,Image.Width,Image.Height,GraphicsUnit.Pixel,grey);}
        if(camera==null)return;var palette=Palette??ThemePalette.Resolve(AppSettings.Load().Theme);
        using var strip=new SolidBrush(StatusBackground);e.Graphics.FillRectangle(strip,0,Height-footer,Width,footer);
        using var titleBg=new SolidBrush(Color.FromArgb(210,15,20,25));using var titleFont=new Font(Font,FontStyle.Bold);var titleSize=TextRenderer.MeasureText(camera.Name,titleFont);e.Graphics.FillRectangle(titleBg,7,7,Math.Min(Width-14,titleSize.Width+14),titleSize.Height+8);TextRenderer.DrawText(e.Graphics,camera.Name,titleFont,new Rectangle(14,11,Width-28,titleSize.Height),Color.White,TextFormatFlags.EndEllipsis);
        string age=(camera.ThumbnailTimeEstimated?"~ ":"")+DeviceFeatures.Age(camera.ThumbnailCapturedUtc,DateTimeOffset.UtcNow);var ageSize=TextRenderer.MeasureText(age,Font);int ageY=Height-footer-ageSize.Height-8;using(var ageBackground=new SolidBrush(Color.FromArgb(210,12,15,18)))e.Graphics.FillRectangle(ageBackground,8,ageY,ageSize.Width+10,ageSize.Height+4);TextRenderer.DrawText(e.Graphics,age,Font,new Point(13,ageY+2),Color.White);
        var level=CameraBattery.Level(camera);int top=Height-footer+(footer-18)/2;
        string batteryState=level.Percent is int percent?(percent<=20?"error":"success"):"unknown";
        using var batteryPen=new Pen(palette.ColorOf(palette.Token("status."+batteryState+".foreground",palette.Muted)),1.6f);
        int batteryX=Width-41;e.Graphics.DrawRectangle(batteryPen,batteryX,top+3,23,12);e.Graphics.DrawLine(batteryPen,batteryX+26,top+6,batteryX+26,top+12);
        if(level.Percent is int charge){using var fill=new SolidBrush(batteryPen.Color);e.Graphics.FillRectangle(fill,batteryX+3,top+6,Math.Max(1,17*charge/100f),6);}


    }
    internal static void DrawMotion(Graphics graphics,int x,int y,bool enabled,ThemePalette palette)
    {
        using var pen=new Pen(palette.ColorOf(enabled?palette.Token("status.success.foreground","#84FF80"):palette.Muted),1.8f);using var body=RoundedGeometry.Path(new RectangleF(x,y+2,20,13),3);graphics.DrawPath(pen,body);
        graphics.DrawPolygon(pen,[new Point(x+20,y+6),new Point(x+26,y+3),new Point(x+26,y+14),new Point(x+20,y+11)]);
        graphics.DrawArc(pen,x+26,y+3,8,12,-65,130);graphics.DrawArc(pen,x+28,y,12,18,-65,130);
        if(!enabled)graphics.DrawLine(pen,x+29,y+18,x+43,y-1);
    }
}

internal sealed class ThemedCheckBox:CheckBox
{
    internal ThemePalette? Palette;
    public ThemedCheckBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    public override Size GetPreferredSize(Size proposedSize){var size=base.GetPreferredSize(proposedSize);return new Size(size.Width+10,Math.Max(26,size.Height));}
    protected override void OnPaint(PaintEventArgs e){var p=Palette??ThemePalette.Resolve(AppSettings.Load().Theme);e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int y=(Height-18)/2;using var bg=new SolidBrush(p.ColorOf(p.Surface));using var pen=new Pen(p.ColorOf(Enabled?(Checked?p.Accent:p.Token("inputs.border",p.Raised)):p.Muted),1.5f);using var box=RoundedGeometry.Path(new RectangleF(1,y,18,18),3);e.Graphics.FillPath(bg,box);e.Graphics.DrawPath(pen,box);if(Checked){pen.Width=2;e.Graphics.DrawLines(pen,[new Point(5,y+9),new Point(9,y+13),new Point(16,y+5)]);}TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(27,0,Width-27,Height),p.ColorOf(Enabled?p.Text:p.Muted),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(25,2,Width-28,Height-4),ForeColor,BackColor);}
}
