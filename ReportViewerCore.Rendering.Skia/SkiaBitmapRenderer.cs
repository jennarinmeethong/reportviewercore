using ReportViewerCore.Rendering;
using SkiaSharp;

namespace ReportViewerCore.Rendering.Skia;

public sealed class SkiaBitmapRenderer : IDisposable
{
	private readonly SkiaFontResolver _fonts;
	private readonly SkiaImageCodec _images = new();

	public SkiaBitmapRenderer(SkiaFontResolver? fonts = null)
	{
		_fonts = fonts ?? new SkiaFontResolver();
	}

	public RenderImage Render(RenderSize size, Action<IRenderCanvas> draw)
	{
		ArgumentNullException.ThrowIfNull(draw);
		if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size), "Bitmap dimensions must be finite and greater than zero.");
		}

		int width = checked((int)MathF.Ceiling(size.Width));
		int height = checked((int)MathF.Ceiling(size.Height));
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size), "Bitmap dimensions must be greater than zero.");
		}

		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using SKSurface surface = SKSurface.Create(info) ?? throw new InvalidOperationException("SkiaSharp could not create a bitmap surface.");
		using var canvas = new SkiaRenderCanvas(surface.Canvas, size, _fonts);
		draw(canvas);
		surface.Canvas.Flush();

		using SKImage image = surface.Snapshot();
		using SKData? png = image.Encode(SKEncodedImageFormat.Png, 100);
		if (png is null)
		{
			throw new UnsupportedImageException();
		}

		return _images.Decode(png.ToArray());
	}

	public void Dispose()
	{
		_fonts.Dispose();
	}
}
