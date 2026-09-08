using System.Runtime.InteropServices;
using System.Text;

namespace QuotaArc.Providers;

internal static class WindowsCredentialStore
{
    private const int CredTypeGeneric = 1;

    public static string? Read(params string[] targets)
    {
        foreach (var target in targets)
        {
            if (string.IsNullOrEmpty(target)) continue;
            if (CredRead(target, CredTypeGeneric, 0, out var ptr) && ptr != IntPtr.Zero)
            {
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                    if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize <= 0)
                        continue;
                    var bytes = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                    var text = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                    if (text.Length > 0 && text[0] == '\uFEFF')
                        text = text[1..];
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                    // Some blobs are UTF-16.
                    text = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
                finally
                {
                    CredFree(ptr);
                }
            }
        }
        return null;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
