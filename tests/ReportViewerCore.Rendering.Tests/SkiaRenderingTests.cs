using FluentAssertions;
using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ReportViewerCore.Engine;
using ReportViewerCore.Headless;
using ReportViewerCore.Rendering;
using ReportViewerCore.Rendering.Html;
using ReportViewerCore.Rendering.OpenXml;
using ReportViewerCore.Rendering.Skia;
using Xunit;

namespace ReportViewerCore.Rendering.Tests;

public sealed class SkiaRenderingTests
{
	[Fact]
	public void BitmapRenderer_can_render_without_system_drawing()
	{
		using var renderer = new SkiaBitmapRenderer();

		RenderImage image = renderer.Render(new RenderSize(160, 96), canvas =>
		{
			canvas.Clear(RenderColor.White);
			canvas.FillRectangle(new RenderRect(12, 12, 80, 32), new RenderColor(20, 100, 220));
			canvas.DrawLine(new RenderPoint(0, 0), new RenderPoint(159, 95), RenderColor.Black, 2);
		});

		image.Width.Should().Be(160);
		image.Height.Should().Be(96);
		image.PngData.Length.Should().BeGreaterThan(100);
		image.PngData.Span[..8].ToArray().Should().Equal(137, 80, 78, 71, 13, 10, 26, 10);
	}

	[Fact]
	public void Bitmap_renderer_rejects_non_finite_dimensions()
	{
		using var renderer = new SkiaBitmapRenderer();

		Action render = () => renderer.Render(new RenderSize(float.NaN, 96), _ => { });

		render.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void Text_shaper_returns_glyphs_and_metrics()
	{
		using var fonts = new SkiaFontResolver();
		var request = new FontRequest("Arial", 16);
		var shaper = new SkiaTextShaper(fonts);

		ShapedText shaped = shaper.Shape("Report", request);
		FontMetrics metrics = fonts.GetMetrics(request);

		shaped.Glyphs.Should().NotBeEmpty();
		shaped.AdvanceX.Should().BeGreaterThan(0);
		metrics.LineHeight.Should().BeGreaterThan(0);
	}

	[Fact]
	public void Platform_font_fallback_assigns_per_glyph_horizontal_advances()
	{
		using var fonts = new SkiaFontResolver();
		var shaper = new SkiaTextShaper(fonts);

		ShapedText shaped = shaper.Shape("Report", new FontRequest("Arial", 16));

		shaped.Glyphs.Should().Contain(glyph => glyph.AdvanceX > 0);
		shaped.Glyphs.Sum(glyph => glyph.AdvanceX).Should().BeApproximately(shaped.AdvanceX, 0.01f);
	}

	[Fact]
	public void Skia_bitmap_renderer_accepts_multiline_text_without_control_glyphs()
	{
		using var renderer = new SkiaBitmapRenderer();

		Action render = () => renderer.Render(new RenderSize(160, 80), canvas => canvas.DrawText("Line 1\nLine 2", new RenderPoint(8, 24), new FontRequest("Arial", 14), RenderColor.Black));

		render.Should().NotThrow();
	}

	[Fact]
	public void Skia_font_resolver_requires_existing_registered_font_files()
	{
		using var fonts = new SkiaFontResolver();
		string missingFont = Path.Combine(Path.GetTempPath(), $"reportviewercore-missing-{Guid.NewGuid():N}.ttf");

		Action register = () => fonts.RegisterFont("MissingFont", missingFont);
		register.Should().Throw<FileNotFoundException>();
	}

	[Fact]
	public void Skia_font_resolver_fails_closed_for_unknown_font_families()
	{
		using var fonts = new SkiaFontResolver();
		string family = $"ReportViewerCore-Missing-{Guid.NewGuid():N}";

		Action resolve = () => fonts.GetMetrics(new FontRequest(family, 12));

		resolve.Should().Throw<FontNotFoundException>();
	}

	[Fact]
	public void Skia_font_resolver_rejects_non_finite_font_sizes()
	{
		using var fonts = new SkiaFontResolver();

		Action resolve = () => fonts.GetMetrics(new FontRequest("Arial", float.NaN));

		resolve.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void Text_shaper_covers_thai_arabic_cjk_and_rtl_direction()
	{
		using var fonts = new SkiaFontResolver();
		var shaper = new SkiaTextShaper(fonts);
		var samples = new[]
		{
			("ภาษาไทย", TextDirection.LeftToRight),
			("العربية", TextDirection.RightToLeft),
			("日本語", TextDirection.LeftToRight)
		};

		foreach ((string text, TextDirection direction) in samples)
		{
			ShapedText shaped = shaper.Shape(text, new FontRequest("Arial", 16), direction);
			shaped.Glyphs.Should().NotBeEmpty();
			shaped.AdvanceX.Should().BeGreaterThan(0);
		}
	}

	[Fact]
	public void Text_shaper_supports_vertical_directions()
	{
		using var fonts = new SkiaFontResolver();
		var shaper = new SkiaTextShaper(fonts);

		ShapedText topToBottom = shaper.Shape("縦書き", new FontRequest("Arial", 16), TextDirection.TopToBottom);
		ShapedText bottomToTop = shaper.Shape("縦書き", new FontRequest("Arial", 16), TextDirection.BottomToTop);

		topToBottom.Glyphs.Should().NotBeEmpty();
		topToBottom.AdvanceX.Should().Be(0);
		topToBottom.AdvanceY.Should().BeNegative();
		bottomToTop.AdvanceX.Should().Be(0);
		bottomToTop.AdvanceY.Should().BePositive();
	}

	[Fact]
	public void Pdf_document_writes_a_completed_document()
	{
		using var output = new MemoryStream();
		using (var document = new SkiaPdfDocument(output))
		{
			IRenderCanvas canvas = document.BeginPage(new RenderSize(240, 120));
			canvas.Clear(RenderColor.White);
			canvas.DrawText("Cross-platform report", new RenderPoint(20, 56), new FontRequest("Arial", 18), RenderColor.Black);
			document.EndPage();
			document.Complete();
		}

		output.ToArray().AsSpan(0, 5).ToArray().Should().Equal("%PDF-"u8.ToArray());
		output.Length.Should().BeGreaterThan(500);
	}

	[Fact]
	public void Pdf_document_rejects_non_finite_page_dimensions()
	{
		using var output = new MemoryStream();
		using var document = new SkiaPdfDocument(output);

		Action beginPage = () => document.BeginPage(new RenderSize(float.PositiveInfinity, 120));

		beginPage.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void Pdf_renderer_writes_hyperlink_annotations()
	{
		using var renderer = new SkiaPdfRenderer();
		ReportOutput output = renderer.Render(new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(240, 120), canvas => canvas.DrawHyperlink("Docs", new RenderPoint(20, 56), new FontRequest("Arial", 18), RenderColor.Black, "https://example.com/docs"))
		}), new ReportRenderOptions(ReportOutputFormat.Pdf));

		string pdf = System.Text.Encoding.Latin1.GetString(output.Data.Span);
		pdf.Should().Contain("/URI").And.Contain("example.com/docs");
	}

