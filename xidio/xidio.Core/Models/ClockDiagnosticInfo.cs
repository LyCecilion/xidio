namespace xidio.Core.Models;

public sealed class ClockDiagnosticInfo
{
    public required DateTimeOffset LocalTime { get; init; }

    public required string NtpServer { get; init; }

    public DateTimeOffset? NtpTime { get; init; }

    public TimeSpan? Offset { get; init; }

    public string? Error { get; init; }
}
