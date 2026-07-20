using System.Collections;
using ReportViewerCore.Headless;
using ReportViewerCore.Engine;
using ReportViewerCore.Rendering;
using ReportViewerCore.Rendering.Html;
using ReportViewerCore.Rendering.OpenXml;
using ReportViewerCore.Rendering.Skia;

const float pageWidth = 595;
const float pageHeight = 842;
var outputDirectory = args.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

using var fonts = new SkiaFontResolver();
using var bitmapRenderer = new SkiaBitmapRenderer(fonts);
RenderImage preview = bitmapRenderer.Render(new RenderSize(pageWidth, pageHeight), DrawReport);
File.WriteAllBytes(Path.Combine(outputDirectory, "cross-platform-smoke.png"), preview.PngData.ToArray());

using var pdfRenderer = new SkiaPdfRenderer(fonts);
var pipeline = new HeadlessReportRenderer(new[] { pdfRenderer });
var report = new ReportDocument(new[]
{
	new ReportPage(new RenderSize(pageWidth, pageHeight), DrawReport)
});
ReportOutput pdf = pipeline.Render(report, new ReportRenderOptions(ReportOutputFormat.Pdf));
File.WriteAllBytes(Path.Combine(outputDirectory, "cross-platform-smoke.pdf"), pdf.Data.ToArray());

var htmlPipeline = new HeadlessReportRenderer(new IReportRenderer[] { new HtmlReportRenderer() });
ReportOutput html = htmlPipeline.Render(report, new ReportRenderOptions(ReportOutputFormat.Html));
File.WriteAllBytes(Path.Combine(outputDirectory, "cross-platform-smoke.html"), html.Data.ToArray());

var openXmlPipeline = new HeadlessReportRenderer(new IReportRenderer[]
{
	new ExcelOpenXmlRenderer(),
	new WordOpenXmlRenderer()
});
var openXmlReport = new ReportDocument(new[]
{
	new ReportPage(new RenderSize(pageWidth, pageHeight), DrawOpenXmlReport)
});
ReportOutput excel = openXmlPipeline.Render(openXmlReport, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
ReportOutput word = openXmlPipeline.Render(openXmlReport, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
File.WriteAllBytes(Path.Combine(outputDirectory, "cross-platform-smoke.xlsx"), excel.Data.ToArray());
File.WriteAllBytes(Path.Combine(outputDirectory, "cross-platform-smoke.docx"), word.Data.ToArray());

using var rdlcDefinition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Report.rdlc"));
var rdlcData = new RdlcDataContext(new Dictionary<string, IEnumerable>
{
	["Items"] = new[]
	{
		new { Description = "Alpha", Price = 10.5m, Qty = 2, Total = 21m },
		new { Description = "Beta", Price = 7.25m, Qty = 3, Total = 21.75m }
	}
});
using var localReport = new LocalReport(new RdlcReportEngine(), new HeadlessReportRenderer(new IReportRenderer[] { pdfRenderer, new HtmlReportRenderer() }));
localReport.LoadReportDefinition(rdlcDefinition);
localReport.SetDataSources(rdlcData.DataSets!);
localReport.SetParameters(rdlcData.Parameters ?? new Dictionary<string, object?>());
ReportOutput rdlcHtml = localReport.Render(ReportOutputFormat.Html);
File.WriteAllBytes(Path.Combine(outputDirectory, "rdlc-engine.html"), rdlcHtml.Data.ToArray());
ReportOutput rdlcPdf = localReport.Render(ReportOutputFormat.Pdf);
File.WriteAllBytes(Path.Combine(outputDirectory, "rdlc-engine.pdf"), rdlcPdf.Data.ToArray());

Console.WriteLine($"Wrote PNG, PDF, HTML, XLSX, DOCX, and RDLC-engine outputs to {outputDirectory}");

static void DrawReport(IRenderCanvas canvas)
{
	canvas.Clear(RenderColor.White);
	canvas.FillRectangle(new RenderRect(0, 0, 595, 92), new RenderColor(24, 47, 78));
	canvas.DrawText("ReportViewer Core", new RenderPoint(36, 42), new FontRequest("Arial", 26, Bold: true), RenderColor.White);
	canvas.DrawText("Cross-platform rendering backend", new RenderPoint(36, 70), new FontRequest("Arial", 14), new RenderColor(220, 230, 242));

	canvas.DrawText("macOS arm64 / Linux arm64 / Windows", new RenderPoint(36, 142), new FontRequest("Arial", 16, Bold: true), RenderColor.Black);
	canvas.DrawLine(new RenderPoint(36, 158), new RenderPoint(559, 158), new RenderColor(100, 120, 145), 1);

	canvas.FillRectangle(new RenderRect(36, 190, 523, 92), new RenderColor(239, 244, 249));
	canvas.DrawRectangle(new RenderRect(36, 190, 523, 92), new RenderColor(100, 120, 145), 1);
	canvas.DrawText("Engine output", new RenderPoint(54, 226), new FontRequest("Arial", 15, Bold: true), RenderColor.Black);
	canvas.DrawText("This page exercises the backend-neutral canvas, font metrics,", new RenderPoint(54, 251), new FontRequest("Arial", 12), new RenderColor(45, 55, 65));
	canvas.DrawText("HarfBuzz shaping boundary, and Skia PDF/image surfaces.", new RenderPoint(54, 270), new FontRequest("Arial", 12), new RenderColor(45, 55, 65));

	canvas.DrawText("Latin text and deterministic font metrics", new RenderPoint(36, 340), new FontRequest("Arial", 18), RenderColor.Black);
	canvas.DrawText("The legacy WinForms adapter remains Windows-only.", new RenderPoint(36, 390), new FontRequest("Arial", 13), new RenderColor(60, 70, 80));
	canvas.DrawHyperlink("Open report documentation", new RenderPoint(36, 430), new FontRequest("Arial", 13), new RenderColor(20, 90, 180), "https://example.com/report");
}

static void DrawOpenXmlReport(IRenderCanvas canvas)
{
	canvas.Clear(RenderColor.White);
	canvas.DrawText("ReportViewer Core", new RenderPoint(36, 42), new FontRequest("Arial", 26, Bold: true), RenderColor.Black);
	canvas.DrawText("Open XML text export", new RenderPoint(36, 82), new FontRequest("Arial", 16), RenderColor.Black);
	canvas.DrawText("This baseline preserves text and external hyperlinks.", new RenderPoint(36, 122), new FontRequest("Arial", 12), RenderColor.Black);
	canvas.DrawHyperlink("Open report documentation", new RenderPoint(36, 162), new FontRequest("Arial", 12), RenderColor.Black, "https://example.com/report");
}