	[Fact]
	public void Headless_pipeline_routes_a_report_document_to_the_requested_renderer()
	{
		using var renderer = new SkiaPdfRenderer();
		var pipeline = new HeadlessReportRenderer(new[] { renderer });
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(240, 120), canvas =>
			{
				canvas.Clear(RenderColor.White);
				canvas.DrawText("Headless", new RenderPoint(20, 56), new FontRequest("Arial", 18), RenderColor.Black);
			})
		});

		ReportOutput output = pipeline.Render(report, new ReportRenderOptions(ReportOutputFormat.Pdf));

		output.Format.Should().Be(ReportOutputFormat.Pdf);
		output.MimeType.Should().Be("application/pdf");
		output.FileExtension.Should().Be("pdf");
		output.Data.Span[..5].ToArray().Should().Equal("%PDF-"u8.ToArray());
	}

	[Fact]
	public void Html_renderer_writes_semantic_multi_page_markup_and_escapes_text()
	{
		var renderer = new HtmlReportRenderer();
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas =>
			{
				canvas.DrawText("<Report>", new RenderPoint(10, 24), new FontRequest("Arial", 14), RenderColor.Black);
				canvas.DrawText("縦書き", new RenderPoint(80, 24), new FontRequest("Arial", 14), RenderColor.Black, TextDirection.TopToBottom);
				canvas.DrawHyperlink("Docs", new RenderPoint(10, 48), new FontRequest("Arial", 12), RenderColor.Black, "https://example.com/docs");
				canvas.DrawImage(new RenderImage(1, 1, "png"u8.ToArray()), new RenderRect(10, 60, 20, 20));
			}),
			new ReportPage(new RenderSize(200, 100), canvas => canvas.DrawText("Page 2", new RenderPoint(10, 24), new FontRequest("Arial", 14), RenderColor.Black))
		});

		ReportOutput output = renderer.Render(report, new ReportRenderOptions(ReportOutputFormat.Html));
		string html = System.Text.Encoding.UTF8.GetString(output.Data.Span);

		html.Should().Contain("<section class=\"report-page\"").And.Contain("<text").And.Contain("&lt;Report&gt;").And.Contain("writing-mode=\"tb\"");
		html.Should().Contain("href=\"https://example.com/docs\"").And.Contain("data:image/png;base64,cG5n");
		html.Split("<section class=\"report-page\"").Length.Should().Be(3);
		int svgStart = html.IndexOf("<svg", StringComparison.Ordinal);
		int svgEnd = html.IndexOf("</svg>", svgStart, StringComparison.Ordinal);
		XDocument.Parse(html.Substring(svgStart, svgEnd + "</svg>".Length - svgStart)).Root.Should().NotBeNull();
	}

	[Fact]
	public void Html_renderer_rejects_javascript_links()
	{
		var renderer = new HtmlReportRenderer();
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(100, 50), canvas => canvas.DrawHyperlink("Unsafe", new RenderPoint(0, 20), new FontRequest("Arial", 10), RenderColor.Black, "javascript:alert(1)"))
		});

		Action act = () => renderer.Render(report, new ReportRenderOptions(ReportOutputFormat.Html));

		act.Should().Throw<ArgumentException>().WithMessage("*Only http, https, mailto, and relative URLs are supported.*");
	}

	[Theory]
	[InlineData("javascript:alert(1)")]
	[InlineData("file:///tmp/report")]
	[InlineData("http://[invalid")]
	public void Shared_render_url_policy_rejects_unsafe_or_malformed_links(string url)
	{
		Action act = () => RenderUrlPolicy.ValidateHyperlink(url);

		act.Should().Throw<ArgumentException>().WithMessage("*Only http, https, mailto, and relative URLs are supported.*");
	}

	[Theory]
	[InlineData("https://example.com/report")]
	[InlineData("mailto:reports@example.com")]
	[InlineData("/reports/detail")]
	[InlineData("detail.rdlc")]
	public void Shared_render_url_policy_accepts_supported_links(string url)
	{
		Action act = () => RenderUrlPolicy.ValidateHyperlink(url);

		act.Should().NotThrow();
	}

	[Fact]
	public void Excel_openxml_renderer_writes_workbook_and_hyperlink_parts()
	{
		var renderer = new ExcelOpenXmlRenderer();
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas =>
			{
				canvas.DrawText("Excel text", new RenderPoint(10, 20), new FontRequest("Arial", 12), RenderColor.Black);
				canvas.DrawText("縦", new RenderPoint(80, 20), new FontRequest("Arial", 12), RenderColor.Black, TextDirection.TopToBottom);
				canvas.DrawHyperlink("Docs", new RenderPoint(10, 40), new FontRequest("Arial", 12), RenderColor.Black, "https://example.com");
				canvas.DrawHyperlink("Mail", new RenderPoint(10, 60), new FontRequest("Arial", 12), RenderColor.Black, "mailto:reports@example.com");
			})
		});

		ReportOutput output = renderer.Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);

		archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
		archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
		archive.GetEntry("xl/styles.xml").Should().NotBeNull();
		archive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
		archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels").Should().NotBeNull();
		using var sheetReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		string sheet = sheetReader.ReadToEnd();
		sheet.Should().Contain("Excel text").And.Contain("s=\"1\"").And.Contain("dimension ref=\"A1:B4\"").And.Contain("hyperlinks").And.Contain("ref=\"A3\"").And.Contain("ref=\"A4\"");
		using var relationshipReader = new StreamReader(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!.Open());
		relationshipReader.ReadToEnd().Should().Contain("https://example.com").And.Contain("mailto:reports@example.com");
	}

	[Fact]
	public void Openxml_renderers_reject_unsafe_hyperlinks()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(100, 50), canvas => canvas.DrawHyperlink("Unsafe", new RenderPoint(0, 20), new FontRequest("Arial", 10), RenderColor.Black, "javascript:alert(1)"))
		});

		Action excelRender = () => new ExcelOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		Action wordRender = () => new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		excelRender.Should().Throw<ArgumentException>().WithMessage("*Only http, https, mailto, and relative URLs are supported.*");
		wordRender.Should().Throw<ArgumentException>().WithMessage("*Only http, https, mailto, and relative URLs are supported.*");

		var networkPathReport = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(100, 50), canvas => canvas.DrawHyperlink("Unsafe", new RenderPoint(0, 20), new FontRequest("Arial", 10), RenderColor.Black, "//external.example/report"))
		});
		Action networkPathRender = () => new HtmlReportRenderer().Render(networkPathReport, new ReportRenderOptions(ReportOutputFormat.Html));
		networkPathRender.Should().Throw<ArgumentException>().WithMessage("*Only http, https, mailto, and relative URLs are supported.*");
	}

	[Fact]
	public void Openxml_renderers_preserve_text_color()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(240, 120), canvas => canvas.DrawText("Colored", new RenderPoint(10, 20), new FontRequest("Arial", 12), new RenderColor(18, 52, 86)))
		});

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var excelArchive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var sheetReader = new StreamReader(excelArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		sheetReader.ReadToEnd().Should().Contain("FF123456").And.Contain("rFont").And.Contain("val=\"12\"");

		ReportOutput word = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var documentReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		documentReader.ReadToEnd().Should().Contain("w:val=\"123456\"").And.Contain("w:left=\"200\"").And.Contain("w:ascii=\"Arial\"").And.Contain("w:val=\"24\"");
	}

	[Fact]
	public void Openxml_renderers_preserve_leading_and_trailing_text_spaces()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(240, 120), canvas => canvas.DrawText("  padded  ", new RenderPoint(10, 20), new FontRequest("Arial", 12), RenderColor.Black))
		});

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var excelArchive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var sheetReader = new StreamReader(excelArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		sheetReader.ReadToEnd().Should().Contain("xml:space=\"preserve\"");

		ReportOutput word = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var documentReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		documentReader.ReadToEnd().Should().Contain("xml:space=\"preserve\"");
	}

	[Fact]
	public void Rdlc_engine_propagates_tablix_colspan_to_excel_merged_cells()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "merged-cell-table.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 12, Category = "North" } }
		}));

		ReportOutput output = new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
		using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		string sheet = reader.ReadToEnd();

		sheet.Should().Contain("Group header").And.Contain("Alpha").And.Contain("<mergeCells count=\"1\"><mergeCell ref=\"A1:B1\"");
	}

	[Fact]
	public void Openxml_renderer_emits_rowspan_merge_ranges_and_dimensions()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(128, 64), canvas => canvas.DrawTableCell("Row span", new RenderPoint(4, 16), new RenderRect(0, 0, 64, 40), new FontRequest("Arial", 10), RenderColor.Black, columnSpan: 1, rowSpan: 2))
		});

		ReportOutput output = new ExcelOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
		using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		string sheet = reader.ReadToEnd();

		sheet.Should().Contain("<dimension ref=\"A1:A2\"").And.Contain("<mergeCells count=\"1\"><mergeCell ref=\"A1:A2\"");
	}

	[Fact]
	public void Openxml_renderers_embed_png_images()
	{
		using var bitmap = new SkiaBitmapRenderer();
		RenderImage image = bitmap.Render(new RenderSize(12, 12), canvas => canvas.FillRectangle(new RenderRect(0, 0, 12, 12), new RenderColor(220, 40, 40)));
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas => canvas.DrawImage(image, new RenderRect(10, 10, 24, 24)))
		});

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using (var archive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read))
		{
			archive.GetEntry("xl/media/image1_1.png").Should().NotBeNull();
			archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
			archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels").Should().NotBeNull();
			using var drawingReader = new StreamReader(archive.GetEntry("xl/drawings/drawing1.xml")!.Open());
			drawingReader.ReadToEnd().Should().Contain("<xdr:pic").And.Contain("<xdr:blipFill").And.Contain("<xdr:spPr");
		}

		ReportOutput word = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		wordArchive.GetEntry("word/media/image1.png").Should().NotBeNull();
		wordArchive.GetEntry("word/document.xml").Should().NotBeNull();
		wordArchive.GetEntry("word/_rels/document.xml.rels").Should().NotBeNull();
		using var imageDocumentReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		imageDocumentReader.ReadToEnd().Should().Contain("type=\"#_x0000_t75\"").And.Contain("style=\"position:absolute;left:10pt;top:10pt;width:24pt;height:24pt").And.Contain("imagedata").And.Contain("r:id=\"rId1\"");
	}

	[Fact]
	public void Openxml_renderers_embed_matching_page_previews_for_visible_layout()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(160, 90), canvas =>
			{
				canvas.Clear(RenderColor.White);
				canvas.FillRectangle(new RenderRect(0, 0, 160, 24), new RenderColor(24, 47, 78));
				canvas.DrawText("Visual preview", new RenderPoint(12, 18), new FontRequest("Arial", 12, Bold: true), RenderColor.White);
			})
		});
		using var bitmap = new SkiaBitmapRenderer();
		byte[] expected = bitmap.Render(report.Pages[0].Size, report.Pages[0].Render).PngData.ToArray();

		foreach ((IReportRenderer renderer, ReportOutputFormat format, string previewPath) in new[]
		{
			((IReportRenderer)new ExcelOpenXmlRenderer(), ReportOutputFormat.ExcelOpenXml, "xl/media/preview1.png"),
			((IReportRenderer)new WordOpenXmlRenderer(), ReportOutputFormat.WordOpenXml, "word/media/preview1.png")
		})
		{
			ReportOutput output = renderer.Render(report, new ReportRenderOptions(format));
			using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
			using Stream stream = archive.GetEntry(previewPath)!.Open();
			using var preview = new MemoryStream();
			stream.CopyTo(preview);
			preview.ToArray().Should().Equal(expected);
		}
	}

	[Fact]
	public void Openxml_renderers_clip_images_to_page_bounds()
	{
		using var bitmap = new SkiaBitmapRenderer();
		RenderImage image = bitmap.Render(new RenderSize(12, 12), canvas => canvas.FillRectangle(new RenderRect(0, 0, 12, 12), new RenderColor(220, 40, 40)));
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(20, 10), canvas => canvas.DrawImage(image, new RenderRect(-10, -5, 30, 20)))
		});

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using (var archive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read))
		using (var drawingReader = new StreamReader(archive.GetEntry("xl/drawings/drawing1.xml")!.Open()))
		{
			string drawing = drawingReader.ReadToEnd();
			drawing.Should().Contain("<xdr:ext cx=\"254000\" cy=\"127000\"").And.Contain("<a:srcRect l=\"33333\" t=\"25000\"");
		}

		ReportOutput word = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var documentReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		documentReader.ReadToEnd().Should().Contain("style=\"position:absolute;left:0pt;top:0pt;width:20pt;height:10pt").And.Contain("cropleft=\"0.33333\"").And.Contain("croptop=\"0.25\"");
	}

	[Fact]
	public void Openxml_renderers_clip_vector_shapes_to_page_bounds()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(20, 10), canvas =>
			{
				canvas.FillRectangle(new RenderRect(-5, -4, 15, 10), RenderColor.Black);
				canvas.DrawLine(new RenderPoint(-5, -5), new RenderPoint(25, 15), RenderColor.Black, 1);
			})
		});

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using (var archive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read))
		using (var drawingReader = new StreamReader(archive.GetEntry("xl/drawings/drawing1.xml")!.Open()))
		{
			string drawing = drawingReader.ReadToEnd();
			drawing.Should().Contain("<xdr:ext cx=\"127000\" cy=\"76200\"").And.Contain("<xdr:ext cx=\"190500\" cy=\"127000\"");
		}

		ReportOutput word = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var documentReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		documentReader.ReadToEnd().Should().Contain("left:0pt;top:0pt;width:10pt;height:6pt").And.Contain("from=\"0,0\" to=\"15,10\"");
	}

	[Fact]
	public void Word_openxml_renderer_writes_document_and_external_relationship()
	{
		var renderer = new WordOpenXmlRenderer();
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas => canvas.DrawHyperlink("Docs", new RenderPoint(10, 20), new FontRequest("Arial", 12), RenderColor.Black, "https://example.com"))
		});

		ReportOutput output = renderer.Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);

		archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
		archive.GetEntry("word/document.xml").Should().NotBeNull();
		archive.GetEntry("word/_rels/document.xml.rels").Should().NotBeNull();
		using var documentReader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
		documentReader.ReadToEnd().Should().Contain("w:hyperlink").And.Contain("w:w=\"4000\"").And.Contain("w:h=\"2000\"");
	}

	[Fact]
	public void Word_openxml_renderer_does_not_append_a_blank_page_break()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas => canvas.DrawText("One page", new RenderPoint(10, 20), new FontRequest("Arial", 12), RenderColor.Black))
		});

		ReportOutput output = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
		using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
		string xml = reader.ReadToEnd();

		xml.Should().NotContain("w:type=\"page\"");
	}

	[Fact]
	public void Word_openxml_renderer_preserves_vertical_text_direction()
	{
		var renderer = new WordOpenXmlRenderer();
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas => canvas.DrawText("縦書き", new RenderPoint(10, 20), new FontRequest("Arial", 12), RenderColor.Black, TextDirection.TopToBottom))
		});

		ReportOutput output = renderer.Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
		using var documentReader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());

		documentReader.ReadToEnd().Should().Contain("w:textDirection").And.Contain("tbRl");
	}

	[Fact]
	public void Skia_pdf_renderer_emits_annotations_for_vertical_hyperlinks()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas => canvas.DrawHyperlink("縦リンク", new RenderPoint(20, 60), new FontRequest("Arial", 12), RenderColor.Black, "https://example.com/vertical", TextDirection.TopToBottom))
		});

		string pdf = System.Text.Encoding.Latin1.GetString(new SkiaPdfRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.Pdf)).Data.Span);

		pdf.Should().Contain("/URI").And.Contain("example.com/vertical").And.Contain("/Rect");
	}

	[Fact]
	public void Openxml_renderer_maps_rectangles_and_lines_to_native_shape_parts()
	{
		var renderer = new ExcelOpenXmlRenderer();
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(100, 50), canvas =>
			{
				canvas.FillRectangle(new RenderRect(0, 0, 10, 10), RenderColor.Black);
				canvas.DrawLine(new RenderPoint(0, 0), new RenderPoint(20, 10), RenderColor.Black, 1);
				canvas.DrawLine(new RenderPoint(20, 10), new RenderPoint(0, 0), RenderColor.Black, 1);
			})
		});

		ReportOutput excel = renderer.Render(report, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var excelArchive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var drawing = new StreamReader(excelArchive.GetEntry("xl/drawings/drawing1.xml")!.Open());
		drawing.ReadToEnd().Should().Contain("sp").And.Contain("prstGeom").And.Contain("flipH=\"1\"").And.Contain("flipV=\"1\"");

		ReportOutput word = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var document = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		document.ReadToEnd().Should().Contain("urn:schemas-microsoft-com:vml").And.Contain("rect").And.Contain("line").And.Contain("from=\"20,10\"").And.Contain("to=\"0,0\"");
	}

	[Fact]
	public void Openxml_word_renderer_maps_multiline_fixture_text_to_break_nodes()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "multiline.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition);
		ReportOutput output = new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
		using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
		string xml = reader.ReadToEnd();

		xml.Should().Contain("Line 1").And.Contain("w:br").And.Contain("Line 2");
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		html.Should().Contain("<tspan").And.Contain("Line 1").And.Contain("Line 2");
	}

	[Fact]
	public void Report_page_source_adapter_bridges_paginated_pages_into_the_shared_document()
	{
		ReportDocument document = ReportPageSourceAdapter.Adapt(new FixturePageSource());
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().HaveCount(2);
		html.Should().Contain("Legacy page 1").And.Contain("Legacy page 2");
	}

	[Fact]
	public void Report_page_source_adapter_rejects_empty_sources()
	{
		Action adapt = () => ReportPageSourceAdapter.Adapt(new EmptyPageSource());

		adapt.Should().Throw<InvalidDataException>().WithMessage("*at least one page*");
	}

	[Fact]
	public void Report_page_source_adapter_rejects_invalid_page_sizes()
	{
		Action adapt = () => ReportPageSourceAdapter.Adapt(new InvalidSizePageSource());

		adapt.Should().Throw<InvalidDataException>().WithMessage("*invalid size for page 0*");
	}

	[Fact]
	public void Report_page_source_adapter_rejects_non_finite_page_sizes()
	{
		Action adapt = () => ReportPageSourceAdapter.Adapt(new NonFinitePageSource());

		adapt.Should().Throw<InvalidDataException>().WithMessage("*invalid size for page 0*");
	}

	[Theory]
	[InlineData(0, 100)]
	[InlineData(100, 0)]
	[InlineData(-1, 100)]
	[InlineData(100, -1)]
	public void Report_document_rejects_non_positive_page_sizes(float width, float height)
	{
		Action create = () => new ReportDocument(new[] { new ReportPage(new RenderSize(width, height), _ => { }) });

		create.Should().Throw<ArgumentException>().WithMessage("*invalid dimensions*");
	}

	[Fact]
	public void Report_document_rejects_null_page_delegates()
	{
		Action create = () => new ReportDocument(new[] { new ReportPage(new RenderSize(100, 50), null!) });

		create.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void Rdlc_engine_binds_detail_rows_into_backend_neutral_document()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "simple.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		var dataSets = new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Name = "Alpha", Amount = 10 },
				new { Name = "Beta", Amount = 20 }
			}
		};

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(dataSets));
		ReportOutput output = new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html));
		string html = System.Text.Encoding.UTF8.GetString(output.Data.Span);

		document.Pages.Should().ContainSingle();
		document.Pages[0].Size.Width.Should().BeApproximately(595.28f, 0.1f);
		html.Should().Contain("Name").And.Contain("Amount").And.Contain("Alpha").And.Contain("Beta").And.Contain("10").And.Contain("20");
	}

	[Fact]
	public void Rdlc_engine_resolves_dataset_names_case_insensitively()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "simple.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["items"] = new[] { new { Name = "Case-insensitive", Amount = 7 } }
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Case-insensitive").And.Contain("7");
	}

	[Fact]
	public void Rdlc_engine_combines_multiple_tablixes_using_their_offsets()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "multi-tablix.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Sale A", Amount = 10 } },
			["Returns"] = new[] { new { Name = "Return B", Amount = 3 } }
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().ContainSingle();
		html.Should().Contain("Sales").And.Contain("Returns").And.Contain("Sale A").And.Contain("Return B").And.Contain("Mixed body item");
	}

	[Fact]
	public void Rdlc_engine_resolves_a_subreport_through_the_explicit_provider()
	{
		string parentPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "subreport-parent.rdlc");
		string childPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "simple.rdlc");
		using FileStream parent = File.OpenRead(parentPath);
		var resolver = new FixtureSubreportResolver(File.ReadAllBytes(childPath));
		ReportDocument document = new RdlcReportEngine().CreateDocument(parent, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Nested", Amount = 7 } }
		}, SubreportResolver: resolver));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().ContainSingle();
		html.Should().Contain("Nested").And.Contain("Amount");
		resolver.OpenedNames.Should().ContainSingle().Which.Should().Be("Child");
	}

	[Fact]
	public void Rdlc_engine_maps_parameters_into_a_subreport()
	{
		string parentPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "subreport-parent.rdlc");
		string childPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "subreport-parameter-child.rdlc");
		using FileStream parent = File.OpenRead(parentPath);
		var resolver = new FixtureSubreportResolver(File.ReadAllBytes(childPath));
		ReportDocument document = new RdlcReportEngine().CreateDocument(parent, new RdlcDataContext(
			Parameters: new Dictionary<string, object?> { ["ParentTitle"] = "Mapped title" },
			SubreportResolver: resolver));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Mapped title");
		resolver.OpenedNames.Should().ContainSingle().Which.Should().Be("Child");
	}

	[Fact]
	public void Rdlc_engine_splits_detail_rows_across_pages()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "simple.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		var rows = Enumerable.Range(1, 80)
			.Select(index => new { Name = $"Row {index}", Amount = index })
			.ToArray();

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = rows
		}));

		document.Pages.Count.Should().BeGreaterThan(1);
		document.Pages.Should().OnlyContain(page => Math.Abs(page.Size.Height - 841.89f) < 0.1f);

		ReportOutput word = new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var wordReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		wordReader.ReadToEnd().Should().Contain("w:val=\"nextPage\"").And.NotContain("w:type=\"page\"").And.Contain("Row 80");

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var excelArchive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var workbookReader = new StreamReader(excelArchive.GetEntry("xl/workbook.xml")!.Open());
		workbookReader.ReadToEnd().Should().Contain("Page 2");
	}

	[Fact]
	public void Word_openxml_renderer_preserves_each_page_section_size()
	{
		var report = new ReportDocument(new[]
		{
			new ReportPage(new RenderSize(200, 100), canvas => canvas.DrawText("First", new RenderPoint(4, 16), new FontRequest("Arial", 10), RenderColor.Black)),
			new ReportPage(new RenderSize(300, 150), canvas => canvas.DrawText("Second", new RenderPoint(4, 16), new FontRequest("Arial", 10), RenderColor.Black))
		});

		ReportOutput output = new WordOpenXmlRenderer().Render(report, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
		using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
		string xml = reader.ReadToEnd();

		xml.Split("<w:pgSz", StringSplitOptions.None).Length.Should().Be(3);
		xml.Should().Contain("w:val=\"nextPage\"").And.Contain("w:w=\"4000\" w:h=\"2000\"").And.Contain("w:w=\"6000\" w:h=\"3000\"");
	}

	[Fact]
	public void Rdlc_engine_repeats_page_header_and_footer_on_each_page()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "header-footer.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition);
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().HaveCount(2);
		html.Split("Page header").Length.Should().Be(3);
		html.Split("Page footer").Length.Should().Be(3);
		html.Should().Contain("First page body").And.Contain("Second page body");
	}

	[Fact]
	public void Rdlc_engine_repeats_page_header_and_footer_for_tablix_pages()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "header-footer-tablix.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = Enumerable.Range(1, 80).Select(index => new { Name = $"Row {index}", Amount = index })
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Count.Should().BeGreaterThan(1);
		html.Split("Tablix header").Length.Should().Be(document.Pages.Count + 1);
		html.Split("Tablix footer").Length.Should().Be(document.Pages.Count + 1);
	}

	[Fact]
	public void Rdlc_engine_composes_a_subreport_alongside_a_tablix()
	{
		string parentPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "mixed-tablix-subreport.rdlc");
		string childPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "simple.rdlc");
		using FileStream parent = File.OpenRead(parentPath);
		var resolver = new FixtureSubreportResolver(File.ReadAllBytes(childPath));
		ReportDocument document = new RdlcReportEngine().CreateDocument(parent, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Mixed child row", Amount = 4 } }
		}, SubreportResolver: resolver));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().ContainSingle();
		html.Should().Contain("Parent tablix").And.Contain("Mixed child row").And.Contain("Standalone parent item");
	}

	[Fact]
	public void Rdlc_engine_preserves_nested_page_decoration_offsets()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-header-footer.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition);
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().HaveCount(2);
		html.Split("Nested page header").Length.Should().Be(3);
		html.Split("Nested page footer").Length.Should().Be(3);
		html.Should().Contain("x=\"85.039\"").And.Contain("Nested page header");
	}

	[Fact]
	public void Rdlc_engine_applies_parameter_defaults_without_overriding_explicit_values()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "parameter-default.rdlc");
		using FileStream defaultDefinition = File.OpenRead(fixturePath);
		ReportDocument defaultDocument = new RdlcReportEngine().CreateDocument(defaultDefinition);
		string defaultHtml = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(defaultDocument, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		using FileStream explicitDefinition = File.OpenRead(fixturePath);
		ReportDocument explicitDocument = new RdlcReportEngine().CreateDocument(explicitDefinition, new RdlcDataContext(Parameters: new Dictionary<string, object?> { ["gReEtInG"] = "Explicit greeting" }));
		string explicitHtml = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(explicitDocument, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		defaultHtml.Should().Contain("Hello from RDLC default");
		explicitHtml.Should().Contain("Explicit greeting").And.NotContain("Hello from RDLC default");
	}

	[Fact]
	public void Rdlc_engine_uses_multi_value_parameter_defaults_for_join()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "multi-value-parameter.rdlc");
		using FileStream defaultDefinition = File.OpenRead(fixturePath);
		ReportDocument defaultDocument = new RdlcReportEngine().CreateDocument(defaultDefinition);
		string defaultHtml = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(defaultDocument, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		defaultHtml.Should().Contain("Regions: APAC, EMEA");
	}

	[Fact]
	public void Rdlc_engine_joins_allow_listed_multi_value_parameters()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "multi-value-parameter.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(Parameters: new Dictionary<string, object?>
		{
			["Regions"] = new[] { "APAC", "EMEA", "NA" }
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Regions: APAC, EMEA, NA");
	}

	[Fact]
	public void Rdlc_engine_does_not_execute_unsupported_code_expressions()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "unsupported-expression.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition);
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Safe prefix:").And.NotContain("Code.Untrusted");
	}

	[Fact]
	public void Rdlc_engine_rejects_unsupported_map_report_items_explicitly()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "unsupported-map.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		Action createDocument = () => new RdlcReportEngine().CreateDocument(definition);
		createDocument.Should().Throw<NotSupportedException>().WithMessage("*Map*");
	}

	[Fact]
	public void Rdlc_engine_rejects_unsupported_chart_types_explicitly()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "unsupported-chart.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		Action createDocument = () => new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 10 } }
		}));
		createDocument.Should().Throw<NotSupportedException>().WithMessage("*Radar*");
	}

	[Fact]
	public void Rdlc_engine_renders_column_charts_from_a_dedicated_fixture()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "column-chart.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 10 } }
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		html.Should().Contain("Column chart").And.Contain("<rect").And.Contain("Alpha");
		using var pdf = new SkiaPdfRenderer();
		pdf.Render(document, new ReportRenderOptions(ReportOutputFormat.Pdf)).Data.Span[..5].ToArray().Should().Equal("%PDF-"u8.ToArray());

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var excelArchive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var excelChart = new StreamReader(excelArchive.GetEntry("xl/charts/chart1_1.xml")!.Open());
		excelChart.ReadToEnd().Should().Contain("barDir").And.Contain("val=\"col\"");

		ReportOutput word = new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var wordChart = new StreamReader(wordArchive.GetEntry("word/charts/chart1.xml")!.Open());
		wordChart.ReadToEnd().Should().Contain("barDir").And.Contain("val=\"col\"");
	}

	[Fact]
	public void Rdlc_engine_rejects_sibling_row_group_branches_until_member_layout_is_supported()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "unsupported-group-branch.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		Action createDocument = () => new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Category = "A", Region = "X", Name = "Alpha" } }
		}));

		createDocument.Should().Throw<NotSupportedException>().WithMessage("*sibling row-group branches*");
	}

	[Fact]
	public void Rdlc_engine_renders_terminal_sibling_row_group_branches()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "sibling-group-branches.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha" },
				new { Category = "A", Region = "Y", Name = "Beta" },
				new { Category = "B", Region = "X", Name = "Gamma" }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		html.Should().Contain("Sibling branches").And.Contain("Category: A (2)").And.Contain("Category: B (1)").And.Contain("Region: X (2)").And.Contain("Region: Y (1)").And.Contain("Category detail: Alpha").And.Contain("Static interstitial section").And.Contain("Region detail: Gamma");
	}

	[Fact]
	public void Rdlc_engine_renders_sibling_row_group_branches_without_a_static_header()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "sibling-group-no-header.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha" },
				new { Category = "A", Region = "Y", Name = "Beta" },
				new { Category = "B", Region = "X", Name = "Gamma" }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		document.Pages.Should().HaveCount(1);
		html.Should().Contain("Category: A (2)").And.Contain("Category: B (1)").And.Contain("Region: X (2)").And.Contain("Region: Y (1)").And.Contain("Category detail: Alpha").And.Contain("Region detail: Gamma").And.Contain("Grand total rows: 3");
	}

	[Fact]
	public void Rdlc_engine_starts_sibling_branch_groups_on_explicit_page_breaks()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "sibling-group-start-pagebreak.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha" },
				new { Category = "A", Region = "Y", Name = "Beta" },
				new { Category = "B", Region = "X", Name = "Gamma" }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		document.Pages.Should().HaveCount(3);
		html.Should().Contain("Sibling start break").And.Contain("Category: A (2)").And.Contain("Region: X (2)").And.Contain("Region: Y (1)").And.Contain("Category detail: Gamma");
	}

	[Fact]
	public void Rdlc_engine_supports_start_and_end_sibling_branch_page_breaks()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "sibling-group-start-end-pagebreak.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha" },
				new { Category = "A", Region = "Y", Name = "Beta" },
				new { Category = "B", Region = "X", Name = "Gamma" }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		document.Pages.Should().HaveCount(4);
		html.Should().Contain("Sibling start-and-end break").And.Contain("Category: A (2)").And.Contain("Static interstitial section").And.Contain("Region: X (2)").And.Contain("Region: Y (1)").And.Contain("Category detail: Gamma").And.Contain("Grand total rows: 3");
	}

	[Fact]
	public void Rdlc_engine_propagates_nested_child_end_break_to_static_subtotal()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-sibling-child-end-pagebreak.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha", Amount = 10 },
				new { Category = "A", Region = "Y", Name = "Beta", Amount = 20 },
				new { Category = "B", Region = "X", Name = "Gamma", Amount = 30 }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		document.Pages.Should().HaveCount(4);
		html.Should().Contain("Nested child end break").And.Contain("Category: A").And.Contain("Category section: A").And.Contain("Category section: B").And.Contain("Category child wrapper: A").And.Contain("Category child wrapper: B").And.Contain("Region: X").And.Contain("Region: Y").And.Contain("Category subtotal: A (30)").And.Contain("Category subtotal: B (30)").And.Contain("Name detail: Gamma");
	}

	[Fact]
	public void Rdlc_engine_renders_nested_sibling_row_group_branches()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-sibling-group-branches.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha", Amount = 10 },
				new { Category = "A", Region = "Y", Name = "Beta", Amount = 20 },
				new { Category = "B", Region = "X", Name = "Gamma", Amount = 30 }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		document.Pages.Should().HaveCount(8);
		html.Should().Contain("Nested sibling branches").And.Contain("Category: A (2)").And.Contain("Category child section: A").And.Contain("Category child section: B").And.Contain("Region: X (1)").And.Contain("Region: Y (1)").And.Contain("Category subtotal: A (30)").And.Contain("Category subtotal: B (30)").And.Contain("Child name: Alpha (1)").And.Contain("Child name detail: Beta").And.Contain("Name: Alpha (1)").And.Contain("Nested detail: Gamma").And.Contain("Name detail: Beta").And.Contain("Grand total: 60");
	}

	[Fact]
	public void Rdlc_engine_renders_nested_sibling_children_under_a_single_root_group()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-sibling-single-root.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha", Amount = 10 },
				new { Category = "A", Region = "Y", Name = "Beta", Amount = 20 },
				new { Category = "B", Region = "X", Name = "Gamma", Amount = 30 }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		document.Pages.Should().HaveCount(2);
		html.Should().Contain("Nested single root").And.Contain("Category: A (2)").And.Contain("Category: B (1)").And.Contain("Category section: A").And.Contain("Region: Y (1)").And.Contain("Category subtotal: B (30)").And.Contain("Child name: Alpha (1)").And.Contain("Child name detail: Gamma").And.Contain("Grand total: 60");
	}

	[Fact]
	public void Rdlc_engine_renders_nested_sibling_children_without_a_static_header()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-sibling-single-root-no-header.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha", Amount = 10 },
				new { Category = "A", Region = "Y", Name = "Beta", Amount = 20 },
				new { Category = "B", Region = "X", Name = "Gamma", Amount = 30 }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		document.Pages.Should().HaveCount(2);
		html.Should().Contain("Category: A (2)").And.Contain("Category: B (1)").And.Contain("Category section: A").And.Contain("Region: Y (1)").And.Contain("Category subtotal: B (30)").And.Contain("Child name: Alpha (1)").And.Contain("Child name detail: Gamma").And.Contain("Grand total: 60");
	}

	[Fact]
	public void Rdlc_engine_rejects_unsupported_group_pagebreak_locations()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "unsupported-pagebreak.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		Action createDocument = () => new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Category = "A", Name = "Alpha" } }
		}));

		createDocument.Should().Throw<NotSupportedException>().WithMessage("*page breaks at 'Between'*Before*");
	}

	[Fact]
	public void Rdlc_engine_resolves_undeclared_parameters_case_insensitively()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "parameter-case-insensitive.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(Parameters: new Dictionary<string, object?>
		{
			["gReEtInG"] = "Welcome"
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Greeting: Welcome");
	}

	[Fact]
	public void Rdlc_engine_rejects_subreports_inside_tablix_cells_explicitly()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "unsupported-tablix-subreport.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		Action createDocument = () => new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Row" } }
		}));

		createDocument.Should().Throw<NotSupportedException>().WithMessage("*subreports inside tablix cells*");
	}

	[Fact]
	public void Rdlc_engine_rejects_nested_container_subreports_explicitly()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "unsupported-nested-subreport.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);

		Action createDocument = () => new RdlcReportEngine().CreateDocument(definition);

		createDocument.Should().Throw<NotSupportedException>().WithMessage("*only supports subreports as direct body items*");
	}

	[Fact]
	public void Rdlc_engine_resolves_allow_listed_is_nothing_expression()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "is-nothing.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(
			new Dictionary<string, IEnumerable> { ["Items"] = new[] { new { Name = "Alpha", Optional = (string?)null } } },
			new Dictionary<string, object?> { ["Optional"] = null }));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Null=True Value=False Parameter=True Logic=True Or=True Unknown= UnknownLogic=").And.NotContain("Unknown=True").And.NotContain("UnknownLogic=True");
	}

	[Fact]
	public void Rdlc_engine_preserves_textbox_hyperlinks_for_html_and_pdf()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "hyperlink.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(Parameters: new Dictionary<string, object?> { ["TargetUrl"] = "https://example.com/rdlc" }));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		string pdf = System.Text.Encoding.Latin1.GetString(new SkiaPdfRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Pdf)).Data.Span);

		html.Should().Contain("href=\"https://example.com/rdlc\"").And.Contain("Open linked report");
		pdf.Should().Contain("/URI").And.Contain("example.com/rdlc");
	}

	[Fact]
	public void Rdlc_engine_applies_parent_offsets_to_nested_report_items()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-items.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition);
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Nested report item").And.Contain("x=\"56.693\"");
	}

	[Fact]
	public void Rdlc_engine_maps_text_color_and_writing_mode_to_renderers()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "styled-text.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition);
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("fill=\"#FF0000\"").And.Contain("writing-mode=\"tb\"").And.Contain("縦書き styled text");
	}

	[Fact]
	public void Rdlc_engine_covers_international_text_styles_and_directions_from_one_fixture()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "international-text.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition);

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		html.Should().Contain("Latin styled report text").And.Contain("ภาษาไทย รายงาน").And.Contain("تقرير عربي").And.Contain("日本語レポート").And.Contain("縦書き styled text").And.Contain("font-weight=\"700\"").And.Contain("font-style=\"italic\"").And.Contain("direction=\"rtl\"").And.Contain("writing-mode=\"tb\"").And.Contain("fill=\"#123456\"");

		using var pdfRenderer = new SkiaPdfRenderer();
		pdfRenderer.Render(document, new ReportRenderOptions(ReportOutputFormat.Pdf)).Data.Span[..5].ToArray().Should().Equal("%PDF-"u8.ToArray());

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var excelArchive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var sheetReader = new StreamReader(excelArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		string sheet = sheetReader.ReadToEnd();
		sheet.Should().Contain("Latin styled report text").And.Contain("ภาษาไทย รายงาน").And.Contain("تقرير عربي").And.Contain("日本語レポート").And.Contain("縦書き styled text");
		using var stylesReader = new StreamReader(excelArchive.GetEntry("xl/styles.xml")!.Open());
		stylesReader.ReadToEnd().Should().Contain("readingOrder").And.Contain("textRotation");

		ReportOutput word = new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var documentReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		string wordDocument = documentReader.ReadToEnd();
		wordDocument.Should().Contain("Latin styled report text").And.Contain("ภาษาไทย รายงาน").And.Contain("تقرير عربي").And.Contain("日本語レポート").And.Contain("縦書き styled text").And.Contain("w:textDirection").And.Contain("w:b").And.Contain("w:i");
	}

	[Fact]
	public void Rdlc_engine_resolves_expression_image_values_through_the_injected_resolver()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "image-expression.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(
			Parameters: new Dictionary<string, object?> { ["ImageKey"] = "logo" },
			ImageResolver: new ExpressionImageResolver()));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("data:image/png;base64,cG5n");
	}

	[Fact]
	public void Rdlc_engine_resolves_expression_images_inside_tablix_cells()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "tablix-image.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(
			new Dictionary<string, IEnumerable> { ["Items"] = new[] { new { ImageKey = "logo" } } },
			ImageResolver: new ExpressionImageResolver()));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Logo").And.Contain("data:image/png;base64,cG5n");
	}

	[Fact]
	public void Rdlc_engine_resolves_safe_composite_field_and_parameter_expressions()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "composite-expression.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(
			new Dictionary<string, IEnumerable> { ["Items"] = new[] { new { Name = "Alpha", Amount = 7 }, new { Name = "Beta", Amount = 12 } } },
			new Dictionary<string, object?> { ["Greeting"] = "Welcome" }));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Greeting: Welcome").And.Contain("Customer: Alpha - 7.00").And.Contain("Customer: Beta - High").And.Contain("Literal match");
	}

	[Fact]
	public void Rdlc_engine_resolves_allow_listed_string_functions()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "string-functions.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = " Alpha " } }
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Len=7 Trim=Alpha Upper= ALPHA  Lower= alpha ");
	}

	[Fact]
	public void Rdlc_engine_resolves_safe_search_and_replace_string_functions()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "string-functions-advanced.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha" } }
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Index=3 Replace=AlPHa Mid=pha").And.Contain("Malformed=");
	}

	[Fact]
	public void Rdlc_engine_sorts_tablix_rows_before_pagination()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "sorted-tablix.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Beta", Amount = 2 }, new { Name = "Alpha", Amount = 10 }, new { Name = "Gamma", Amount = 1 } }
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Rows: 3 Total: 13 Min: 1 Max: 10");
		html.IndexOf("Alpha", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Beta", StringComparison.Ordinal));
		html.IndexOf("Beta", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Gamma", StringComparison.Ordinal));
	}

	[Fact]
	public void Rdlc_engine_sorts_decimal_comma_values_numerically_in_current_culture()
	{
		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
			string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "culture-sorted-tablix.rdlc");
			using FileStream definition = File.OpenRead(fixturePath);
			ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
			{
				["Items"] = new[]
				{
					new { Name = "Ten", Amount = "10,0" },
					new { Name = "OnePointFive", Amount = "1,5" },
					new { Name = "Two", Amount = "2,0" }
				}
			}));
			string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

			html.IndexOf("OnePointFive", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Two", StringComparison.Ordinal));
			html.IndexOf("Two", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Ten", StringComparison.Ordinal));
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	[Fact]
	public void Rdlc_engine_uses_group_scope_for_tablix_aggregates()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "grouped-tablix.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha", Amount = 10 },
				new { Category = "B", Region = "Y", Name = "Beta", Amount = 2 },
				new { Category = "B", Region = "Y", Name = "Gamma", Amount = 1 },
				new { Category = "B", Region = "Z", Name = "Delta", Amount = 4 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Category: A").And.Contain("Category: B").And.Contain("Region: X").And.Contain("Region: Y").And.Contain("Region: Z").And.Contain("Group: A (1/10)").And.Contain("Group: B (2/3)").And.Contain("Group: B (1/4)").And.Contain("Subtotal: A").And.Contain("Sum=10 Avg=10").And.Contain("Subtotal: B").And.Contain("Sum=3 Avg=1.5").And.Contain("Sum=4 Avg=4");
	}

	[Fact]
	public void Rdlc_engine_keeps_composite_group_keys_with_delimiter_values_separate()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "grouped-tablix.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A\u001FX", Region = "Y", Name = "First", Amount = 10 },
				new { Category = "A", Region = "X\u001FY", Name = "Second", Amount = 2 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Group rows: 1").And.Contain("Group: A\u001FX (1/10)").And.Contain("Group: A (1/2)");
	}

	[Fact]
	public void Rdlc_engine_keeps_nested_group_prefixes_with_delimiter_values_separate()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-grouped-tablix.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A\u001FX", Region = "Y", Segment = "I", Name = "First", Amount = 10 },
				new { Category = "A", Region = "X\u001FY", Segment = "II", Name = "Second", Amount = 2 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Region: Y (1, Sum=10)").And.Contain("Region: X\u001FY (1, Sum=2)");
	}

	[Fact]
	public void Rdlc_engine_resolves_first_last_and_count_scoped_aggregates()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "scoped-aggregates.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Name = "Alpha", Amount = 10 },
				new { Name = "Beta", Amount = 0 },
				new { Name = "Gamma", Amount = 3 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("First=Alpha Last=Gamma Count=3 Min=0 Max=10");
	}

	[Fact]
	public void Rdlc_engine_omits_items_hidden_by_allow_listed_visibility_expression()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "visibility.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(
			new Dictionary<string, IEnumerable> { ["Items"] = new[] { new { Name = "Hidden row" } } },
			new Dictionary<string, object?> { ["HideDetails"] = true }));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Visible content").And.Contain("Tablix header").And.NotContain("Hidden content").And.NotContain("Hidden tablix content");

		using FileStream expandedDefinition = File.OpenRead(fixturePath);
		ReportDocument expandedDocument = new RdlcReportEngine().CreateDocument(expandedDefinition, new RdlcDataContext(
			new Dictionary<string, IEnumerable> { ["Items"] = new[] { new { Name = "Expanded row" } } },
			new Dictionary<string, object?> { ["HideDetails"] = false }));
		string expandedHtml = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(expandedDocument, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		expandedHtml.Should().Contain("Hidden content").And.Contain("Hidden tablix content");
	}

	[Fact]
	public void Rdlc_engine_renders_three_nested_row_group_levels()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-grouped-tablix.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Segment = "I", Name = "Alpha", Amount = 10 },
				new { Category = "A", Region = "X", Segment = "II", Name = "Beta", Amount = 2 },
				new { Category = "A", Region = "Y", Segment = "I", Name = "Gamma", Amount = 3 },
				new { Category = "B", Region = "X", Segment = "I", Name = "Delta", Amount = 4 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Category: A (3, Sum=15)").And.Contain("Region: X (2, Sum=12)").And.Contain("Region: Y (1, Sum=3)").And.Contain("Segment: I (1, Sum=10)").And.Contain("Segment: II (1, Sum=2)").And.Contain("Category: B (1, Sum=4)").And.Contain("Detail: Delta").And.Contain("Subtotal: B = 4");
	}

	[Fact]
	public void Rdlc_engine_starts_grouped_scopes_on_explicit_page_breaks()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "grouped-pagebreak.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Name = "Alpha", Amount = 1 },
				new { Category = "B", Name = "Beta", Amount = 2 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().HaveCount(2);
		html.Should().Contain("Detail: Alpha").And.Contain("Detail: Beta");
	}

	[Fact]
	public void Rdlc_engine_applies_nested_group_pagebreak_only_at_its_scope_level()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-group-pagebreak.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha" },
				new { Category = "A", Region = "Y", Name = "Beta" },
				new { Category = "B", Region = "Y", Name = "Gamma" }
			}
		}));

		document.Pages.Should().HaveCount(2);
	}

	[Fact]
	public void Rdlc_engine_supports_start_and_end_pagebreaks_on_linear_nested_groups()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-group-start-end-pagebreak.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha" },
				new { Category = "A", Region = "Y", Name = "Beta" },
				new { Category = "B", Region = "Y", Name = "Gamma" }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().HaveCount(4);
		html.Should().Contain("Nested start and end page breaks").And.Contain("Detail: Alpha").And.Contain("Detail: Beta").And.Contain("Detail: Gamma");
	}

	[Fact]
	public void Rdlc_engine_repeats_static_detail_and_subtotal_rows_for_nested_groups()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-group-static-detail-subtotal.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha", Amount = 1 },
				new { Category = "A", Region = "X", Name = "Beta", Amount = 2 },
				new { Category = "A", Region = "Y", Name = "Gamma", Amount = 3 },
				new { Category = "B", Region = "Y", Name = "Delta", Amount = 4 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Detail: Alpha").And.Contain("Detail: Beta").And.Contain("Detail: Gamma").And.Contain("Detail: Delta")
			.And.Contain("Region subtotal: 3").And.Contain("Category subtotal: 6").And.Contain("Category subtotal: 4").And.Contain("Grand total: 10");
		html.Split("Region subtotal: 3").Should().HaveCount(3);
		html.Split("Category subtotal: ").Should().HaveCount(3);
	}

	[Fact]
	public void Rdlc_engine_preserves_a_single_child_static_wrapper_and_root_total()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-static-wrapper-single-child.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Name = "Alpha" },
				new { Category = "A", Region = "Y", Name = "Beta" },
				new { Category = "B", Region = "Y", Name = "Gamma" }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Nested static wrapper").And.Contain("Category: A").And.Contain("Category: B")
			.And.Contain("Category wrapper").And.Contain("Region: X").And.Contain("Region: Y")
			.And.Contain("Detail: Alpha").And.Contain("Detail: Beta").And.Contain("Detail: Gamma").And.Contain("Grand total rows: 3");
		html.Split("Category wrapper").Should().HaveCount(3);
		html.Split("Grand total rows: 3").Should().HaveCount(2);
	}

	[Fact]
	public void Rdlc_engine_recurses_through_static_wrappers_with_multiple_nested_dynamic_children()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "nested-static-wrapper-multiple-children.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = "A", Region = "X", Product = "P1", Name = "Alpha" },
				new { Category = "A", Region = "X", Product = "P2", Name = "Beta" },
				new { Category = "A", Region = "Y", Product = "P2", Name = "Gamma" },
				new { Category = "B", Region = "Y", Product = "P3", Name = "Delta" }
			}
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Nested multi-child wrapper").And.Contain("Category: A").And.Contain("Category: B")
			.And.Contain("Region: X").And.Contain("Region: Y").And.Contain("Product: P1").And.Contain("Product: P2").And.Contain("Product: P3")
			.And.Contain("Region leading row").And.Contain("Region footer: X (2)").And.Contain("Region footer: Y (1)").And.Contain("Grand total rows: 4");
		html.Split("Category wrapper").Should().HaveCount(3);
		html.Split("Region leading row").Should().HaveCount(4);
	}

	[Fact]
	public void Rdlc_engine_keeps_null_group_keys_in_one_aggregate_scope()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "grouped-null-keys.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Category = (string?)null, Name = "Null A", Amount = 1 },
				new { Category = (string?)null, Name = "Null B", Amount = 2 },
				new { Category = (string?)"A", Name = "Alpha", Amount = 5 }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Category=[] Rows=2").And.Contain("First=Null A").And.Contain("Last=Null B").And.Contain("Subtotal=3").And.Contain("Category=[A] Rows=1").And.Contain("Subtotal=5");
	}

	[Fact]
	public void Rdlc_engine_renders_static_group_header_when_data_region_is_empty()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "grouped-null-keys.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = Array.Empty<object>()
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().HaveCount(1);
		html.Should().Contain("Null-key groups").And.NotContain("Detail:");
	}

	[Fact]
	public void Rdlc_engine_renders_no_rows_message_for_empty_tablix()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "no-rows-message.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = Array.Empty<object>()
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		document.Pages.Should().ContainSingle();
		html.Should().Contain("Empty state").And.Contain("No data available").And.NotContain("Detail:");
		using var pdf = new SkiaPdfRenderer();
		pdf.Render(document, new ReportRenderOptions(ReportOutputFormat.Pdf)).Data.Span[..5].ToArray().Should().Equal("%PDF-"u8.ToArray());

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var excelArchive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var excelReader = new StreamReader(excelArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		excelReader.ReadToEnd().Should().Contain("No data available");

		ReportOutput word = new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		using var wordReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		wordReader.ReadToEnd().Should().Contain("No data available");
	}

	[Fact]
	public void Rdlc_engine_resolves_embedded_images_through_the_injected_resolver()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "image.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(ImageResolver: new SkiaImageResolver()));
		ReportOutput output = new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html));
		string html = System.Text.Encoding.UTF8.GetString(output.Data.Span);

		document.Pages.Should().ContainSingle();
		html.Should().Contain("Embedded image").And.Contain("data:image/png;base64,");
	}

	[Fact]
	public void Rdlc_engine_renders_bar_and_column_charts_to_all_backends()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "chart.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		var rows = new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 10 }, new { Name = "Beta", Amount = 20 } }
		};
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(rows));

		ReportOutput html = new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html));
		System.Text.Encoding.UTF8.GetString(html.Data.Span).Should().Contain("Sales by item").And.Contain("Column trend").And.Contain("Report layout").And.Contain("Alpha").And.Contain("Beta").And.Contain("<rect").And.Contain("<line");
		using var pdf = new SkiaPdfRenderer();
		pdf.Render(document, new ReportRenderOptions(ReportOutputFormat.Pdf)).Data.Span[..5].ToArray().Should().Equal("%PDF-"u8.ToArray());
		ReportOutput excel = new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var archive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		using var sheet = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
		sheet.ReadToEnd().Should().Contain("Sales by item").And.Contain("Alpha").And.Contain("20");
		archive.GetEntry("xl/charts/chart1_1.xml").Should().NotBeNull();
		archive.Entries.Count(entry => entry.FullName.StartsWith("xl/charts/", StringComparison.Ordinal)).Should().Be(6);
		using (var columnChartReader = new StreamReader(archive.GetEntry("xl/charts/chart1_5.xml")!.Open()))
		{
			columnChartReader.ReadToEnd().Should().Contain("barChart").And.Contain("barDir").And.Contain("val=\"col\"");
		}
		using (var doughnutChartReader = new StreamReader(archive.GetEntry("xl/charts/chart1_6.xml")!.Open()))
		{
			doughnutChartReader.ReadToEnd().Should().Contain("doughnutChart").And.Contain("holeSize");
		}
		archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
		archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().NotBeNull();
		ReportOutput word = new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		wordArchive.GetEntry("word/charts/chart1.xml").Should().NotBeNull();
		wordArchive.Entries.Count(entry => entry.FullName.StartsWith("word/charts/", StringComparison.Ordinal)).Should().Be(6);
		wordArchive.GetEntry("word/_rels/document.xml.rels").Should().NotBeNull();
		using var wordDocumentReader = new StreamReader(wordArchive.GetEntry("word/document.xml")!.Open());
		wordDocumentReader.ReadToEnd().Should().Contain("<anchor").And.Contain("positionH").And.Contain("positionV").And.NotContain("<inline");
	}

	[Fact]
	public void Rdlc_engine_renders_line_area_and_pie_charts_with_semantic_backend_output()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "chart.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 10 }, new { Name = "Beta", Amount = 20 }, new { Name = "Gamma", Amount = 5 } }
		}));

		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);
		html.Should().Contain("Sales by item").And.Contain("Trend").And.Contain("Area trend").And.Contain("Share").And.Contain("Column trend").And.Contain("Share ring").And.Contain("<rect").And.Contain("<line").And.Contain("<polyline").And.Contain("<polygon").And.Contain("<path");
		using var pdfRenderer = new SkiaPdfRenderer();
		pdfRenderer.Render(document, new ReportRenderOptions(ReportOutputFormat.Pdf)).Data.Span[..5].ToArray().Should().Equal("%PDF-"u8.ToArray());

		ReportOutput excel = new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml));
		using var archive = new ZipArchive(new MemoryStream(excel.Data.ToArray()), ZipArchiveMode.Read);
		string chartXml = string.Join('\n', archive.Entries.Where(entry => entry.FullName.StartsWith("xl/charts/", StringComparison.Ordinal)).Select(entry =>
		{
			using var reader = new StreamReader(entry.Open());
			return reader.ReadToEnd();
		}));
		chartXml.Should().Contain("barChart").And.Contain("barDir").And.Contain("val=\"col\"").And.Contain("lineChart").And.Contain("areaChart").And.Contain("pieChart").And.Contain("doughnutChart").And.Contain("holeSize");

		ReportOutput word = new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml));
		using var wordArchive = new ZipArchive(new MemoryStream(word.Data.ToArray()), ZipArchiveMode.Read);
		string wordChartXml = string.Join('\n', wordArchive.Entries.Where(entry => entry.FullName.StartsWith("word/charts/", StringComparison.Ordinal)).Select(entry =>
		{
			using var reader = new StreamReader(entry.Open());
			return reader.ReadToEnd();
		}));
		wordChartXml.Should().Contain("barChart").And.Contain("barDir").And.Contain("val=\"col\"").And.Contain("lineChart").And.Contain("areaChart").And.Contain("pieChart").And.Contain("doughnutChart").And.Contain("holeSize");
	}

	[Fact]
	public void Openxml_renderers_emit_well_formed_xml_for_fixture_chart_outputs()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "chart.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 10 }, new { Name = "Beta", Amount = 20 } }
		}));

		foreach (ReportOutput output in new[]
		{
			new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml)),
			new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml))
		})
		{
			using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
			foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
			{
				using Stream entryStream = entry.Open();
				Action parse = () => XDocument.Load(entryStream, LoadOptions.PreserveWhitespace);
				parse.Should().NotThrow($"OpenXML part {entry.FullName} should be well-formed");
			}
		}
	}

	[Fact]
	public void Openxml_renderers_keep_internal_relationship_targets_valid()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "chart.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 10 }, new { Name = "Beta", Amount = 20 } }
		}));

		foreach (ReportOutput output in new[]
		{
			new ExcelOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.ExcelOpenXml)),
			new WordOpenXmlRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.WordOpenXml))
		})
		{
			using var archive = new ZipArchive(new MemoryStream(output.Data.ToArray()), ZipArchiveMode.Read);
			HashSet<string> entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
			foreach (ZipArchiveEntry relationshipEntry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
			{
				using Stream relationshipStream = relationshipEntry.Open();
				XDocument relationships = XDocument.Load(relationshipStream, LoadOptions.PreserveWhitespace);
				IReadOnlyList<XElement> relationshipNodes = relationships.Root?.Elements().ToArray() ?? Array.Empty<XElement>();
				relationshipNodes.Select(node => node.Attribute("Id")?.Value).Should().OnlyHaveUniqueItems();
				foreach (XElement relationship in relationshipNodes)
				{
					if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					string target = relationship.Attribute("Target")?.Value ?? string.Empty;
					target.Should().NotBeNullOrWhiteSpace();
					entries.Should().Contain(ResolvePackagePath(relationshipEntry.FullName, target), $"internal relationship target from {relationshipEntry.FullName} should exist");
				}
			}
		}
	}

	[Fact]
	public void Rdlc_engine_renders_visual_items_inside_tablix_cells()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "tablix-visual-items.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Alpha", Amount = 10 }, new { Name = "Beta", Amount = 20 } }
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Cell visuals").And.Contain("Nested cell text").And.Contain("Sales in cell").And.Contain("Alpha").And.Contain("Beta").And.Contain("<rect").And.Contain("<line");
	}

	[Fact]
	public void Local_report_facade_loads_rdlc_and_renders_requested_format()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "simple.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		var renderers = new HeadlessReportRenderer(new IReportRenderer[] { new HtmlReportRenderer() });
		using var report = new LocalReport(new RdlcReportEngine(), renderers);
		report.LoadReportDefinition(definition);
		report.SetDataSources(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[] { new { Name = "Facade", Amount = 42 } }
		});

		ReportOutput output = report.Render(ReportOutputFormat.Html);
		System.Text.Encoding.UTF8.GetString(output.Data.Span).Should().Contain("Facade").And.Contain("42");
		report.CreateDocument().Pages.Should().ContainSingle();
	}

	[Fact]
	public async Task Server_report_forwards_explicit_request_to_transport()
	{
		var transport = new RecordingTransport();
		var endpoint = new Uri("https://reports.example.test/ReportServer");
		var report = new ServerReport(transport, endpoint, "/Sales/Summary");
		var parameters = new Dictionary<string, string> { ["Region"] = "APAC" };

		await report.RenderAsync(ReportOutputFormat.Pdf, "<DeviceInfo />", parameters);

		transport.Request.Should().NotBeNull();
		transport.Request!.Endpoint.Should().Be(endpoint);
		transport.Request.ReportPath.Should().Be("/Sales/Summary");
		transport.Request.Format.Should().Be(ReportOutputFormat.Pdf);
		transport.Request.Parameters!["Region"].Should().Be("APAC");
	}

	[Fact]
	public async Task Http_report_server_transport_uses_explicit_authentication_and_url_access()
	{
		var handler = new RecordingHttpHandler();
		using var client = new HttpClient(handler);
		var authenticator = new RecordingAuthenticator();
		var transport = new HttpReportServerTransport(client, authenticator);

		ReportOutput output = await transport.RenderAsync(new ReportServerRenderRequest(
			new Uri("https://reports.example.test/ReportServer"),
			"/Sales/Summary",
			ReportOutputFormat.Pdf,
			"<DeviceInfo />",
			new Dictionary<string, string> { ["Region"] = "APAC & SEA" }));

		output.Data.ToArray().Should().Equal("%PDF-"u8.ToArray());
		handler.Request.Should().NotBeNull();
		handler.Request!.RequestUri!.AbsoluteUri.Should().Contain("?/Sales/Summary").And.Contain("rs%3AFormat=PDF").And.Contain("Region=APAC%20%26%20SEA");
		authenticator.Called.Should().BeTrue();
		handler.Request.Headers.GetValues("X-Test-Auth").Should().ContainSingle().Which.Should().Be("injected");
	}

	[Fact]
	public async Task Http_report_server_transport_rejects_non_http_endpoints()
	{
		using var client = new HttpClient(new RecordingHttpHandler());
		var transport = new HttpReportServerTransport(client);
		Func<Task> render = () => transport.RenderAsync(new ReportServerRenderRequest(
			new Uri("file:///reports/ReportServer"),
			"/Sales/Summary",
			ReportOutputFormat.Pdf));

		await render.Should().ThrowAsync<ArgumentException>().WithMessage("*absolute HTTP(S) URI*");
	}

	[Fact]
	public async Task Http_report_server_transport_rejects_unsupported_formats_before_sending()
	{
		var handler = new RecordingHttpHandler();
		using var client = new HttpClient(handler);
		var transport = new HttpReportServerTransport(client);
		Func<Task> render = () => transport.RenderAsync(new ReportServerRenderRequest(
			new Uri("https://reports.example.test/ReportServer"),
			"/Sales/Summary",
			(ReportOutputFormat)999));

		await render.Should().ThrowAsync<ArgumentOutOfRangeException>();
		handler.Request.Should().BeNull();
	}

	private static string ResolvePackagePath(string relationshipPath, string target)
	{
		string baseDirectory = string.Empty;
		if (!string.Equals(relationshipPath, "_rels/.rels", StringComparison.Ordinal))
		{
			int marker = relationshipPath.LastIndexOf("/_rels/", StringComparison.Ordinal);
			marker.Should().BeGreaterThanOrEqualTo(0);
			string sourcePath = relationshipPath[..marker] + "/" + relationshipPath[(marker + 7)..^5];
			int slash = sourcePath.LastIndexOf('/');
			baseDirectory = slash >= 0 ? sourcePath[..slash] : string.Empty;
		}

		var segments = new List<string>();
		string combined = target.StartsWith("/", StringComparison.Ordinal) ? target[1..] : string.Join('/', new[] { baseDirectory, target }.Where(value => !string.IsNullOrEmpty(value)));
		foreach (string segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment == ".")
			{
				continue;
			}
			if (segment == "..")
			{
				segments.Should().NotBeEmpty();
				segments.RemoveAt(segments.Count - 1);
				continue;
			}
			segments.Add(segment);
		}

		return string.Join('/', segments);
	}

	private sealed class RecordingTransport : IReportServerTransport
	{
		public ReportServerRenderRequest? Request { get; private set; }

		public Task<ReportOutput> RenderAsync(ReportServerRenderRequest request, CancellationToken cancellationToken = default)
		{
			Request = request;
			return Task.FromResult(new ReportOutput(request.Format, "application/pdf", "pdf", "%PDF-"u8.ToArray()));
		}
	}

	private sealed class RecordingHttpHandler : HttpMessageHandler
	{
		public HttpRequestMessage? Request { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Request = request;
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new ByteArrayContent("%PDF-"u8.ToArray())
			});
		}
	}

	private sealed class RecordingAuthenticator : IReportServerAuthenticator
	{
		public bool Called { get; private set; }

		public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
		{
			Called = true;
			request.Headers.Add("X-Test-Auth", "injected");
			return ValueTask.CompletedTask;
		}
	}

	private sealed class ExpressionImageResolver : IImageResolver
	{
		public RenderImage? Resolve(RenderImageRequest request)
		{
			request.Value.Should().Be("logo");
			return new RenderImage(1, 1, "png"u8.ToArray());
		}
	}

	private sealed class FixturePageSource : IReportPageSource
	{
		public int PageCount => 2;

		public RenderSize GetPageSize(int pageIndex) => new(240, 120);

		public void RenderPage(int pageIndex, IRenderCanvas canvas)
		{
			canvas.Clear(RenderColor.White);
			canvas.DrawText($"Legacy page {pageIndex + 1}", new RenderPoint(12, 24), new FontRequest("Arial", 12), RenderColor.Black);
		}
	}

	private sealed class EmptyPageSource : IReportPageSource
	{
		public int PageCount => 0;

		public RenderSize GetPageSize(int pageIndex) => new(240, 120);

		public void RenderPage(int pageIndex, IRenderCanvas canvas)
		{
		}
	}

	private sealed class InvalidSizePageSource : IReportPageSource
	{
		public int PageCount => 1;

		public RenderSize GetPageSize(int pageIndex) => new(0, 120);

		public void RenderPage(int pageIndex, IRenderCanvas canvas)
		{
		}
	}

	[Fact]
	public void Rdlc_engine_ignores_non_finite_chart_values()
	{
		string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "engine", "chart.rdlc");
		using FileStream definition = File.OpenRead(fixturePath);
		ReportDocument document = new RdlcReportEngine().CreateDocument(definition, new RdlcDataContext(new Dictionary<string, IEnumerable>
		{
			["Items"] = new[]
			{
				new { Name = "Finite", Amount = 5f },
				new { Name = "Not-a-number", Amount = float.NaN },
				new { Name = "Infinite", Amount = float.PositiveInfinity }
			}
		}));
		string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(document, new ReportRenderOptions(ReportOutputFormat.Html)).Data.Span);

		html.Should().Contain("Finite").And.NotContain("Not-a-number").And.NotContain("Infinite");
	}

	private sealed class NonFinitePageSource : IReportPageSource
	{
		public int PageCount => 1;

		public RenderSize GetPageSize(int pageIndex) => new(float.NaN, 120);

		public void RenderPage(int pageIndex, IRenderCanvas canvas)
		{
		}
	}

	private sealed class FixtureSubreportResolver : IRdlcSubreportResolver
	{
		private readonly byte[] _definition;

		public FixtureSubreportResolver(byte[] definition) => _definition = definition;

		public List<string> OpenedNames { get; } = new();

		public Stream Open(string reportName)
		{
			OpenedNames.Add(reportName);
			return new MemoryStream(_definition, writable: false);
		}
	}
}
