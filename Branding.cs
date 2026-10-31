using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DontBlink;

internal static class Branding
{
    private static readonly PrivateFontCollection Fonts = new();
    private static readonly IntPtr FontMemory;
    static Branding()
    {
        byte[] bytes = Read("Assets.Fonts.CinzelDecorative-Bold.ttf");
        FontMemory = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, FontMemory, bytes.Length);
        Fonts.AddMemoryFont(FontMemory, bytes.Length);
        uint count = 0;
        AddFontMemResourceEx(FontMemory, (uint)bytes.Length, IntPtr.Zero, ref count);
    }
    public static Font TitleFont(float size) => new(Fonts.Families[0], size, FontStyle.Bold);
    public static Icon WindowIcon() { using var stream = new MemoryStream(Read("Assets.Branding.gui.ico")); using var icon = new Icon(stream); return (Icon)icon.Clone(); }
    public static byte[] Read(string suffix)
    {
        var assembly = typeof(Branding).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr file, uint size, IntPtr reserved, ref uint count);
}
