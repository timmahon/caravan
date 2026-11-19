using System.Globalization;

namespace Caravan.Samples.FileBatch;

/// <summary>The summary of one CSV file of <c>customer,amount</c> lines.</summary>
/// <param name="File">The file name.</param>
/// <param name="Lines">The number of data lines.</param>
/// <param name="Total">The sum of the amounts.</param>
public sealed record FileSummary(string File, int Lines, decimal Total)
{
    /// <summary>Reads a CSV text (an optional <c>customer,amount</c> header line is skipped).</summary>
    /// <param name="file">The file name.</param>
    /// <param name="csv">The text.</param>
    /// <returns>The summary.</returns>
    public static FileSummary FromCsv(string file, string csv)
    {
        var lines = 0;
        var total = 0m;
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',');
            if (parts.Length == 2 && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                lines++;
                total += amount;
            }
        }

        return new FileSummary(file, lines, total);
    }
}
