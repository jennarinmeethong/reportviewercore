using ReportViewerCore.Rendering;
using SkiaSharp;

namespace ReportViewerCore.Rendering.Skia;

public sealed class SkiaPdfDocument : IRenderDocument
{
	private readonly SKDocument _document;
	private SkiaRenderCanvas? _currentPage;
	private bool _completed;

	public SkiaPdfDocument(Stream output, SkiaFontResolver? fonts = null)
	{
		ArgumentNullException.ThrowIfNull(output);
		Fonts = fonts ?? new SkiaFontResolver();
		_document = SKDocument.CreatePdf(output) ?? throw new InvalidOperationException("SkiaSharp could not create a PDF document.");
	}

	public SkiaFontResolver Fonts { get; }

	public IRenderCanvas BeginPage(RenderSize size)
	{
		ThrowIfCompleted();
		if (_currentPage is not null)
		{
			throw new InvalidOperationException("End the current PDF page before starting another page.");
		}
		if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size), "PDF page dimensions must be finite and greater than zero.");
		}

		SKCanvas canvas = _document.BeginPage(size.Width, size.Height);
		_currentPage = new SkiaRenderCanvas(canvas, size, Fonts);
		return _currentPage;
	}

	public void EndPage()
	{
		ThrowIfCompleted();
		if (_currentPage is null)
		{
			throw new InvalidOperationException("There is no active PDF page.");
		}

		_currentPage.Dispose();
		_currentPage = null;
		_document.EndPage();
	}

	public void Complete()
	{
		ThrowIfCompleted();
		if (_currentPage is not null)
		{
			throw new InvalidOperationException("End the current PDF page before completing the document.");
		}

		_document.Close();
		_completed = true;
	}

	public void Dispose()
	{
		if (!_completed)
		{
			_document.Abort();
			_completed = true;
		}

		Fonts.Dispose();
		_document.Dispose();
	}

	private void ThrowIfCompleted()
	{
		ObjectDisposedException.ThrowIf(_completed, this);
	}
}
