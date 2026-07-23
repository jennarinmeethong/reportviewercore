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
		_shapes.Add(new OpenXmlShape(new RenderRect(MathF.Min(start.X, end.X), MathF.Min(start.Y, end.Y), MathF.Abs(end.X - start.X), MathF.Abs(end.Y - start.Y)), true, null, color, strokeWidth, start, end));
	}

	public void DrawText(string text, RenderPoint baseline, FontRequest font, RenderColor color, TextDirection direction = TextDirection.LeftToRight)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);
		_texts.Add(new OpenXmlText(text, baseline, font, color, direction, null));
	}

	public void DrawTableCell(string text, RenderPoint baseline, RenderRect bounds, FontRequest font, RenderColor color, string? url = null, TextDirection direction = TextDirection.LeftToRight, int columnSpan = 1, int rowSpan = 1)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);
		if (url is not null)
		{
			RenderUrlPolicy.ValidateHyperlink(url);
		}
		if (columnSpan < 1 || rowSpan < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(columnSpan), "Table cell spans must be positive.");
		}
		_texts.Add(new OpenXmlText(text, baseline, font, color, direction, url, bounds, columnSpan, rowSpan));
	}

	public void DrawHyperlink(string text, RenderPoint baseline, FontRequest font, RenderColor color, string url, TextDirection direction = TextDirection.LeftToRight)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);
		ArgumentException.ThrowIfNullOrWhiteSpace(url);
		RenderUrlPolicy.ValidateHyperlink(url);
		_texts.Add(new OpenXmlText(text, baseline, font, color, direction, url));
	}

	public void DrawImage(RenderImage image, RenderRect destination)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(image);
		_images.Add(new OpenXmlImage(image, destination));
	}

	public void DrawBarChart(string title, IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color)
	{
		DrawChart(RenderChartType.Bar, title, bars, destination, font, color);
	}

	public void DrawChart(RenderChartType chartType, string title, IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(points);
		_charts.Add(new OpenXmlChart(chartType, title, points.ToArray(), destination));
		_texts.Add(new OpenXmlText(title, new RenderPoint(destination.X, destination.Y + font.Size), font with { Bold = true }, color, TextDirection.LeftToRight, null));
		for (int index = 0; index < points.Count; index++)
		{
			RenderChartBar bar = points[index];
			float y = destination.Y + font.Size + 8 + index * MathF.Max(font.Size * 1.8f, 20);
			_texts.Add(new OpenXmlText(bar.Label, new RenderPoint(destination.X, y), font, color, TextDirection.LeftToRight, null));
			_texts.Add(new OpenXmlText(bar.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), new RenderPoint(destination.X + destination.Width * 0.6f, y), font, color, TextDirection.LeftToRight, null));
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
	RenderColor Color,
	TextDirection Direction,
	string? Url,
	RenderRect? TableCellBounds = null,
	int ColumnSpan = 1,
	int RowSpan = 1);

internal sealed record OpenXmlImage(RenderImage Image, RenderRect Destination, OpenXmlImageCrop? Crop = null);

