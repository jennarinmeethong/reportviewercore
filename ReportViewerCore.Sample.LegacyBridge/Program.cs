using Microsoft.Reporting.NETCore;

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
Console.WriteLine($"Legacy LocalReport rendered through v2: {mimeType}");
