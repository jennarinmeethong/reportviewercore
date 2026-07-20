using ReportViewerCore.Rendering;

namespace ReportViewerCore.Rendering.Skia;

public sealed class SkiaPdfRenderer : IReportRenderer, IDisposable
{
	private readonly SkiaFontResolver _fonts;

	public SkiaPdfRenderer(SkiaFontResolver? fonts = null)
	{
		_fonts = fonts ?? new SkiaFontResolver();
	}

	public ReportOutputFormat Format => ReportOutputFormat.Pdf;

	public ReportOutput Render(ReportDocument document, ReportRenderOptions options)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(options);
		if (options.Format != Format)
		{
			throw new ArgumentException($"This renderer only supports {Format}.", nameof(options));
		}

		using var output = new MemoryStream();
		using (var pdf = new SkiaPdfDocument(output, _fonts))
		{
			foreach (ReportPage page in document.Pages)
			{
				IRenderCanvas canvas = pdf.BeginPage(page.Size);
				page.Render(canvas);
				pdf.EndPage();
			}

			pdf.Complete();
		}

		return new ReportOutput(Format, "application/pdf", "pdf", output.ToArray());
	}

	public void Dispose()
	{
		_fonts.Dispose();
	}
}
