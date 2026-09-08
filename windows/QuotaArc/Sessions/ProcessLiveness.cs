using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuotaArc.Sessions;

internal static class ProcessLiveness
{
    private static readonly TimeSpan ReuseTolerance = TimeSpan.FromMinutes(5);

    public static bool IsAlive(int pid, DateTime? startedAt)
    {
        if (!Exists(pid, out var actualStart)) return false;
        if (startedAt is null || actualStart is null) return true;
        return (actualStart.Value - startedAt.Value).Duration() < ReuseTolerance;
    }

    public static bool NamedProcessRunning(params string[] names)
    {
        try
        {
            foreach (var name in names)
            {
                if (Process.GetProcessesByName(StripExe(name)).Length > 0)
                    return true;
            }
        }
        catch { /* access denied on some processes */ }
        return false;
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    private static bool Exists(int pid, out DateTime? start)
    {
        start = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;
            try { start = p.StartTime; } catch { /* denied */ }
            return true;
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();
}
