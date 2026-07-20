using ReportViewerCore.Rendering;
using SkiaSharp;

namespace ReportViewerCore.Rendering.Skia;

public sealed class SkiaRenderCanvas : IRenderCanvas
{
	private readonly SKCanvas _canvas;
	private readonly SkiaFontResolver _fonts;
	private readonly SkiaTextShaper _shaper;
	private bool _disposed;

	internal SkiaRenderCanvas(SKCanvas canvas, RenderSize size, SkiaFontResolver fonts)
	{
		_canvas = canvas;
		Size = size;
		_fonts = fonts;
		_shaper = new SkiaTextShaper(fonts);
	}

	public RenderSize Size { get; }

	public void Clear(RenderColor color)
	{
		ThrowIfDisposed();
		_canvas.Clear(ToSkColor(color));
	}

	public void FillRectangle(RenderRect rectangle, RenderColor color)
	{
		ThrowIfDisposed();
		using var paint = CreatePaint(color, SKPaintStyle.Fill);
		_canvas.DrawRect(ToSkRect(rectangle), paint);
	}

	public void DrawRectangle(RenderRect rectangle, RenderColor color, float strokeWidth)
	{
		ThrowIfDisposed();
		using var paint = CreatePaint(color, SKPaintStyle.Stroke, strokeWidth);
		_canvas.DrawRect(ToSkRect(rectangle), paint);
	}

	public void DrawLine(RenderPoint start, RenderPoint end, RenderColor color, float strokeWidth)
	{
		ThrowIfDisposed();
		using var paint = CreatePaint(color, SKPaintStyle.Stroke, strokeWidth);
		_canvas.DrawLine(start.X, start.Y, end.X, end.Y, paint);
	}

	public void DrawText(string text, RenderPoint baseline, FontRequest font, RenderColor color, TextDirection direction = TextDirection.LeftToRight)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);

		using SkiaFont resolvedFont = _fonts.Resolve(font);
		ShapedText shaped = _shaper.Shape(text, font, direction);
		if (shaped.Glyphs.Count == 0)
		{
			return;
		}

		ushort[] glyphIds = shaped.Glyphs.Select(glyph => checked((ushort)glyph.GlyphId)).ToArray();
		var positions = new SKPoint[glyphIds.Length];
		float x = baseline.X;
		float y = baseline.Y;
		for (int i = 0; i < glyphIds.Length; i++)
		{
			ShapedGlyph glyph = shaped.Glyphs[i];
			positions[i] = new SKPoint(x + glyph.OffsetX, y - glyph.OffsetY);
			x += glyph.AdvanceX;
			y -= glyph.AdvanceY;
		}

		using var builder = new SKTextBlobBuilder();
		builder.AddPositionedRun(glyphIds, resolvedFont.Font, positions);
		using SKTextBlob? blob = builder.Build();
		if (blob is null)
		{
			return;
		}

		using var paint = CreatePaint(color, SKPaintStyle.Fill);
		_canvas.DrawText(blob, 0, 0, paint);
	}

	public void DrawHyperlink(string text, RenderPoint baseline, FontRequest font, RenderColor color, string url, TextDirection direction = TextDirection.LeftToRight)
	{
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(url);
		ValidateUrl(url);
		DrawText(text, baseline, font, color, direction);
		ShapedText shaped = _shaper.Shape(text, font, direction);
		FontMetrics metrics = _fonts.GetMetrics(font);
		_canvas.DrawUrlAnnotation(new SKRect(baseline.X, baseline.Y - metrics.Ascent, baseline.X + MathF.Abs(shaped.AdvanceX), baseline.Y - metrics.Ascent + metrics.LineHeight), url);
	}

	public void DrawImage(RenderImage image, RenderRect destination)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(image);

		using SKData data = SKData.CreateCopy(image.PngData.Span);
		using SKImage? decoded = SKImage.FromEncodedData(data);
		if (decoded is null)
		{
			throw new UnsupportedImageException();
		}

		using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
		_canvas.DrawImage(decoded, ToSkRect(destination), paint);
	}

	public void DrawBarChart(string title, IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(bars);
		DrawText(title, new RenderPoint(destination.X + 4, destination.Y + font.Size + 2), font with { Bold = true }, color);
		if (bars.Count == 0)
		{
			return;
		}

		float max = MathF.Max(1, bars.Max(bar => MathF.Abs(bar.Value)));
		float rowHeight = MathF.Max(font.Size * 1.8f, (destination.Height - font.Size - 8) / bars.Count);
		float labelWidth = MathF.Min(destination.Width * 0.35f, 120);
		for (int index = 0; index < bars.Count; index++)
		{
			RenderChartBar bar = bars[index];
			float y = destination.Y + font.Size + 8 + index * rowHeight;
			DrawText(bar.Label, new RenderPoint(destination.X + 4, y + font.Size), font, color);
			float width = MathF.Max(0, (destination.Width - labelWidth - 8) * MathF.Abs(bar.Value) / max);
			FillRectangle(new RenderRect(destination.X + labelWidth, y + 2, width, MathF.Max(2, font.Size)), color);
			DrawText(bar.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), new RenderPoint(destination.X + labelWidth + width + 4, y + font.Size), font, color);
		}
	}

	public void Dispose()
	{
		_disposed = true;
	}

	private static SKPaint CreatePaint(RenderColor color, SKPaintStyle style, float strokeWidth = 0)
	{
		return new SKPaint
		{
			Color = ToSkColor(color),
			Style = style,
			StrokeWidth = strokeWidth,
			IsAntialias = true
		};
	}

	private static SKColor ToSkColor(RenderColor color) => new(color.Red, color.Green, color.Blue, color.Alpha);

	private static SKRect ToSkRect(RenderRect rectangle) => new(rectangle.X, rectangle.Y, rectangle.Right, rectangle.Bottom);

	private static void ValidateUrl(string url)
	{
		if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? parsed) || (parsed.IsAbsoluteUri && parsed.Scheme is not ("http" or "https" or "mailto")))
		{
			throw new ArgumentException("Only http, https, mailto, and relative URLs are supported.", nameof(url));
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
