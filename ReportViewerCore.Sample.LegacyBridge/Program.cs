using Microsoft.Reporting.NETCore;
using System.Text;
using System.Text.Json;

var outputDirectory = args.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

using var report = new LocalReport();
using var definition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Report.rdlc"));
using var goldenDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenBridgeExpectations.json")));
JsonElement golden = goldenDocument.RootElement.GetProperty("legacyBridge");
int expectedPageCount = golden.GetProperty("expectedPageCount").GetInt32();
string[] requiredText = golden.GetProperty("requiredText").EnumerateArray().Select(value => value.GetString()!).ToArray();
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
bool legacyComparisonAvailable = OperatingSystem.IsWindows();
int legacyPageCount = legacyComparisonAvailable ? report.GetTotalPages(out _) : portablePageCount;
bool requiredTextPresent = requiredText.All(text => markup.Contains(text, StringComparison.Ordinal));
if (portablePageCount != expectedPageCount || (legacyComparisonAvailable && portablePageCount != legacyPageCount) || !requiredTextPresent)
{
	throw new InvalidOperationException($"Legacy/v2 comparison failed: portable pages={portablePageCount}, legacy pages={legacyPageCount}, golden pages={expectedPageCount}, required text present={requiredTextPresent}.");
}

string legacyStatus = legacyComparisonAvailable ? $"legacy pages={legacyPageCount}" : "legacy page count skipped on non-Windows";
Console.WriteLine($"Legacy LocalReport v2 comparison passed: {portablePageCount} page(s), {mimeType}; {legacyStatus}");

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
