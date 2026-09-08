using Microsoft.Win32;

namespace QuotaArc.Model;

/// <summary>
/// Tiny persistence in HKCU, standing in for macOS UserDefaults.
/// </summary>
internal static class AppSettings
{
    private const string Path = AppBranding.RegistryKey;

    /// <summary>
    /// Copies settings from the previous Windows install name, once.
    /// </summary>
    public static void MigrateFromPreviousInstall()
    {
        using var dest = Registry.CurrentUser.OpenSubKey(Path);
        if (dest is not null && dest.ValueCount > 0) return;

        using var src = Registry.CurrentUser.OpenSubKey(@"Software\Codenotch");
        if (src is null || src.ValueCount == 0) return;

        using var write = Registry.CurrentUser.CreateSubKey(Path);
        foreach (var name in src.GetValueNames())
        {
            var value = src.GetValue(name);
            if (value is null) continue;
            write.SetValue(name, value, src.GetValueKind(name));
        }
    }

    public static string? Get(string key)
    {
        using var hive = Registry.CurrentUser.OpenSubKey(Path);
        return hive?.GetValue(key) as string;
    }

    public static string[] GetList(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrEmpty(raw)) return [];
        return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool GetBool(string key, bool fallback = false)
    {
        var raw = Get(key);
        if (raw is null) return fallback;
        return raw is "1" or "true" or "True";
    }

    public static bool GetBoolOrDefault(string key, bool whenMissing)
    {
        var raw = Get(key);
        if (raw is null) return whenMissing;
        return raw is "1" or "true" or "True";
    }

    public static double GetDouble(string key, double fallback = 0)
    {
        var raw = Get(key);
        return double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public static void SetDouble(string key, double value) =>
        Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static void Set(string key, string value)
    {
        using var hive = Registry.CurrentUser.CreateSubKey(Path);
        hive.SetValue(key, value);
    }

    public static void SetList(string key, IEnumerable<string> values) =>
        Set(key, string.Join('\n', values));

    public static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

    public static void Remove(string key)
    {
        using var hive = Registry.CurrentUser.OpenSubKey(Path, true);
        hive?.DeleteValue(key, false);
    }

    public static void EraseAll()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Path, false);
    }
}
