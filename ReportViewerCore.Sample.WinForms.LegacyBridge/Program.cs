using Microsoft.Reporting.WinForms;

var outputDirectory = args.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

using var report = new LocalReport();
using var definition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Report.rdlc"));
report.LoadReportDefinition(definition);
report.DataSources.Add(new ReportDataSource("Items", new[]
{
	new { Name = "WinForms LocalReport", Amount = 10 },
	new { Name = "Portable v2 renderer", Amount = 20 }
}));

byte[] html = report.RenderPortable("HTML", null, out string mimeType, out _, out string extension);
File.WriteAllBytes(Path.Combine(outputDirectory, $"winforms-legacy-bridge.{extension}"), html);
Console.WriteLine($"WinForms LocalReport rendered through v2: {mimeType}");
