using Microsoft.Reporting.WinForms;
using System.Text;
using System.Text.Json;

var outputDirectory = args.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

using var report = new LocalReport();
using var definition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Parent.rdlc"));
using var childDefinition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Child.rdlc"));
using var goldenDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenBridgeExpectations.json")));
JsonElement golden = goldenDocument.RootElement.GetProperty("winFormsLegacyBridge");
int expectedPageCount = golden.GetProperty("expectedPageCount").GetInt32();
string[] requiredText = golden.GetProperty("requiredText").EnumerateArray().Select(value => value.GetString()!).ToArray();
report.LoadReportDefinition(definition);
report.LoadSubreportDefinition("Child", childDefinition);
report.SetParameters(new[] { new ReportParameter("ParentTitle", "RPL fallback proof") });

byte[] html = report.RenderPortable("HTML", null, out string mimeType, out _, out string extension);
File.WriteAllBytes(Path.Combine(outputDirectory, $"winforms-legacy-bridge.{extension}"), html);
string markup = Encoding.UTF8.GetString(html);
int portablePageCount = CountOccurrences(markup, "class=\"report-page\"");
int legacyPageCount = report.GetTotalPages(out _);
bool requiredTextPresent = requiredText.All(text => markup.Contains(text, StringComparison.Ordinal));
if (portablePageCount != legacyPageCount || portablePageCount != expectedPageCount || !requiredTextPresent)
{
	throw new InvalidOperationException($"Legacy/v2 comparison failed: portable pages={portablePageCount}, legacy pages={legacyPageCount}, golden pages={expectedPageCount}, required text present={requiredTextPresent}.");
}

Console.WriteLine($"WinForms RPL fallback comparison passed: {portablePageCount} page(s), {mimeType}");

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
