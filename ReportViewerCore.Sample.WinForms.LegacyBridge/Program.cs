using Microsoft.Reporting.WinForms;
using System.Text;

var outputDirectory = args.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

using var report = new LocalReport();
using var definition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Parent.rdlc"));
using var childDefinition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Child.rdlc"));
report.LoadReportDefinition(definition);
report.LoadSubreportDefinition("Child", childDefinition);
report.SetParameters(new[] { new ReportParameter("ParentTitle", "RPL fallback proof") });

byte[] html = report.RenderPortable("HTML", null, out string mimeType, out _, out string extension);
File.WriteAllBytes(Path.Combine(outputDirectory, $"winforms-legacy-bridge.{extension}"), html);
string markup = Encoding.UTF8.GetString(html);
int portablePageCount = CountOccurrences(markup, "class=\"report-page\"");
int legacyPageCount = report.GetTotalPages(out _);
if (portablePageCount != legacyPageCount || !markup.Contains("RPL fallback proof", StringComparison.Ordinal))
{
	throw new InvalidOperationException($"Legacy/v2 comparison failed: portable pages={portablePageCount}, legacy pages={legacyPageCount}, child text present={markup.Contains("RPL fallback proof", StringComparison.Ordinal)}.");
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
