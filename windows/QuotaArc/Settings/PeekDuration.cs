namespace QuotaArc.Settings;

internal enum PeekDuration
{
    Brief,
    Standard,
    Long
}

internal static class PeekDurationInfo
{
    public static PeekDuration[] All { get; } = Enum.GetValues<PeekDuration>();

    public static string Raw(this PeekDuration value) => value.ToString().ToLowerInvariant();

    public static PeekDuration Parse(string? raw) => raw switch
    {
        "brief" => PeekDuration.Brief,
        "long" => PeekDuration.Long,
        _ => PeekDuration.Standard
    };

    public static double Seconds(this PeekDuration value) => value switch
    {
        PeekDuration.Brief => 3,
        PeekDuration.Long => 10,
        _ => 5
    };

    public static string Title(this PeekDuration value) => value switch
    {
        PeekDuration.Brief => "3 seconds",
        PeekDuration.Long => "10 seconds",
        _ => "5 seconds"
    };

    public static string Explanation(this PeekDuration value) => value switch
    {
        PeekDuration.Brief => "Long enough to notice, short enough to ignore.",
        PeekDuration.Long => "Stays until you have had a chance to look up.",
        _ => "Long enough to read the session's name and reach for it."
    };
}
