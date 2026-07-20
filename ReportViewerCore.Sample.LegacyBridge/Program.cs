using Microsoft.Reporting.NETCore;
using System.Text;

var outputDirectory = args.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

using var report = new LocalReport();
using var definition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Report.rdlc"));
report.LoadReportDefinition(definition);
report.DataSources.Add(new ReportDataSource("Items", new[]
{
	new { Name = "Windows bridge", Amount = 10 },
	new { Name = "Shared Skia backend", Amount = 20 }
}));

byte[] html = report.RenderPortable("HTML", null, out string mimeType, out _, out string extension);
File.WriteAllBytes(Path.Combine(outputDirectory, $"legacy-bridge.{extension}"), html);
string markup = Encoding.UTF8.GetString(html);
int portablePageCount = CountOccurrences(markup, "class=\"report-page\"");
int legacyPageCount = report.GetTotalPages(out _);
if (portablePageCount != legacyPageCount || !markup.Contains("Windows bridge", StringComparison.Ordinal) || !markup.Contains("Shared Skia backend", StringComparison.Ordinal))
{
	throw new InvalidOperationException($"Legacy/v2 comparison failed: portable pages={portablePageCount}, legacy pages={legacyPageCount}, expected rows present={markup.Contains("Windows bridge", StringComparison.Ordinal) && markup.Contains("Shared Skia backend", StringComparison.Ordinal)}.");
}

Console.WriteLine($"Legacy LocalReport v2 comparison passed: {portablePageCount} page(s), {mimeType}");

static int CountOccurrences(string value, string token)
{
	int count = 0;
	int offset = 0;
	while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
	{
		count++;
		offset += token.Length;
	}

	return count;
}
