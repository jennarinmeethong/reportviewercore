using System.Text.Json;
using System.Collections;
using ReportViewerCore.Headless;
using ReportViewerCore.Rendering;
using ReportViewerCore.Rendering.Html;
using ReportViewerCore.Rendering.OpenXml;
using ReportViewerCore.Rendering.Skia;

const float firstPageWidth = 760;
const float firstPageHeight = 980;
const float secondPageWidth = 840;
const float secondPageHeight = 760;

string outputDirectory = args.Length == 0
	? Path.GetFullPath(Path.Combine("artifacts", "feature-showcase"))
	: Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

using var fonts = new SkiaFontResolver();
using var bitmapRenderer = new SkiaBitmapRenderer(fonts);
RenderImage sampleImage = bitmapRenderer.Render(new RenderSize(64, 64), canvas =>
{
	canvas.Clear(new RenderColor(224, 236, 248));
	canvas.FillRectangle(new RenderRect(8, 8, 48, 48), new RenderColor(35, 111, 180));
	canvas.DrawRectangle(new RenderRect(8, 8, 48, 48), new RenderColor(15, 45, 80), 2);
	canvas.DrawLine(new RenderPoint(8, 56), new RenderPoint(56, 8), RenderColor.White, 3);
});

var report = new ReportDocument(new[]
{
	new ReportPage(new RenderSize(firstPageWidth, firstPageHeight), canvas => DrawOverview(canvas, sampleImage)),
	new ReportPage(new RenderSize(secondPageWidth, secondPageHeight), canvas => DrawChartsAndClipping(canvas, sampleImage))
});

for (int pageIndex = 0; pageIndex < report.Pages.Count; pageIndex++)
{
	ReportPage page = report.Pages[pageIndex];
	RenderImage pageImage = bitmapRenderer.Render(page.Size, page.Render);
	File.WriteAllBytes(Path.Combine(outputDirectory, $"feature-showcase-page-{pageIndex + 1}.png"), pageImage.PngData.ToArray());
}

var renderers = new HeadlessReportRenderer(new IReportRenderer[]
{
	new SkiaPdfRenderer(fonts),
	new HtmlReportRenderer(),
	new ExcelOpenXmlRenderer(),
	new WordOpenXmlRenderer()
});
foreach (ReportOutputFormat format in Enum.GetValues<ReportOutputFormat>())
{
	ReportOutput output = renderers.Render(report, new ReportRenderOptions(format));
	File.WriteAllBytes(Path.Combine(outputDirectory, $"feature-showcase.{output.FileExtension}"), output.Data.ToArray());
}

