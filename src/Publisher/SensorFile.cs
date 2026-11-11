using System.Globalization;

namespace MachineTelemetry.Edge;

public static class SensorFile
{
    public static async Task<double> ReadAsync(string path, CancellationToken cancellation)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellation);
        if (lines.Length < 2 || !lines[0].TrimEnd().EndsWith(" YES", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The sensor did not return a valid CRC.");
        }

        var start = lines[1].LastIndexOf(" t=", StringComparison.Ordinal);
        if (start < 0 || !int.TryParse(lines[1].AsSpan(start + 3), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var millidegrees) ||
            millidegrees is < -55000 or > 125000)
        {
            throw new InvalidDataException("The sensor did not return a valid temperature.");
        }
        return millidegrees / 1000.0;
    }
}
