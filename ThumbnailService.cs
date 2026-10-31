using System.Runtime.InteropServices;
using System.Drawing.Imaging;

namespace DontBlink.Services;

// Uses Windows' installed video thumbnail provider; no separate FFmpeg install.
public sealed class ThumbnailService(ClipService clips)
{
    private readonly SemaphoreSlim _gate = new(1);
    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        string? video = clips.ArchivePath(key);
        if (video == null) return null;
        string directory = Path.Combine(Path.GetDirectoryName(video)!, ".thumbnails");
        string cache = Path.Combine(directory, key + ".jpg");
        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(cache)) return await File.ReadAllBytesAsync(cache, ct);
            ct.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                object? shell = null; IntPtr bitmap = IntPtr.Zero;
                try
                {
                    Guid iid = typeof(IImageFactory).GUID;
                    Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(video, IntPtr.Zero, ref iid, out shell));
                    ((IImageFactory)shell).GetImage(new PixelSize { Width = 256, Height = 144 }, 8, out bitmap);
                    using var image = Image.FromHbitmap(bitmap);
                    using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Jpeg);
                    completion.TrySetResult(stream.ToArray());
                }
                catch (Exception) { completion.TrySetResult(null); }
                finally
                {
                    if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                    if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
                }
            }) { IsBackground = true, Name = "DontBlink thumbnail" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            var result = await completion.Task;
            ct.ThrowIfCancellationRequested();
            if (result != null)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    await File.WriteAllBytesAsync(cache + ".partial", result, ct);
                    File.Move(cache + ".partial", cache, true);
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return result;
        }
        finally { _gate.Release(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct PixelSize { public int Width; public int Height; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory { void GetImage(PixelSize size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
