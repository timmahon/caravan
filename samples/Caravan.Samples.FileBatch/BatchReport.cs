using System.Globalization;
using System.Text;

namespace Caravan.Samples.FileBatch;

/// <summary>Renders the report of one poll: a line per file and the grand total.</summary>
public static class BatchReport
{
    /// <summary>Renders the report.</summary>
    /// <param name="summaries">The file summaries of the poll.</param>
    /// <returns>The report text.</returns>
    public static string Render(IEnumerable<FileSummary> summaries)
    {
        var report = new StringBuilder();
        var files = summaries.OrderBy(s => s.File, StringComparer.Ordinal).ToList();
        foreach (var file in files)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{file.File}: {file.Lines} lines, total {file.Total:0.00}");
        }

        report.Append(CultureInfo.InvariantCulture, $"TOTAL: {files.Count} files, {files.Sum(f => f.Lines)} lines, {files.Sum(f => f.Total):0.00}");
        return report.ToString();
    }
}
