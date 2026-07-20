using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ReportViewerCore.Rendering;

namespace ReportViewerCore.Rendering.OpenXml;

public sealed class ExcelOpenXmlRenderer : IReportRenderer
{
	public ReportOutputFormat Format => ReportOutputFormat.ExcelOpenXml;

	public ReportOutput Render(ReportDocument document, ReportRenderOptions options)
	{
		OpenXmlPackageWriter.Validate(document, options, Format);
		return OpenXmlPackageWriter.WriteExcel(document);
	}
}

public sealed class WordOpenXmlRenderer : IReportRenderer
{
	public ReportOutputFormat Format => ReportOutputFormat.WordOpenXml;

	public ReportOutput Render(ReportDocument document, ReportRenderOptions options)
	{
		OpenXmlPackageWriter.Validate(document, options, Format);
		return OpenXmlPackageWriter.WriteWord(document);
	}
}

internal sealed class OpenXmlRenderCanvas : IRenderCanvas
{
	private readonly List<OpenXmlText> _texts = new();
	private readonly List<OpenXmlImage> _images = new();
	private readonly List<OpenXmlChart> _charts = new();
	private readonly List<OpenXmlShape> _shapes = new();
	private bool _disposed;

	public OpenXmlRenderCanvas(RenderSize size)
	{
		Size = size;
	}

	public RenderSize Size { get; }

	internal IReadOnlyList<OpenXmlText> Texts => _texts;
	internal IReadOnlyList<OpenXmlImage> Images => _images;
	internal IReadOnlyList<OpenXmlChart> Charts => _charts;
	internal IReadOnlyList<OpenXmlShape> Shapes => _shapes;

	public void Clear(RenderColor color)
	{
		ThrowIfDisposed();
	}

	public void FillRectangle(RenderRect rectangle, RenderColor color)
	{
		ThrowIfDisposed();
		_shapes.Add(new OpenXmlShape(rectangle, false, color, null, 0));
	}

	public void DrawRectangle(RenderRect rectangle, RenderColor color, float strokeWidth)
	{
		ThrowIfDisposed();
		_shapes.Add(new OpenXmlShape(rectangle, false, null, color, strokeWidth));
	}

	public void DrawLine(RenderPoint start, RenderPoint end, RenderColor color, float strokeWidth)
	{
		ThrowIfDisposed();
		_shapes.Add(new OpenXmlShape(new RenderRect(MathF.Min(start.X, end.X), MathF.Min(start.Y, end.Y), MathF.Abs(end.X - start.X), MathF.Abs(end.Y - start.Y)), true, null, color, strokeWidth));
	}

