using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace DontBlink.Services;

// DPAPI uses the current Windows user. Neither the password nor the SMS PIN is logged.
public sealed class CredentialStore(string? filePath = null)
{
    private readonly string _path = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DontBlink", "credentials.dat");
    public record Credentials(string Email, string Password);
    public Credentials? Load()
    {
        if (!File.Exists(_path)) return null;
        byte[] plain = Transform(File.ReadAllBytes(_path), false);
        try { return JsonSerializer.Deserialize<Credentials>(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Save(string email, string password)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new Credentials(email, password));
        try
        {
            byte[] encrypted = Transform(plain, true);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            try { File.WriteAllBytes(_path + ".partial", encrypted); File.Move(_path + ".partial", _path, true); }
            finally { if (File.Exists(_path + ".partial")) File.Delete(_path + ".partial"); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Delete() { if (File.Exists(_path)) File.Delete(_path); }

    internal static byte[] Transform(byte[] data, bool encrypt)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool ok = encrypt
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not access the saved credentials for this user.");
            byte[] result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[data.Length], 0, input.Data, data.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