string[] generatedFiles =
{
	"feature-showcase-page-1.png",
	"feature-showcase-page-2.png",
	"feature-showcase.pdf",
	"feature-showcase.html",
	"feature-showcase.xlsx",
	"feature-showcase.docx",
	"feature-showcase-manifest.json"
};
var manifest = new
{
	Name = "ReportViewerCore v2 feature showcase",
	Pages = new[]
	{
		new { Number = 1, Width = firstPageWidth, Height = firstPageHeight, Focus = "text, styles, directions, links, image, table-cell spans, rectangles, lines" },
		new { Number = 2, Width = secondPageWidth, Height = secondPageHeight, Focus = "bar, column, line, area, pie, doughnut charts and page clipping" }
	},
	OutputFormats = new[] { "png", "pdf", "html", "xlsx", "docx" },
	Features = new[]
	{
		"Clear and filled/stroked rectangles",
		"Lines with visible and page-clipped geometry",
		"Text family, size, bold, italic, color, whitespace, and multiline content",
		"Left-to-right, right-to-left, top-to-bottom, and bottom-to-top text",
		"Relative and HTTP hyperlink validation path",
		"Embedded PNG image with page-clipped image crop metadata",
		"Table cells with ColSpan and RowSpan metadata",
		"Bar compatibility shim plus column, line, area, pie, and doughnut charts",
		"Multi-page document with different page sizes and native page boundaries"
	},
	Files = generatedFiles
};
File.WriteAllText(Path.Combine(outputDirectory, "feature-showcase-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

WriteRdlcShowcase(outputDirectory, bitmapRenderer, renderers);

Console.WriteLine($"Wrote feature showcase outputs ({generatedFiles.Length} files) to {outputDirectory}");

static void WriteRdlcShowcase(string outputDirectory, SkiaBitmapRenderer bitmapRenderer, HeadlessReportRenderer renderers)
{
	string sourcePath = Path.Combine(AppContext.BaseDirectory, "FeatureShowcase.rdlc");
	string showcaseDirectory = Path.Combine(outputDirectory, "rdlc-feature-showcase");
	Directory.CreateDirectory(showcaseDirectory);
	File.Copy(sourcePath, Path.Combine(showcaseDirectory, "rdlc-feature-showcase.rdlc"), overwrite: true);

	using var definition = File.OpenRead(sourcePath);
	using var localReport = new LocalReport(new ReportViewerCore.Engine.RdlcReportEngine(), renderers, new SkiaImageResolver());
	localReport.LoadReportDefinition(definition);
	localReport.SetDataSources(new Dictionary<string, IEnumerable>
	{
		["Items"] = new[]
		{
			new ShowcaseRow("North", "Alpha", 12),
			new ShowcaseRow("North", "Beta", 8),
			new ShowcaseRow("South", "Gamma", 16),
			new ShowcaseRow("South", "Delta", 5)
		}
	});
	localReport.SetParameters(new Dictionary<string, object?>
	{
		["TargetUrl"] = "https://example.com/rdlc-feature-showcase",
		["HideDetails"] = false
	});

	ReportDocument document = localReport.CreateDocument();
	for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
	{
		ReportPage page = document.Pages[pageIndex];
		RenderImage pageImage = bitmapRenderer.Render(page.Size, page.Render);
		File.WriteAllBytes(Path.Combine(showcaseDirectory, $"rdlc-feature-showcase-page-{pageIndex + 1}.png"), pageImage.PngData.ToArray());
	}

	foreach (ReportOutputFormat format in Enum.GetValues<ReportOutputFormat>())
	{
		ReportOutput output = renderers.Render(document, new ReportRenderOptions(format));
		File.WriteAllBytes(Path.Combine(showcaseDirectory, $"rdlc-feature-showcase.{output.FileExtension}"), output.Data.ToArray());
	}

	string[] files = Directory.GetFiles(showcaseDirectory).Select(Path.GetFileName).Where(name => name is not null).Cast<string>().Where(name => !name.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)).Append("rdlc-feature-showcase-manifest.json").OrderBy(name => name, StringComparer.Ordinal).ToArray();
	var manifest = new
	{
		Name = "ReportViewerCore constrained RDLC feature showcase",
		Pages = document.Pages.Select((page, index) => new { Number = index + 1, page.Size.Width, page.Size.Height }).ToArray(),
		OutputFormats = new[] { "png", "pdf", "html", "xlsx", "docx" },
		Features = new[]
		{
			"RDLC page header and footer",
			"Fields, parameters, string concatenation, CountRows, Sum, Avg, Min, and Max",
			"Grouped tablix with sorting-ready data and ColSpan/RowSpan metadata",
			"Conditional visibility and hyperlink action",
			"Embedded image, rectangle, line, and nested report items",
			"Bar, column, line, area, pie, and doughnut charts"
		},
		Files = files
	};
	File.WriteAllText(Path.Combine(showcaseDirectory, "rdlc-feature-showcase-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
}

static void DrawOverview(IRenderCanvas canvas, RenderImage image)
{
	canvas.Clear(RenderColor.White);
	canvas.FillRectangle(new RenderRect(0, 0, canvas.Size.Width, 112), new RenderColor(24, 47, 78));
	canvas.DrawText("ReportViewer Core v2", new RenderPoint(36, 44), new FontRequest("Arial", 28, Bold: true), RenderColor.White);
	canvas.DrawText("Feature showcase — every portable canvas operation", new RenderPoint(36, 82), new FontRequest("Arial", 16, Italic: true), new RenderColor(220, 230, 242));

	canvas.DrawText("Text, style, direction, and hyperlinks", new RenderPoint(36, 154), new FontRequest("Arial", 18, Bold: true), RenderColor.Black);
	canvas.DrawLine(new RenderPoint(36, 170), new RenderPoint(724, 170), new RenderColor(100, 120, 145), 1);
	canvas.DrawText("Bold and italic text with preserved  spaces", new RenderPoint(36, 208), new FontRequest("Arial", 16, Bold: true, Italic: true), new RenderColor(34, 85, 140));
	canvas.DrawText("ภาษาไทย / العربية / 日本語", new RenderPoint(36, 244), new FontRequest("Arial", 16), RenderColor.Black);
	canvas.DrawText("RTL: تقرير محمول", new RenderPoint(36, 280), new FontRequest("Arial", 16), RenderColor.Black, TextDirection.RightToLeft);
	canvas.DrawText("Vertical: 縦書き", new RenderPoint(520, 280), new FontRequest("Arial", 14), RenderColor.Black, TextDirection.TopToBottom);
	canvas.DrawText("Reverse: 逆", new RenderPoint(680, 280), new FontRequest("Arial", 14), RenderColor.Black, TextDirection.BottomToTop);
	canvas.DrawText("Multiline text\nwith native line breaks", new RenderPoint(36, 330), new FontRequest("Arial", 14), new RenderColor(55, 65, 81));
	canvas.DrawHyperlink("Relative report link", new RenderPoint(36, 390), new FontRequest("Arial", 14), new RenderColor(20, 90, 180), "/reports/detail");
	canvas.DrawHyperlink("HTTP documentation link", new RenderPoint(220, 390), new FontRequest("Arial", 14), new RenderColor(20, 90, 180), "https://example.com/report");

	canvas.FillRectangle(new RenderRect(36, 430, 688, 4), new RenderColor(220, 226, 232));
	canvas.DrawText("Images, shapes, and table-cell metadata", new RenderPoint(36, 482), new FontRequest("Arial", 18, Bold: true), RenderColor.Black);
	canvas.DrawImage(image, new RenderRect(36, 512, 96, 96));
	canvas.DrawRectangle(new RenderRect(154, 512, 190, 96), new RenderColor(50, 90, 130), 2);
	canvas.FillRectangle(new RenderRect(174, 532, 150, 56), new RenderColor(233, 242, 252));
	canvas.DrawLine(new RenderPoint(365, 512), new RenderPoint(520, 608), new RenderColor(185, 60, 60), 3);
	canvas.DrawLine(new RenderPoint(520, 512), new RenderPoint(365, 608), new RenderColor(60, 130, 85), 3);
	canvas.DrawTableCell("Merged header (ColSpan=2)", new RenderPoint(36, 670), new RenderRect(36, 630, 330, 40), new FontRequest("Arial", 13, Bold: true), RenderColor.Black, columnSpan: 2);
	canvas.DrawTableCell("RowSpan", new RenderPoint(36, 730), new RenderRect(36, 670, 120, 80), new FontRequest("Arial", 13), RenderColor.Black, rowSpan: 2);
	canvas.DrawTableCell("Cell B", new RenderPoint(180, 710), new RenderRect(156, 670, 210, 40), new FontRequest("Arial", 13), RenderColor.Black, url: "https://example.com/cell");
	canvas.DrawTableCell("Cell C", new RenderPoint(180, 750), new RenderRect(156, 710, 210, 40), new FontRequest("Arial", 13), RenderColor.Black);
	canvas.DrawText("The same ReportDocument is exported to PNG, PDF, HTML, XLSX, and DOCX.", new RenderPoint(36, 850), new FontRequest("Arial", 15, Bold: true), new RenderColor(24, 47, 78));
	canvas.DrawText("Page 2 contains all six native chart kinds and page-boundary clipping cases.", new RenderPoint(36, 884), new FontRequest("Arial", 13), new RenderColor(75, 85, 99));
}

static void DrawChartsAndClipping(IRenderCanvas canvas, RenderImage image)
{
	canvas.Clear(new RenderColor(249, 250, 251));
	canvas.DrawText("Charts and page-boundary clipping", new RenderPoint(32, 38), new FontRequest("Arial", 22, Bold: true), RenderColor.Black);
	canvas.DrawText("Bar, column, line, area, pie, and doughnut use the shared chart contract.", new RenderPoint(32, 62), new FontRequest("Arial", 12), new RenderColor(75, 85, 99));

	var points = new[]
	{
		new RenderChartBar("Alpha", 12),
		new RenderChartBar("Beta", 8),
		new RenderChartBar("Gamma", 16),
		new RenderChartBar("Delta", 5)
	};
	FontRequest chartFont = new("Arial", 10);
	RenderColor chartColor = new(35, 111, 180);
	canvas.DrawBarChart("Bar shim", points, new RenderRect(32, 82, 245, 250), chartFont, chartColor);
	canvas.DrawChart(RenderChartType.Column, "Column", points, new RenderRect(297, 82, 245, 250), chartFont, chartColor);
	canvas.DrawChart(RenderChartType.Line, "Line", points, new RenderRect(562, 82, 245, 250), chartFont, chartColor);
	canvas.DrawChart(RenderChartType.Area, "Area", points, new RenderRect(32, 365, 245, 250), chartFont, chartColor);
	canvas.DrawChart(RenderChartType.Pie, "Pie", points, new RenderRect(297, 365, 245, 250), chartFont, chartColor);
	canvas.DrawChart(RenderChartType.Doughnut, "Doughnut", points, new RenderRect(562, 365, 245, 250), chartFont, chartColor);

	canvas.DrawText("Clipping cases (objects intentionally cross the page edge)", new RenderPoint(32, 672), new FontRequest("Arial", 15, Bold: true), RenderColor.Black);
	canvas.DrawImage(image, new RenderRect(-24, 690, 100, 80));
	canvas.FillRectangle(new RenderRect(-18, 790, 80, 40), new RenderColor(230, 130, 80));
	canvas.DrawLine(new RenderPoint(-20, 850), new RenderPoint(180, 690), new RenderColor(170, 50, 50), 3);
	canvas.DrawText("Visible portions remain inside the page in Skia, HTML, DOCX, and XLSX.", new RenderPoint(210, 760), new FontRequest("Arial", 12), RenderColor.Black);
}

file sealed record ShowcaseRow(string Category, string Name, decimal Amount);