	public void DrawText(string text, RenderPoint baseline, FontRequest font, RenderColor color, TextDirection direction = TextDirection.LeftToRight)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);
		_texts.Add(new OpenXmlText(text, baseline, font, direction, null));
	}

	public void DrawHyperlink(string text, RenderPoint baseline, FontRequest font, RenderColor color, string url, TextDirection direction = TextDirection.LeftToRight)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);
		ArgumentException.ThrowIfNullOrWhiteSpace(url);
		_texts.Add(new OpenXmlText(text, baseline, font, direction, url));
	}

	public void DrawImage(RenderImage image, RenderRect destination)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(image);
		_images.Add(new OpenXmlImage(image, destination));
	}

	public void DrawBarChart(string title, IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(bars);
		_charts.Add(new OpenXmlChart(title, bars.ToArray(), destination));
		_texts.Add(new OpenXmlText(title, new RenderPoint(destination.X, destination.Y + font.Size), font with { Bold = true }, TextDirection.LeftToRight, null));
		for (int index = 0; index < bars.Count; index++)
		{
			RenderChartBar bar = bars[index];
			float y = destination.Y + font.Size + 8 + index * MathF.Max(font.Size * 1.8f, 20);
			_texts.Add(new OpenXmlText(bar.Label, new RenderPoint(destination.X, y), font, TextDirection.LeftToRight, null));
			_texts.Add(new OpenXmlText(bar.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), new RenderPoint(destination.X + destination.Width * 0.6f, y), font, TextDirection.LeftToRight, null));
		}
	}

	public void Dispose()
	{
		_disposed = true;
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal sealed record OpenXmlText(
	string Text,
	RenderPoint Baseline,
	FontRequest Font,
	TextDirection Direction,
	string? Url);

internal sealed record OpenXmlImage(RenderImage Image, RenderRect Destination);

internal sealed record OpenXmlPage(
	IReadOnlyList<OpenXmlText> Texts,
	IReadOnlyList<OpenXmlImage> Images,
	IReadOnlyList<OpenXmlChart> Charts,
	IReadOnlyList<OpenXmlShape> Shapes);

internal sealed record OpenXmlChart(string Title, IReadOnlyList<RenderChartBar> Bars, RenderRect Destination);

internal sealed record OpenXmlShape(RenderRect Bounds, bool IsLine, RenderColor? Fill, RenderColor? Stroke, float StrokeWidth);

internal static class OpenXmlPackageWriter
{
	private const string OfficeDocumentRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
	private const string WorksheetRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
	private const string HyperlinkRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
	private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
	private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
	private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
	private static readonly XNamespace OfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
	private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
	private static readonly XNamespace SpreadsheetDrawing = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
	private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
	private static readonly XNamespace WordDrawing = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
	private static readonly XNamespace Picture = "http://schemas.openxmlformats.org/drawingml/2006/picture";
	private static readonly XNamespace Vml = "urn:schemas-microsoft-com:vml";
	private static readonly XNamespace Chart = "http://schemas.openxmlformats.org/drawingml/2006/chart";

	internal static ReportOutput WriteExcel(ReportDocument document)
	{
		var pages = Capture(document);
		using var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			WriteEntry(archive, "[Content_Types].xml", ExcelContentTypesWithCharts(pages));
			WriteEntry(archive, "_rels/.rels", PackageRelationships("/xl/workbook.xml", OfficeDocumentRelationship));

			var workbookRelationships = new XElement(Relationships + "Relationships");
			for (int i = 0; i < pages.Count; i++)
			{
				workbookRelationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{i + 1}"), new XAttribute("Type", WorksheetRelationship), new XAttribute("Target", $"worksheets/sheet{i + 1}.xml")));
				WriteEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", ExcelSheet(pages[i], i + 1));
				WriteEntry(archive, $"xl/worksheets/_rels/sheet{i + 1}.xml.rels", ExcelSheetRelationships(pages[i], i + 1));
				if (pages[i].Images.Count > 0 || pages[i].Charts.Count > 0 || pages[i].Shapes.Count > 0)
				{
					WriteEntry(archive, $"xl/drawings/drawing{i + 1}.xml", ExcelDrawing(pages[i], i + 1));
					WriteEntry(archive, $"xl/drawings/_rels/drawing{i + 1}.xml.rels", ExcelDrawingRelationships(pages[i], i + 1));
					foreach ((OpenXmlImage image, int imageIndex) in pages[i].Images.Select((image, index) => (image, index)))
					{
						WriteBinaryEntry(archive, $"xl/media/image{i + 1}_{imageIndex + 1}.png", image.Image.PngData);
					}
					foreach ((OpenXmlChart chart, int chartIndex) in pages[i].Charts.Select((chart, index) => (chart, index)))
					{
						WriteEntry(archive, $"xl/charts/chart{i + 1}_{chartIndex + 1}.xml", ExcelChart(chart));
					}
				}
		}
			string stylesRelationshipId = $"rId{pages.Count + 1}";
			workbookRelationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", stylesRelationshipId), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")));

			WriteEntry(archive, "xl/workbook.xml", ExcelWorkbook(pages.Count));
			WriteEntry(archive, "xl/styles.xml", ExcelStyles());
			WriteEntry(archive, "xl/_rels/workbook.xml.rels", workbookRelationships);
		}

		return new ReportOutput(ReportOutputFormat.ExcelOpenXml, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx", stream.ToArray());
	}

	internal static ReportOutput WriteWord(ReportDocument document)
	{
		var pages = Capture(document);
		using var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			WriteEntry(archive, "[Content_Types].xml", WordContentTypes(pages));
			WriteEntry(archive, "_rels/.rels", PackageRelationships("/word/document.xml", OfficeDocumentRelationship));
			var relationships = new XElement(Relationships + "Relationships");
			var documentXml = new XElement(Word + "document", new XAttribute(XNamespace.Xmlns + "w", Word), new XAttribute(XNamespace.Xmlns + "r", OfficeDocument), new XElement(Word + "body"));
			XElement body = documentXml.Element(Word + "body")!;
			int hyperlinkId = 1;
			int imageIndex = 1;
			int chartIndex = 1;
			foreach (OpenXmlPage page in pages)
			{
				foreach (OpenXmlText text in page.Texts)
				{
					XElement run = WordRun(text);
					if (text.Url is null)
					{
						body.Add(new XElement(Word + "p", WordParagraphProperties(text), run));
					}
					else
					{
						string id = $"rId{hyperlinkId++}";
						relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", id), new XAttribute("Type", HyperlinkRelationship), new XAttribute("Target", text.Url), new XAttribute("TargetMode", "External")));
						body.Add(new XElement(Word + "p", WordParagraphProperties(text), new XElement(Word + "hyperlink", new XAttribute(OfficeDocument + "id", id), run)));
					}
				}
				foreach (OpenXmlImage image in page.Images)
				{
					string id = $"rId{hyperlinkId++}";
					relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", id), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"), new XAttribute("Target", $"media/image{imageIndex}.png")));
					body.Add(new XElement(Word + "p", WordImage(image, id, imageIndex)));
					WriteBinaryEntry(archive, $"word/media/image{imageIndex}.png", image.Image.PngData);
					imageIndex++;
				}
				foreach (OpenXmlChart chart in page.Charts)
				{
					string id = $"rId{hyperlinkId++}";
					string chartPath = $"charts/chart{chartIndex}.xml";
					relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", id), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"), new XAttribute("Target", chartPath)));
					body.Add(new XElement(Word + "p", WordChart(chart, id, chartIndex)));
					WriteEntry(archive, $"word/{chartPath}", ExcelChart(chart));
					chartIndex++;
				}
				foreach ((OpenXmlShape shape, int shapeIndex) in page.Shapes.Select((shape, index) => (shape, index)))
				{
					body.Add(new XElement(Word + "p", WordShape(shape, 300 + shapeIndex)));
				}
				body.Add(new XElement(Word + "p", new XElement(Word + "r", new XElement(Word + "br", new XAttribute(Word + "type", "page")))));
			}
			body.Add(new XElement(Word + "sectPr", new XElement(Word + "pgSz", new XAttribute(Word + "w", "11906"), new XAttribute(Word + "h", "16838"))));
			WriteEntry(archive, "word/document.xml", documentXml);
			WriteEntry(archive, "word/_rels/document.xml.rels", relationships);
		}

		return new ReportOutput(ReportOutputFormat.WordOpenXml, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx", stream.ToArray());
	}

	private static List<OpenXmlPage> Capture(ReportDocument document)
	{
		var pages = new List<OpenXmlPage>(document.Pages.Count);
		foreach (ReportPage page in document.Pages)
		{
			using var canvas = new OpenXmlRenderCanvas(page.Size);
			page.Render(canvas);
				pages.Add(new OpenXmlPage(canvas.Texts, canvas.Images, canvas.Charts, canvas.Shapes));
		}
		return pages;
	}

	private static XElement ExcelWorkbook(int pageCount) => new(Spreadsheet + "workbook", new XAttribute(XNamespace.Xmlns + "r", OfficeDocument), new XElement(Spreadsheet + "sheets", Enumerable.Range(1, pageCount).Select(i => new XElement(Spreadsheet + "sheet", new XAttribute("name", $"Page {i}"), new XAttribute("sheetId", i), new XAttribute(OfficeDocument + "id", $"rId{i}")))));

	private static XElement ExcelSheet(OpenXmlPage page, int pageNumber)
	{
		IReadOnlyList<OpenXmlText> texts = page.Texts;
		var rows = new Dictionary<int, List<(int Column, OpenXmlText Text)>>();
		foreach (OpenXmlText text in texts)
		{
			int row = Math.Max(1, (int)MathF.Floor(text.Baseline.Y / 20) + 1);
			int column = Math.Max(1, (int)MathF.Floor(text.Baseline.X / 64) + 1);
			if (!rows.TryGetValue(row, out List<(int Column, OpenXmlText Text)>? values))
			{
				values = new List<(int Column, OpenXmlText Text)>();
				rows[row] = values;
			}
			while (values.Any(item => item.Column == column))
			{
				column++;
			}
			values.Add((column, text));
		}

		var sheetData = new XElement(Spreadsheet + "sheetData", rows.OrderBy(pair => pair.Key).Select(pair => new XElement(Spreadsheet + "row", new XAttribute("r", pair.Key), pair.Value.Select(cell => ExcelCell(cell.Column, pair.Key, cell.Text)))));
		var sheet = new XElement(Spreadsheet + "worksheet", new XAttribute(XNamespace.Xmlns + "r", OfficeDocument), sheetData);
		var hyperlinks = texts.Where(text => text.Url is not null).Select((text, index) => new XElement(Spreadsheet + "hyperlink", new XAttribute("ref", ExcelCellReference(text, index)), new XAttribute(OfficeDocument + "id", $"rId{index + 1}"))).ToArray();
		if (hyperlinks.Length > 0)
		{
			sheet.Add(new XElement(Spreadsheet + "hyperlinks", hyperlinks));
		}
		if (page.Images.Count > 0 || page.Charts.Count > 0 || page.Shapes.Count > 0)
		{
			sheet.Add(new XElement(Spreadsheet + "drawing", new XAttribute(OfficeDocument + "id", $"rId{hyperlinks.Length + 1}")));
		}
		return sheet;
	}

	private static XElement ExcelCell(int column, int row, OpenXmlText text)
	{
		int styleIndex = ExcelStyleIndex(text.Direction);
		return new XElement(Spreadsheet + "c",
			new XAttribute("r", ExcelColumn(column) + row),
			new XAttribute("t", "inlineStr"),
			styleIndex > 0 ? new XAttribute("s", styleIndex) : null,
			new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", text.Text)));
	}

	private static int ExcelStyleIndex(TextDirection direction) => direction switch
	{
		TextDirection.TopToBottom => 1,
		TextDirection.BottomToTop => 2,
		TextDirection.RightToLeft => 3,
		_ => 0
	};

	private static string ExcelCellReference(OpenXmlText text, int index) => ExcelColumn(Math.Max(1, (int)MathF.Floor(text.Baseline.X / 64) + 1 + index)) + Math.Max(1, (int)MathF.Floor(text.Baseline.Y / 20) + 1);

	private static string ExcelColumn(int value)
	{
		var result = new StringBuilder();
		while (value > 0)
		{
			value--;
			result.Insert(0, (char)('A' + value % 26));
			value /= 26;
		}
		return result.ToString();
	}

	private static XElement ExcelSheetRelationships(OpenXmlPage page, int pageNumber)
	{
		var relationships = new XElement(Relationships + "Relationships");
		IReadOnlyList<OpenXmlText> texts = page.Texts;
		foreach ((OpenXmlText text, int index) in texts.Where(text => text.Url is not null).Select((text, index) => (text, index)))
		{
			relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{index + 1}"), new XAttribute("Type", HyperlinkRelationship), new XAttribute("Target", text.Url!), new XAttribute("TargetMode", "External")));
		}
		if (page.Images.Count > 0)
		{
			relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{texts.Count(text => text.Url is not null) + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"), new XAttribute("Target", $"../drawings/drawing{pageNumber}.xml")));
		}
		else if (page.Charts.Count > 0 || page.Shapes.Count > 0)
		{
			relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{texts.Count(text => text.Url is not null) + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"), new XAttribute("Target", $"../drawings/drawing{pageNumber}.xml")));
		}
		return relationships;
	}

	private static XElement ExcelDrawing(OpenXmlPage page, int pageNumber)
	{
		var root = new XElement(SpreadsheetDrawing + "wsDr", new XAttribute(XNamespace.Xmlns + "xdr", SpreadsheetDrawing), new XAttribute(XNamespace.Xmlns + "a", Drawing), new XAttribute(XNamespace.Xmlns + "r", OfficeDocument));
		foreach ((OpenXmlImage image, int index) in page.Images.Select((image, index) => (image, index)))
		{
			int column = Math.Max(0, (int)MathF.Floor(image.Destination.X / 64));
			int row = Math.Max(0, (int)MathF.Floor(image.Destination.Y / 20));
			root.Add(new XElement(SpreadsheetDrawing + "oneCellAnchor",
				new XElement(SpreadsheetDrawing + "from", new XElement(SpreadsheetDrawing + "col", column), new XElement(SpreadsheetDrawing + "colOff", 0), new XElement(SpreadsheetDrawing + "row", row), new XElement(SpreadsheetDrawing + "rowOff", 0)),
				new XElement(SpreadsheetDrawing + "ext", new XAttribute("cx", ToEmu(image.Destination.Width)), new XAttribute("cy", ToEmu(image.Destination.Height))),
				PictureElement(image, index + 1, $"rId{index + 1}"),
				new XElement(SpreadsheetDrawing + "clientData")));
		}
		foreach ((OpenXmlChart chart, int index) in page.Charts.Select((chart, index) => (chart, index)))
		{
			int relationshipIndex = page.Images.Count + index + 1;
			root.Add(new XElement(SpreadsheetDrawing + "twoCellAnchor",
				new XElement(SpreadsheetDrawing + "from", new XElement(SpreadsheetDrawing + "col", Math.Max(0, (int)MathF.Floor(chart.Destination.X / 64))), new XElement(SpreadsheetDrawing + "colOff", 0), new XElement(SpreadsheetDrawing + "row", Math.Max(0, (int)MathF.Floor(chart.Destination.Y / 20))), new XElement(SpreadsheetDrawing + "rowOff", 0)),
				new XElement(SpreadsheetDrawing + "to", new XElement(SpreadsheetDrawing + "col", Math.Max(0, (int)MathF.Floor(chart.Destination.Right / 64))), new XElement(SpreadsheetDrawing + "colOff", 0), new XElement(SpreadsheetDrawing + "row", Math.Max(0, (int)MathF.Floor(chart.Destination.Bottom / 20))), new XElement(SpreadsheetDrawing + "rowOff", 0)),
				new XElement(SpreadsheetDrawing + "graphicFrame", new XAttribute("macro", string.Empty),
					new XElement(SpreadsheetDrawing + "nvGraphicFramePr", new XElement(SpreadsheetDrawing + "cNvPr", new XAttribute("id", 100 + index), new XAttribute("name", $"Chart {index + 1}")), new XElement(SpreadsheetDrawing + "cNvGraphicFramePr")),
					new XElement(Drawing + "xfrm", new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(Drawing + "ext", new XAttribute("cx", ToEmu(chart.Destination.Width)), new XAttribute("cy", ToEmu(chart.Destination.Height)))),
					new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData", new XAttribute("uri", Chart.NamespaceName), new XElement(Chart + "chart", new XAttribute(OfficeDocument + "id", $"rId{relationshipIndex}"))))),
				new XElement(SpreadsheetDrawing + "clientData")));
		}
		foreach ((OpenXmlShape shape, int index) in page.Shapes.Select((shape, index) => (shape, index)))
		{
			int column = Math.Max(0, (int)MathF.Floor(shape.Bounds.X / 64));
			int row = Math.Max(0, (int)MathF.Floor(shape.Bounds.Y / 20));
			root.Add(new XElement(SpreadsheetDrawing + "oneCellAnchor",
				new XElement(SpreadsheetDrawing + "from", new XElement(SpreadsheetDrawing + "col", column), new XElement(SpreadsheetDrawing + "colOff", 0), new XElement(SpreadsheetDrawing + "row", row), new XElement(SpreadsheetDrawing + "rowOff", 0)),
				new XElement(SpreadsheetDrawing + "ext", new XAttribute("cx", ToEmu(shape.Bounds.Width)), new XAttribute("cy", ToEmu(shape.Bounds.Height))),
				ExcelShape(shape, 300 + index),
				new XElement(SpreadsheetDrawing + "clientData")));
		}
		return root;
	}

	private static XElement ExcelShape(OpenXmlShape shape, int id)
	{
		XElement shapeProperties = new(Drawing + "spPr",
			new XElement(Drawing + "xfrm", new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(Drawing + "ext", new XAttribute("cx", ToEmu(shape.Bounds.Width)), new XAttribute("cy", ToEmu(shape.Bounds.Height)))),
			new XElement(Drawing + "prstGeom", new XAttribute("prst", shape.IsLine ? "line" : "rect"), new XElement(Drawing + "avLst")),
			shape.Fill is RenderColor fill ? SolidFill(fill) : new XElement(Drawing + "noFill"),
			shape.Stroke is RenderColor stroke ? new XElement(Drawing + "ln", new XAttribute("w", ToEmu(MathF.Max(0.5f, shape.StrokeWidth))), SolidFill(stroke)) : null);
		return new XElement(SpreadsheetDrawing + "sp",
			new XElement(SpreadsheetDrawing + "nvSpPr", new XElement(SpreadsheetDrawing + "cNvPr", new XAttribute("id", id), new XAttribute("name", shape.IsLine ? $"Line {id}" : $"Rectangle {id}")), new XElement(SpreadsheetDrawing + "cNvSpPr")),
			shapeProperties);
	}

	private static XElement SolidFill(RenderColor color) => new(Drawing + "solidFill", new XElement(Drawing + "srgbClr", new XAttribute("val", $"{color.Red:X2}{color.Green:X2}{color.Blue:X2}")));

	private static XElement ExcelDrawingRelationships(OpenXmlPage page, int pageNumber)
	{
		var relationships = new XElement(Relationships + "Relationships", page.Images.Select((image, index) => new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{index + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"), new XAttribute("Target", $"../media/image{pageNumber}_{index + 1}.png"))));
		foreach ((OpenXmlChart chart, int index) in page.Charts.Select((chart, index) => (chart, index)))
		{
			relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{page.Images.Count + index + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"), new XAttribute("Target", $"../charts/chart{pageNumber}_{index + 1}.xml")));
		}
		return relationships;
	}

	private static XElement ExcelChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		XNamespace a = Drawing;
		return new XElement(c + "chartSpace", new XAttribute(XNamespace.Xmlns + "c", c), new XAttribute(XNamespace.Xmlns + "a", a), new XAttribute(XNamespace.Xmlns + "r", OfficeDocument),
			new XElement(c + "chart",
				new XElement(c + "autoTitleDeleted", new XAttribute("val", 0)),
				new XElement(c + "title", new XElement(c + "tx", new XElement(c + "rich", new XElement(a + "bodyPr"), new XElement(a + "lstStyle"), new XElement(a + "p", new XElement(a + "r", new XElement(a + "rPr", new XAttribute("lang", "en-US")), new XElement(a + "t", chart.Title))))), new XElement(c + "layout")),
				new XElement(c + "plotArea", new XElement(c + "layout"), BarChart(chart)),
				new XElement(c + "plotVisOnly", new XAttribute("val", 1)),
				new XElement(c + "dispBlanksAs", new XAttribute("val", "gap"))));
	}

	private static XElement BarChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		var series = new XElement(c + "ser",
			new XElement(c + "idx", new XAttribute("val", 0)),
			new XElement(c + "order", new XAttribute("val", 0)),
			new XElement(c + "tx", new XElement(c + "v", chart.Title)),
			new XElement(c + "cat", new XElement(c + "strLit", new XElement(c + "ptCount", new XAttribute("val", chart.Bars.Count)), chart.Bars.Select((bar, index) => new XElement(c + "pt", new XAttribute("idx", index), new XElement(c + "v", bar.Label))))),
			new XElement(c + "val", new XElement(c + "numLit", new XElement(c + "formatCode", "General"), new XElement(c + "ptCount", new XAttribute("val", chart.Bars.Count)), chart.Bars.Select((bar, index) => new XElement(c + "pt", new XAttribute("idx", index), new XElement(c + "v", bar.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))))));
		return new XElement(c + "barChart", new XElement(c + "barDir", new XAttribute("val", "bar")), new XElement(c + "grouping", new XAttribute("val", "clustered")), new XElement(c + "varyColors", new XAttribute("val", 0)), series, new XElement(c + "axId", new XAttribute("val", 1)), new XElement(c + "axId", new XAttribute("val", 2)));
	}

	private static XElement PictureElement(OpenXmlImage image, int id, string relationshipId)
	{
		return new XElement(SpreadsheetDrawing + "pic",
			new XElement(SpreadsheetDrawing + "nvPicPr", new XElement(SpreadsheetDrawing + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Image {id}")), new XElement(SpreadsheetDrawing + "cNvPicPr")),
			new XElement(SpreadsheetDrawing + "blipFill", new XElement(Drawing + "blip", new XAttribute(OfficeDocument + "embed", relationshipId)), new XElement(Drawing + "stretch", new XElement(Drawing + "fillRect"))),
			new XElement(SpreadsheetDrawing + "spPr", new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst"))));
	}

	private static XElement WordImage(OpenXmlImage image, string relationshipId, int id)
	{
		long cx = ToEmu(image.Destination.Width);
		long cy = ToEmu(image.Destination.Height);
		return new XElement(Word + "r", new XElement(Word + "drawing", new XElement(WordDrawing + "inline", new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 0), new XAttribute("distR", 0),
			new XElement(WordDrawing + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
			new XElement(WordDrawing + "docPr", new XAttribute("id", id), new XAttribute("name", $"Image {id}")),
			new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData", new XAttribute("uri", Picture.NamespaceName), new XElement(Picture + "pic",
				new XElement(Picture + "nvPicPr", new XElement(Picture + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Image {id}")), new XElement(Picture + "cNvPicPr")),
				new XElement(Picture + "blipFill", new XElement(Drawing + "blip", new XAttribute(OfficeDocument + "embed", relationshipId)), new XElement(Drawing + "stretch", new XElement(Drawing + "fillRect"))),
				new XElement(Picture + "spPr", new XElement(Drawing + "xfrm", new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(Drawing + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))), new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst")))))))));
	}

	private static XElement WordShape(OpenXmlShape shape, int id)
	{
		string style = $"position:absolute;left:{shape.Bounds.X:0.###}pt;top:{shape.Bounds.Y:0.###}pt;width:{shape.Bounds.Width:0.###}pt;height:{shape.Bounds.Height:0.###}pt";
		return new XElement(Word + "pict",
			new XAttribute(XNamespace.Xmlns + "v", Vml),
			new XElement(Vml + (shape.IsLine ? "line" : "rect"),
				new XAttribute("style", style),
				new XAttribute("filled", shape.Fill is null ? "f" : "t"),
				shape.Fill is RenderColor fill ? new XAttribute("fillcolor", $"#{fill.Red:X2}{fill.Green:X2}{fill.Blue:X2}") : null,
				new XAttribute("stroked", shape.Stroke is null ? "f" : "t"),
				shape.Stroke is RenderColor stroke ? new XAttribute("strokecolor", $"#{stroke.Red:X2}{stroke.Green:X2}{stroke.Blue:X2}") : null,
				shape.Stroke is not null ? new XAttribute("strokeweight", $"{MathF.Max(0.5f, shape.StrokeWidth):0.###}pt") : null));
	}

	private static XElement WordChart(OpenXmlChart chart, string relationshipId, int id)
	{
		long cx = ToEmu(chart.Destination.Width);
		long cy = ToEmu(chart.Destination.Height);
		return new XElement(Word + "r", new XElement(Word + "drawing", new XElement(WordDrawing + "inline", new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 0), new XAttribute("distR", 0),
			new XElement(WordDrawing + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
			new XElement(WordDrawing + "docPr", new XAttribute("id", 200 + id), new XAttribute("name", $"Chart {id}")),
			new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData", new XAttribute("uri", Chart.NamespaceName), new XElement(Chart + "chart", new XAttribute(OfficeDocument + "id", relationshipId)))))));
	}

	private static long ToEmu(float points) => Math.Max(1, (long)Math.Round(points * 12700, MidpointRounding.AwayFromZero));

	private static XElement? WordParagraphProperties(OpenXmlText text)
	{
		return text.Direction switch
		{
			TextDirection.RightToLeft => new XElement(Word + "pPr", new XElement(Word + "bidi")),
			TextDirection.TopToBottom => new XElement(Word + "pPr", new XElement(Word + "textDirection", new XAttribute(Word + "val", "tbRl"))),
			TextDirection.BottomToTop => new XElement(Word + "pPr", new XElement(Word + "textDirection", new XAttribute(Word + "val", "btLr"))),
			_ => null
		};
	}

	private static XElement WordRun(OpenXmlText text) => new(Word + "r", new XElement(Word + "rPr", text.Font.Bold ? new XElement(Word + "b") : null, text.Font.Italic ? new XElement(Word + "i") : null), new XElement(Word + "t", text.Text));

	private static XElement PackageRelationships(string target, string type) => new(Relationships + "Relationships", new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", type), new XAttribute("Target", target)));

	private static XElement ExcelContentTypesWithCharts(IReadOnlyList<OpenXmlPage> pages)
	{
		var entries = new List<object>
		{
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
			new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
			new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))
		};
		for (int index = 0; index < pages.Count; index++)
		{
			OpenXmlPage page = pages[index];
			entries.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{index + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
			if (page.Images.Count > 0 || page.Charts.Count > 0 || page.Shapes.Count > 0)
			{
				entries.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/xl/drawings/drawing{index + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
			}
			for (int chartIndex = 0; chartIndex < page.Charts.Count; chartIndex++)
			{
				entries.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/xl/charts/chart{index + 1}_{chartIndex + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml")));
			}
		}
		return new XElement(ContentTypes + "Types", entries);
	}

	private static XElement ExcelStyles()
	{
		return new XElement(Spreadsheet + "styleSheet",
			new XElement(Spreadsheet + "numFmts", new XAttribute("count", 0)),
			new XElement(Spreadsheet + "fonts", new XAttribute("count", 1), new XElement(Spreadsheet + "font", new XElement(Spreadsheet + "sz", new XAttribute("val", 11)), new XElement(Spreadsheet + "color", new XAttribute("theme", 1)), new XElement(Spreadsheet + "name", new XAttribute("val", "Arial")))),
			new XElement(Spreadsheet + "fills", new XAttribute("count", 2), new XElement(Spreadsheet + "fill", new XElement(Spreadsheet + "patternFill", new XAttribute("patternType", "none"))), new XElement(Spreadsheet + "fill", new XElement(Spreadsheet + "patternFill", new XAttribute("patternType", "gray125")))),
			new XElement(Spreadsheet + "borders", new XAttribute("count", 1), new XElement(Spreadsheet + "border", new XElement(Spreadsheet + "left"), new XElement(Spreadsheet + "right"), new XElement(Spreadsheet + "top"), new XElement(Spreadsheet + "bottom"), new XElement(Spreadsheet + "diagonal"))),
			new XElement(Spreadsheet + "cellStyleXfs", new XAttribute("count", 1), new XElement(Spreadsheet + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
			new XElement(Spreadsheet + "cellXfs", new XAttribute("count", 4),
				new XElement(Spreadsheet + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0)),
				new XElement(Spreadsheet + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), new XElement(Spreadsheet + "alignment", new XAttribute("textRotation", 255))),
				new XElement(Spreadsheet + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), new XElement(Spreadsheet + "alignment", new XAttribute("textRotation", 90))),
				new XElement(Spreadsheet + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), new XElement(Spreadsheet + "alignment", new XAttribute("readingOrder", 2)))),
			new XElement(Spreadsheet + "cellStyles", new XAttribute("count", 1), new XElement(Spreadsheet + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))),
			new XElement(Spreadsheet + "dxfs", new XAttribute("count", 0)),
			new XElement(Spreadsheet + "tableStyles", new XAttribute("count", 0), new XAttribute("defaultTableStyle", "TableStyleMedium2"), new XAttribute("defaultPivotStyle", "PivotStyleLight16")));
	}

	private static XElement ExcelContentTypes(IReadOnlyList<OpenXmlPage> pages) => new(ContentTypes + "Types", new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")), new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")), new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")), pages.Select((page, index) => new object[] { new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{index + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")), page.Images.Count > 0 ? new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/xl/drawings/drawing{index + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")) : null }).SelectMany(items => items).Where(item => item is not null)!);

	private static XElement WordContentTypes(IReadOnlyList<OpenXmlPage> pages)
	{
		var entries = new List<object>
		{
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
			new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))
		};
		int chartIndex = 1;
		foreach (OpenXmlPage page in pages)
		{
			foreach (OpenXmlChart chart in page.Charts)
			{
				entries.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/word/charts/chart{chartIndex++}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml")));
			}
		}
		return new XElement(ContentTypes + "Types", entries);
	}

	private static void WriteBinaryEntry(ZipArchive archive, string path, ReadOnlyMemory<byte> data)
	{
		ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
		using Stream stream = entry.Open();
		stream.Write(data.Span);
	}

	private static void WriteEntry(ZipArchive archive, string path, XElement xml)
	{
		ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
		using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
		writer.Write(xml.ToString(SaveOptions.DisableFormatting));
	}

	internal static void Validate(ReportDocument document, ReportRenderOptions options, ReportOutputFormat format)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(options);
		if (options.Format != format)
		{
			throw new ArgumentException($"This renderer only supports {format}.", nameof(options));
		}
	}
}