internal readonly record struct OpenXmlImageCrop(int Left, int Top, int Right, int Bottom)
{
	public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

internal sealed record OpenXmlPage(
	IReadOnlyList<OpenXmlText> Texts,
	IReadOnlyList<OpenXmlImage> Images,
	IReadOnlyList<OpenXmlChart> Charts,
	IReadOnlyList<OpenXmlShape> Shapes,
	RenderSize Size);

internal sealed record OpenXmlChart(RenderChartType Type, string Title, IReadOnlyList<RenderChartBar> Bars, RenderRect Destination);

internal sealed record OpenXmlShape(RenderRect Bounds, bool IsLine, RenderColor? Fill, RenderColor? Stroke, float StrokeWidth, RenderPoint? Start = null, RenderPoint? End = null);

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
			for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
			{
				OpenXmlPage page = pages[pageIndex];
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
				if (pageIndex < pages.Count - 1)
				{
					body.Add(new XElement(Word + "p", new XElement(Word + "pPr", WordSectionProperties(page.Size, true))));
				}
			}
			OpenXmlPage lastPage = pages[^1];
			body.Add(WordSectionProperties(lastPage.Size, false));
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
			IReadOnlyList<OpenXmlImage> images = canvas.Images
				.Select(image => ClipImageToPage(image, canvas.Size))
				.OfType<OpenXmlImage>()
				.ToArray();
			IReadOnlyList<OpenXmlShape> shapes = canvas.Shapes
				.Select(shape => ClipShapeToPage(shape, canvas.Size))
				.OfType<OpenXmlShape>()
				.ToArray();
			pages.Add(new OpenXmlPage(canvas.Texts, images, canvas.Charts, shapes, canvas.Size));
		}
		return pages;
	}

	private static OpenXmlImage? ClipImageToPage(OpenXmlImage image, RenderSize pageSize)
	{
		RenderRect destination = image.Destination;
		if (!float.IsFinite(destination.X) || !float.IsFinite(destination.Y) || !float.IsFinite(destination.Width) || !float.IsFinite(destination.Height) || destination.Width <= 0 || destination.Height <= 0)
		{
			return null;
		}

		float left = MathF.Max(0, destination.X);
		float top = MathF.Max(0, destination.Y);
		float right = MathF.Min(pageSize.Width, destination.Right);
		float bottom = MathF.Min(pageSize.Height, destination.Bottom);
		if (right <= left || bottom <= top)
		{
			return null;
		}

		var clipped = new RenderRect(left, top, right - left, bottom - top);
		var crop = new OpenXmlImageCrop(
			ToCropPercentage((left - destination.X) / destination.Width),
			ToCropPercentage((top - destination.Y) / destination.Height),
			ToCropPercentage((destination.Right - right) / destination.Width),
			ToCropPercentage((destination.Bottom - bottom) / destination.Height));
		return image with { Destination = clipped, Crop = crop.IsEmpty ? null : crop };
	}

	private static int ToCropPercentage(float value) => Math.Clamp((int)MathF.Round(value * 100000, MidpointRounding.AwayFromZero), 0, 100000);

	private static OpenXmlShape? ClipShapeToPage(OpenXmlShape shape, RenderSize pageSize)
	{
		if (!IsFinite(shape.Bounds) || shape.Bounds.Width < 0 || shape.Bounds.Height < 0)
		{
			return null;
		}

		if (!shape.IsLine)
		{
			RenderRect bounds = Intersect(shape.Bounds, new RenderRect(0, 0, pageSize.Width, pageSize.Height));
			return bounds.Width <= 0 || bounds.Height <= 0 ? null : shape with { Bounds = bounds };
		}

		if (shape.Start is not RenderPoint start || shape.End is not RenderPoint end || !IsFinite(start) || !IsFinite(end) || !ClipLine(start, end, pageSize, out RenderPoint clippedStart, out RenderPoint clippedEnd))
		{
			return null;
		}

		var clippedBounds = new RenderRect(
			MathF.Min(clippedStart.X, clippedEnd.X),
			MathF.Min(clippedStart.Y, clippedEnd.Y),
			MathF.Abs(clippedEnd.X - clippedStart.X),
			MathF.Abs(clippedEnd.Y - clippedStart.Y));
		return shape with { Bounds = clippedBounds, Start = clippedStart, End = clippedEnd };
	}

	private static RenderRect Intersect(RenderRect first, RenderRect second)
	{
		float left = MathF.Max(first.X, second.X);
		float top = MathF.Max(first.Y, second.Y);
		float right = MathF.Min(first.Right, second.Right);
		float bottom = MathF.Min(first.Bottom, second.Bottom);
		return new RenderRect(left, top, MathF.Max(0, right - left), MathF.Max(0, bottom - top));
	}

	private static bool ClipLine(RenderPoint start, RenderPoint end, RenderSize pageSize, out RenderPoint clippedStart, out RenderPoint clippedEnd)
	{
		float deltaX = end.X - start.X;
		float deltaY = end.Y - start.Y;
		float first = 0;
		float last = 1;
		if (!UpdateLineClip(-deltaX, start.X, ref first, ref last) ||
			!UpdateLineClip(deltaX, pageSize.Width - start.X, ref first, ref last) ||
			!UpdateLineClip(-deltaY, start.Y, ref first, ref last) ||
			!UpdateLineClip(deltaY, pageSize.Height - start.Y, ref first, ref last))
		{
			clippedStart = default;
			clippedEnd = default;
			return false;
		}

		clippedStart = new RenderPoint(start.X + first * deltaX, start.Y + first * deltaY);
		clippedEnd = new RenderPoint(start.X + last * deltaX, start.Y + last * deltaY);
		return true;
	}

	private static bool UpdateLineClip(float coefficient, float constant, ref float first, ref float last)
	{
		if (MathF.Abs(coefficient) < float.Epsilon)
		{
			return constant >= 0;
		}

		float ratio = constant / coefficient;
		if (coefficient < 0)
		{
			if (ratio > last)
			{
				return false;
			}
			first = MathF.Max(first, ratio);
		}
		else
		{
			if (ratio < first)
			{
				return false;
			}
			last = MathF.Min(last, ratio);
		}

		return true;
	}

	private static bool IsFinite(RenderRect rectangle) => float.IsFinite(rectangle.X) && float.IsFinite(rectangle.Y) && float.IsFinite(rectangle.Width) && float.IsFinite(rectangle.Height);

	private static bool IsFinite(RenderPoint point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

	private static XElement ExcelWorkbook(int pageCount) => new(Spreadsheet + "workbook", new XAttribute(XNamespace.Xmlns + "r", OfficeDocument), new XElement(Spreadsheet + "sheets", Enumerable.Range(1, pageCount).Select(i => new XElement(Spreadsheet + "sheet", new XAttribute("name", $"Page {i}"), new XAttribute("sheetId", i), new XAttribute(OfficeDocument + "id", $"rId{i}")))));

	private static XElement ExcelSheet(OpenXmlPage page, int pageNumber)
	{
		IReadOnlyList<(int Row, int Column, OpenXmlText Text)> cells = ExcelCells(page);
		var sheetData = new XElement(Spreadsheet + "sheetData", cells.GroupBy(cell => cell.Row).OrderBy(pair => pair.Key).Select(pair => new XElement(Spreadsheet + "row", new XAttribute("r", pair.Key), pair.Select(cell => ExcelCell(cell.Column, pair.Key, cell.Text)))));
		int maxRow = cells.Count == 0 ? 1 : cells.Max(cell => cell.Row + (cell.Text.TableCellBounds is not null ? cell.Text.RowSpan : 1) - 1);
		int maxColumn = cells.Count == 0 ? 1 : cells.Max(cell => cell.Column + (cell.Text.TableCellBounds is not null ? cell.Text.ColumnSpan : 1) - 1);
		var sheet = new XElement(Spreadsheet + "worksheet", new XAttribute(XNamespace.Xmlns + "r", OfficeDocument), new XElement(Spreadsheet + "dimension", new XAttribute("ref", $"A1:{ExcelColumn(maxColumn)}{maxRow}")), sheetData);
		IReadOnlyList<string> mergedRanges = ExcelMergedRanges(cells);
		if (mergedRanges.Count > 0)
		{
			sheet.Add(new XElement(Spreadsheet + "mergeCells", new XAttribute("count", mergedRanges.Count), mergedRanges.Select(reference => new XElement(Spreadsheet + "mergeCell", new XAttribute("ref", reference)))));
		}
		var hyperlinks = cells.Where(cell => cell.Text.Url is not null).Select((cell, index) => new XElement(Spreadsheet + "hyperlink", new XAttribute("ref", ExcelColumn(cell.Column) + cell.Row), new XAttribute(OfficeDocument + "id", $"rId{index + 1}"))).ToArray();
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

	private static IReadOnlyList<(int Row, int Column, OpenXmlText Text)> ExcelCells(OpenXmlPage page)
	{
		var rows = new Dictionary<int, List<(int Column, OpenXmlText Text)>>();
		foreach (OpenXmlText text in page.Texts)
		{
			RenderPoint cellOrigin = text.TableCellBounds is RenderRect bounds
				? new RenderPoint(bounds.X, bounds.Y)
				: text.Baseline;
			int row = Math.Max(1, (int)MathF.Floor(cellOrigin.Y / 20) + 1);
			int column = Math.Max(1, (int)MathF.Floor(cellOrigin.X / 64) + 1);
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

		return rows.OrderBy(pair => pair.Key)
			.SelectMany(pair => pair.Value.Select(cell => (Row: pair.Key, Column: cell.Column, Text: cell.Text)))
			.ToArray();
	}

	private static IReadOnlyList<string> ExcelMergedRanges(IReadOnlyList<(int Row, int Column, OpenXmlText Text)> cells)
	{
		return cells
			.Where(cell => cell.Text.TableCellBounds is not null && (cell.Text.ColumnSpan > 1 || cell.Text.RowSpan > 1))
			.Select(cell => $"{ExcelColumn(cell.Column)}{cell.Row}:{ExcelColumn(cell.Column + cell.Text.ColumnSpan - 1)}{cell.Row + cell.Text.RowSpan - 1}")
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	private static XElement ExcelCell(int column, int row, OpenXmlText text)
	{
		int styleIndex = ExcelStyleIndex(text.Direction);
		return new XElement(Spreadsheet + "c",
			new XAttribute("r", ExcelColumn(column) + row),
			new XAttribute("t", "inlineStr"),
			styleIndex > 0 ? new XAttribute("s", styleIndex) : null,
			new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "r",
				new XElement(Spreadsheet + "rPr",
					new XElement(Spreadsheet + "rFont", new XAttribute("val", text.Font.Family)),
					new XElement(Spreadsheet + "sz", new XAttribute("val", text.Font.Size)),
					text.Font.Bold ? new XElement(Spreadsheet + "b") : null,
					text.Font.Italic ? new XElement(Spreadsheet + "i") : null,
					new XElement(Spreadsheet + "color", new XAttribute("rgb", $"FF{text.Color.Red:X2}{text.Color.Green:X2}{text.Color.Blue:X2}"))),
				TextValue(Spreadsheet + "t", text.Text))));
	}

	private static int ExcelStyleIndex(TextDirection direction) => direction switch
	{
		TextDirection.TopToBottom => 1,
		TextDirection.BottomToTop => 2,
		TextDirection.RightToLeft => 3,
		_ => 0
	};

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
		IReadOnlyList<(int Row, int Column, OpenXmlText Text)> cells = ExcelCells(page);
		foreach ((OpenXmlText text, int index) in cells.Where(cell => cell.Text.Url is not null).Select((cell, index) => (cell.Text, index)))
		{
			relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{index + 1}"), new XAttribute("Type", HyperlinkRelationship), new XAttribute("Target", text.Url!), new XAttribute("TargetMode", "External")));
		}
		int hyperlinkCount = cells.Count(cell => cell.Text.Url is not null);
		if (page.Images.Count > 0)
		{
			relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{hyperlinkCount + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"), new XAttribute("Target", $"../drawings/drawing{pageNumber}.xml")));
		}
		else if (page.Charts.Count > 0 || page.Shapes.Count > 0)
		{
			relationships.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", $"rId{hyperlinkCount + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"), new XAttribute("Target", $"../drawings/drawing{pageNumber}.xml")));
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
		bool flipHorizontal = shape.IsLine && shape.Start is RenderPoint start && shape.End is RenderPoint end && start.X > end.X;
		bool flipVertical = shape.IsLine && shape.Start is RenderPoint verticalStart && shape.End is RenderPoint verticalEnd && verticalStart.Y > verticalEnd.Y;
		XElement shapeProperties = new(Drawing + "spPr",
			new XElement(Drawing + "xfrm", flipHorizontal ? new XAttribute("flipH", "1") : null, flipVertical ? new XAttribute("flipV", "1") : null, new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(Drawing + "ext", new XAttribute("cx", ToEmu(shape.Bounds.Width)), new XAttribute("cy", ToEmu(shape.Bounds.Height)))),
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
				new XElement(c + "plotArea", new XElement(c + "layout"), ChartElement(chart)),
				new XElement(c + "plotVisOnly", new XAttribute("val", 1)),
				new XElement(c + "dispBlanksAs", new XAttribute("val", "gap"))));
	}

	private static XElement ChartElement(OpenXmlChart chart) => chart.Type switch
	{
		RenderChartType.Bar => BarChart(chart),
		RenderChartType.Column => ColumnChart(chart),
		RenderChartType.Line => LineChart(chart),
		RenderChartType.Area => AreaChart(chart),
		RenderChartType.Pie => PieChart(chart),
		RenderChartType.Doughnut => DoughnutChart(chart),
		_ => throw new ArgumentOutOfRangeException(nameof(chart.Type), chart.Type, "Unknown chart type.")
	};

	private static XElement BarChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		return new XElement(c + "barChart", new XElement(c + "barDir", new XAttribute("val", "bar")), new XElement(c + "grouping", new XAttribute("val", "clustered")), new XElement(c + "varyColors", new XAttribute("val", 0)), ChartSeries(chart), new XElement(c + "axId", new XAttribute("val", 1)), new XElement(c + "axId", new XAttribute("val", 2)));
	}

	private static XElement ColumnChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		return new XElement(c + "barChart", new XElement(c + "barDir", new XAttribute("val", "col")), new XElement(c + "grouping", new XAttribute("val", "clustered")), new XElement(c + "varyColors", new XAttribute("val", 0)), ChartSeries(chart), new XElement(c + "axId", new XAttribute("val", 1)), new XElement(c + "axId", new XAttribute("val", 2)));
	}

	private static XElement LineChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		return new XElement(c + "lineChart", new XElement(c + "grouping", new XAttribute("val", "standard")), ChartSeries(chart, includeMarker: true), new XElement(c + "axId", new XAttribute("val", 1)), new XElement(c + "axId", new XAttribute("val", 2)));
	}

	private static XElement AreaChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		return new XElement(c + "areaChart", new XElement(c + "grouping", new XAttribute("val", "standard")), ChartSeries(chart), new XElement(c + "axId", new XAttribute("val", 1)), new XElement(c + "axId", new XAttribute("val", 2)));
	}

	private static XElement PieChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		return new XElement(c + "pieChart", new XElement(c + "varyColors", new XAttribute("val", 1)), ChartSeries(chart));
	}

	private static XElement DoughnutChart(OpenXmlChart chart)
	{
		XNamespace c = Chart;
		return new XElement(c + "doughnutChart", new XElement(c + "varyColors", new XAttribute("val", 1)), new XElement(c + "holeSize", new XAttribute("val", 50)), ChartSeries(chart));
	}

	private static XElement ChartSeries(OpenXmlChart chart, bool includeMarker = false)
	{
		XNamespace c = Chart;
		var categories = new XElement(c + "cat", new XElement(c + "strLit", new XElement(c + "ptCount", new XAttribute("val", chart.Bars.Count)), chart.Bars.Select((bar, index) => new XElement(c + "pt", new XAttribute("idx", index), new XElement(c + "v", bar.Label)))));
		var values = new XElement(c + "val", new XElement(c + "numLit", new XElement(c + "formatCode", "General"), new XElement(c + "ptCount", new XAttribute("val", chart.Bars.Count)), chart.Bars.Select((bar, index) => new XElement(c + "pt", new XAttribute("idx", index), new XElement(c + "v", bar.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))))));
		return new XElement(c + "ser",
			new XElement(c + "idx", new XAttribute("val", 0)),
			new XElement(c + "order", new XAttribute("val", 0)),
			new XElement(c + "tx", new XElement(c + "v", chart.Title)),
			categories,
			values,
			includeMarker ? new XElement(c + "marker", new XElement(c + "symbol", new XAttribute("val", "circle"))) : null);
	}

	private static XElement PictureElement(OpenXmlImage image, int id, string relationshipId)
	{
		return new XElement(SpreadsheetDrawing + "pic",
			new XElement(SpreadsheetDrawing + "nvPicPr", new XElement(SpreadsheetDrawing + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Image {id}")), new XElement(SpreadsheetDrawing + "cNvPicPr")),
			ImageBlipFill(image, relationshipId),
			new XElement(SpreadsheetDrawing + "spPr", new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst"))));
	}

	private static XElement ImageBlipFill(OpenXmlImage image, string relationshipId)
	{
		return new XElement(Drawing + "blipFill",
			new XElement(Drawing + "blip", new XAttribute(OfficeDocument + "embed", relationshipId)),
			image.Crop is OpenXmlImageCrop crop ? new XElement(Drawing + "srcRect", CropAttribute("l", crop.Left), CropAttribute("t", crop.Top), CropAttribute("r", crop.Right), CropAttribute("b", crop.Bottom)) : null,
			new XElement(Drawing + "stretch", new XElement(Drawing + "fillRect")));
	}

	private static XElement WordImage(OpenXmlImage image, string relationshipId, int id)
	{
		long cx = ToEmu(image.Destination.Width);
		long cy = ToEmu(image.Destination.Height);
		XElement graphic = new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData", new XAttribute("uri", Picture.NamespaceName), new XElement(Picture + "pic",
			new XElement(Picture + "nvPicPr", new XElement(Picture + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Image {id}")), new XElement(Picture + "cNvPicPr")),
			ImageBlipFill(image, relationshipId),
			new XElement(Picture + "spPr", new XElement(Drawing + "xfrm", new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(Drawing + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))), new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst"))))));
		return new XElement(Word + "r", new XElement(Word + "drawing", WordFloatingAnchor(image.Destination, id, $"Image {id}", graphic)));
	}

	private static XAttribute? CropAttribute(string name, int value) => value == 0 ? null : new XAttribute(name, value);

	private static XElement WordShape(OpenXmlShape shape, int id)
	{
		string style = $"position:absolute;left:{shape.Bounds.X:0.###}pt;top:{shape.Bounds.Y:0.###}pt;width:{shape.Bounds.Width:0.###}pt;height:{shape.Bounds.Height:0.###}pt";
		XElement element = new(Vml + (shape.IsLine ? "line" : "rect"),
			new XAttribute("style", style),
			new XAttribute("filled", shape.Fill is null ? "f" : "t"),
			shape.Fill is RenderColor fill ? new XAttribute("fillcolor", $"#{fill.Red:X2}{fill.Green:X2}{fill.Blue:X2}") : null,
			new XAttribute("stroked", shape.Stroke is null ? "f" : "t"),
			shape.Stroke is RenderColor stroke ? new XAttribute("strokecolor", $"#{stroke.Red:X2}{stroke.Green:X2}{stroke.Blue:X2}") : null,
			shape.Stroke is not null ? new XAttribute("strokeweight", $"{MathF.Max(0.5f, shape.StrokeWidth):0.###}pt") : null,
			shape.IsLine && shape.Start is RenderPoint start && shape.End is RenderPoint end
				? new XAttribute("from", VmlPoint(start, shape.Bounds))
				: null,
			shape.IsLine && shape.Start is not null && shape.End is RenderPoint lineEnd
				? new XAttribute("to", VmlPoint(lineEnd, shape.Bounds))
				: null);
		return new XElement(Word + "pict",
			new XAttribute(XNamespace.Xmlns + "v", Vml),
			element);
	}

	private static string VmlPoint(RenderPoint point, RenderRect bounds) => $"{point.X - bounds.X:0.###},{point.Y - bounds.Y:0.###}";

	private static XElement WordChart(OpenXmlChart chart, string relationshipId, int id)
	{
		long cx = ToEmu(chart.Destination.Width);
		long cy = ToEmu(chart.Destination.Height);
		XElement graphic = new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData", new XAttribute("uri", Chart.NamespaceName), new XElement(Chart + "chart", new XAttribute(OfficeDocument + "id", relationshipId))));
		return new XElement(Word + "r", new XElement(Word + "drawing", WordFloatingAnchor(chart.Destination, 200 + id, $"Chart {id}", graphic)));
	}

	private static XElement WordFloatingAnchor(RenderRect destination, int id, string name, XElement graphic)
	{
		long cx = ToEmu(destination.Width);
		long cy = ToEmu(destination.Height);
		return new XElement(WordDrawing + "anchor",
			new XAttribute("distT", 0),
			new XAttribute("distB", 0),
			new XAttribute("distL", 0),
			new XAttribute("distR", 0),
			new XAttribute("simplePos", 0),
			new XAttribute("relativeHeight", 0),
			new XAttribute("behindDoc", 0),
			new XAttribute("locked", 0),
			new XAttribute("layoutInCell", 1),
			new XAttribute("allowOverlap", 1),
			new XElement(WordDrawing + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
			new XElement(WordDrawing + "positionH", new XAttribute("relativeFrom", "page"), new XElement(WordDrawing + "posOffset", ToEmuOffset(destination.X))),
			new XElement(WordDrawing + "positionV", new XAttribute("relativeFrom", "page"), new XElement(WordDrawing + "posOffset", ToEmuOffset(destination.Y))),
			new XElement(WordDrawing + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
			new XElement(WordDrawing + "effectExtent", new XAttribute("l", 0), new XAttribute("t", 0), new XAttribute("r", 0), new XAttribute("b", 0)),
			new XElement(WordDrawing + "wrapNone"),
			new XElement(WordDrawing + "docPr", new XAttribute("id", id), new XAttribute("name", name)),
			new XElement(WordDrawing + "cNvGraphicFramePr", new XElement(Drawing + "graphicFrameLocks", new XAttribute("noChangeAspect", 1))),
			graphic);
	}

	private static XElement WordSectionProperties(RenderSize size, bool nextPage)
	{
		return new XElement(Word + "sectPr",
			nextPage ? new XElement(Word + "type", new XAttribute(Word + "val", "nextPage")) : null,
			new XElement(Word + "pgSz", new XAttribute(Word + "w", ToTwips(size.Width)), new XAttribute(Word + "h", ToTwips(size.Height))));
	}

	private static long ToEmu(float points) => Math.Max(1, (long)Math.Round(points * 12700, MidpointRounding.AwayFromZero));

	private static long ToEmuOffset(float points) => Math.Max(0, (long)Math.Round(points * 12700, MidpointRounding.AwayFromZero));

	private static long ToTwips(float points) => Math.Max(0, (long)Math.Round(points * 20, MidpointRounding.AwayFromZero));

	private static long ToHalfPoints(float points) => Math.Max(1, (long)Math.Round(points * 2, MidpointRounding.AwayFromZero));

	private static XElement? WordParagraphProperties(OpenXmlText text)
	{
		var properties = new List<object>();
		if (text.Baseline.X > 0)
		{
			properties.Add(new XElement(Word + "ind", new XAttribute(Word + "left", ToTwips(text.Baseline.X))));
		}

		XElement? direction = text.Direction switch
		{
			TextDirection.RightToLeft => new XElement(Word + "bidi"),
			TextDirection.TopToBottom => new XElement(Word + "textDirection", new XAttribute(Word + "val", "tbRl")),
			TextDirection.BottomToTop => new XElement(Word + "textDirection", new XAttribute(Word + "val", "btLr")),
			_ => null
		};
		if (direction is not null)
		{
			properties.Add(direction);
		}

		return properties.Count == 0 ? null : new XElement(Word + "pPr", properties);
	}

	private static XElement WordRun(OpenXmlText text)
	{
		var content = new List<object>
		{
			new XElement(Word + "rPr",
				new XElement(Word + "rFonts", new XAttribute(Word + "ascii", text.Font.Family), new XAttribute(Word + "hAnsi", text.Font.Family), new XAttribute(Word + "eastAsia", text.Font.Family), new XAttribute(Word + "cs", text.Font.Family)),
				new XElement(Word + "sz", new XAttribute(Word + "val", ToHalfPoints(text.Font.Size))),
				text.Font.Bold ? new XElement(Word + "b") : null,
				text.Font.Italic ? new XElement(Word + "i") : null,
				new XElement(Word + "color", new XAttribute(Word + "val", $"{text.Color.Red:X2}{text.Color.Green:X2}{text.Color.Blue:X2}")))
		};
		content.AddRange(WordTextNodes(text.Text));
		return new XElement(Word + "r", content);
	}

	private static IEnumerable<XElement> WordTextNodes(string value)
	{
		string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			if (index > 0)
			{
				yield return new XElement(Word + "br");
			}

			yield return TextValue(Word + "t", lines[index]);
		}
	}

	private static XElement TextValue(XName name, string value) => new(name, NeedsPreservedWhitespace(value) ? new XAttribute(XNamespace.Xml + "space", "preserve") : null, value);

	private static bool NeedsPreservedWhitespace(string value) => value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));

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

	private static XElement ExcelContentTypes(IReadOnlyList<OpenXmlPage> pages)
	{
		var entries = new List<object>
		{
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
			new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
			new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"))
		};

		for (int index = 0; index < pages.Count; index++)
		{
			OpenXmlPage page = pages[index];
			entries.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{index + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
			if (page.Images.Count > 0)
			{
				entries.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/xl/drawings/drawing{index + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
			}
		}

		return new XElement(ContentTypes + "Types", entries);
	}

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
