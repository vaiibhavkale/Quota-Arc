using System.Security.Cryptography;
using System.Text.Json;

namespace QuotaArc.Providers;

/// <summary>
/// Electron / Chromium OSCrypt on Windows: Local State holds a DPAPI-wrapped
/// AES key, and values are "v10" + nonce + ciphertext + tag.
/// Claude Desktop stores its OAuth cache this way.
/// </summary>
internal static class ChromiumCrypt
{
    public static byte[]? LoadOsCryptKey(string localStatePath)
    {
        if (!File.Exists(localStatePath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (!doc.RootElement.TryGetProperty("os_crypt", out var crypt) ||
                !crypt.TryGetProperty("encrypted_key", out var keyEl))
                return null;
            var raw = keyEl.GetString();
            if (string.IsNullOrEmpty(raw)) return null;
            var blob = Convert.FromBase64String(raw);
            const string prefix = "DPAPI";
            if (blob.Length <= prefix.Length) return null;
            var header = System.Text.Encoding.ASCII.GetString(blob, 0, prefix.Length);
            if (header != prefix) return null;
            var wrapped = blob[prefix.Length..];
            return ProtectedData.Unprotect(wrapped, null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            return null;
        }
    }

    public static string? Decrypt(byte[] key, string base64)
    {
        if (string.IsNullOrEmpty(base64) || key.Length == 0) return null;
        try
        {
            var data = Convert.FromBase64String(base64);
            return DecryptBytes(key, data);
        }
        catch
        {
            return null;
        }
    }

    public static string? DecryptBytes(byte[] key, byte[] data)
    {
        if (data.Length < 3 + 12 + 16) return null;
        var prefix = System.Text.Encoding.ASCII.GetString(data, 0, 3);
        if (prefix is not ("v10" or "v11")) return null;
        var nonce = data.AsSpan(3, 12);
        var tag = data.AsSpan(data.Length - 16, 16);
        var cipher = data.AsSpan(15, data.Length - 15 - 16);
        var plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }
}
