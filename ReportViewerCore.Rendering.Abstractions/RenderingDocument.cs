namespace ReportViewerCore.Rendering;

public enum ReportOutputFormat
{
	Pdf,
	Html,
	ExcelOpenXml,
	WordOpenXml
}

public sealed record ReportRenderOptions(
	ReportOutputFormat Format,
	string? DeviceInfo = null,
	string? CultureName = null);

public sealed record ReportOutput(
	ReportOutputFormat Format,
	string MimeType,
	string FileExtension,
	ReadOnlyMemory<byte> Data);

public delegate void RenderPageDelegate(IRenderCanvas canvas);

public sealed record ReportPage(RenderSize Size, RenderPageDelegate Draw)
{
	public void Render(IRenderCanvas canvas)
	{
		ArgumentNullException.ThrowIfNull(canvas);
		Draw(canvas);
	}
}

public sealed class ReportDocument
{
	public ReportDocument(IEnumerable<ReportPage> pages)
	{
		ArgumentNullException.ThrowIfNull(pages);
		Pages = pages.ToArray();
		if (Pages.Count == 0)
		{
			throw new ArgumentException("A report document must contain at least one page.", nameof(pages));
		}
	}

	public IReadOnlyList<ReportPage> Pages { get; }
}
