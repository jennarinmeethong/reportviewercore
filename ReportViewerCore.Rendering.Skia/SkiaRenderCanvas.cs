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
		string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		if (normalized.Contains('\n'))
		{
			FontMetrics metrics = _fonts.GetMetrics(font);
			string[] lines = normalized.Split('\n');
			for (int index = 0; index < lines.Length; index++)
			{
				DrawText(lines[index], new RenderPoint(baseline.X, baseline.Y + index * metrics.LineHeight), font, color, direction);
			}
			return;
		}

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
		RenderUrlPolicy.ValidateHyperlink(url);
		DrawText(text, baseline, font, color, direction);
		ShapedText shaped = _shaper.Shape(text, font, direction);
		FontMetrics metrics = _fonts.GetMetrics(font);
		float width = MathF.Max(metrics.LineHeight, MathF.Abs(shaped.AdvanceX));
		float height = MathF.Max(metrics.LineHeight, MathF.Abs(shaped.AdvanceY));
		_canvas.DrawUrlAnnotation(new SKRect(baseline.X, baseline.Y - metrics.Ascent, baseline.X + width, baseline.Y - metrics.Ascent + height), url);
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
		DrawChart(RenderChartType.Bar, title, bars, destination, font, color);
	}

	public void DrawChart(RenderChartType chartType, string title, IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(points);
		DrawText(title, new RenderPoint(destination.X + 4, destination.Y + font.Size + 2), font with { Bold = true }, color);

		switch (chartType)
		{
			case RenderChartType.Bar:
				DrawBars(points, destination, font, color);
				break;
			case RenderChartType.Column:
				DrawColumns(points, destination, font, color);
				break;
			case RenderChartType.Line:
			case RenderChartType.Area:
				DrawLineChart(chartType == RenderChartType.Area, points, destination, font, color);
				break;
			case RenderChartType.Pie:
				DrawPieChart(points, destination, font, color);
				break;
			case RenderChartType.Doughnut:
				DrawDoughnutChart(points, destination, font, color);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(chartType), chartType, "Unknown chart type.");
		}
	}

	private void DrawColumns(IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		if (points.Count == 0)
		{
			return;
		}

		float plotTop = destination.Y + font.Size + 8;
		float plotBottom = destination.Bottom - font.Size - 18;
		float min = MathF.Min(0, points.Min(point => point.Value));
		float max = MathF.Max(1, points.Max(point => point.Value));
		float range = MathF.Max(1, max - min);
		float plotHeight = MathF.Max(1, plotBottom - plotTop);
		float baseline = plotBottom - (0 - min) / range * plotHeight;
		float slotWidth = (destination.Width - 8) / points.Count;
		float columnWidth = MathF.Max(2, slotWidth * 0.65f);
		for (int index = 0; index < points.Count; index++)
		{
			RenderChartBar point = points[index];
			float x = destination.X + 4 + index * slotWidth + (slotWidth - columnWidth) / 2;
			float valueY = plotBottom - (point.Value - min) / range * plotHeight;
			float y = MathF.Min(baseline, valueY);
			float height = MathF.Max(2, MathF.Abs(baseline - valueY));
			FillRectangle(new RenderRect(x, y, columnWidth, height), PieColor(color, index));
			DrawText(point.Label, new RenderPoint(x, destination.Bottom - font.Size), font, color);
			DrawText(point.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), new RenderPoint(x, y - 2), font, color);
		}
	}

	private void DrawBars(IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color)
	{
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

	private void DrawLineChart(bool area, IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		if (points.Count == 0)
		{
			return;
		}

		float plotTop = destination.Y + font.Size + 8;
		float plotBottom = destination.Bottom - font.Size - 4;
		float min = MathF.Min(0, points.Min(point => point.Value));
		float max = MathF.Max(1, points.Max(point => point.Value));
		float range = MathF.Max(1, max - min);
		float step = points.Count == 1 ? 0 : (destination.Width - 8) / (points.Count - 1);
		var coordinates = points.Select((point, index) => new RenderPoint(
			destination.X + 4 + index * step,
			plotBottom - (point.Value - min) / range * MathF.Max(1, plotBottom - plotTop))).ToArray();

		if (area)
		{
			using var path = new SKPath();
			path.MoveTo(coordinates[0].X, plotBottom);
			foreach (RenderPoint coordinate in coordinates)
			{
				path.LineTo(coordinate.X, coordinate.Y);
			}
			path.LineTo(coordinates[^1].X, plotBottom);
			path.Close();
			using var paint = CreatePaint(color with { Alpha = Math.Min(color.Alpha, (byte)96) }, SKPaintStyle.Fill);
			_canvas.DrawPath(path, paint);
		}

		using (var paint = CreatePaint(color, SKPaintStyle.Stroke, MathF.Max(1, font.Size / 10)))
		{
			for (int index = 1; index < coordinates.Length; index++)
			{
				_canvas.DrawLine(coordinates[index - 1].X, coordinates[index - 1].Y, coordinates[index].X, coordinates[index].Y, paint);
			}
		}

		for (int index = 0; index < points.Count; index++)
		{
			RenderChartBar point = points[index];
			DrawText(point.Label, new RenderPoint(coordinates[index].X - font.Size, plotBottom + font.Size), font, color);
		}
	}

	private void DrawPieChart(IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		float total = points.Sum(point => MathF.Max(0, point.Value));
		if (total <= 0)
		{
			return;
		}

		float diameter = MathF.Min(destination.Width * 0.58f, destination.Height - font.Size - 8);
		var bounds = new SKRect(destination.X + 4, destination.Y + font.Size + 8, destination.X + 4 + diameter, destination.Y + font.Size + 8 + diameter);
		float startAngle = -90;
		for (int index = 0; index < points.Count; index++)
		{
			float sweep = MathF.Max(0, points[index].Value) / total * 360;
			using var paint = CreatePaint(PieColor(color, index), SKPaintStyle.Fill);
			_canvas.DrawArc(bounds, startAngle, sweep, true, paint);
			startAngle += sweep;
			DrawText(points[index].Label, new RenderPoint(destination.X + diameter + 12, destination.Y + font.Size + 16 + index * (font.Size * 1.4f)), font, color);
		}
	}

	private void DrawDoughnutChart(IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		float total = points.Sum(point => MathF.Max(0, point.Value));
		if (total <= 0)
		{
			return;
		}

		float diameter = MathF.Min(destination.Width * 0.58f, destination.Height - font.Size - 8);
		var bounds = new SKRect(destination.X + 4, destination.Y + font.Size + 8, destination.X + 4 + diameter, destination.Y + font.Size + 8 + diameter);
		float startAngle = -90;
		for (int index = 0; index < points.Count; index++)
		{
			float sweep = MathF.Max(0, points[index].Value) / total * 360;
			using var paint = CreatePaint(PieColor(color, index), SKPaintStyle.Fill);
			_canvas.DrawArc(bounds, startAngle, sweep, true, paint);
			DrawText(points[index].Label, new RenderPoint(destination.X + diameter + 12, destination.Y + font.Size + 16 + index * (font.Size * 1.4f)), font, color);
			startAngle += sweep;
		}

		float holeDiameter = diameter * 0.46f;
		float holeLeft = bounds.Left + (diameter - holeDiameter) / 2;
		float holeTop = bounds.Top + (diameter - holeDiameter) / 2;
		using var holePaint = CreatePaint(RenderColor.White, SKPaintStyle.Fill);
		_canvas.DrawOval(new SKRect(holeLeft, holeTop, holeLeft + holeDiameter, holeTop + holeDiameter), holePaint);
	}

	private static RenderColor PieColor(RenderColor baseColor, int index)
	{
		RenderColor[] palette =
		{
			baseColor,
			new RenderColor(52, 152, 219, baseColor.Alpha),
			new RenderColor(46, 204, 113, baseColor.Alpha),
			new RenderColor(241, 196, 15, baseColor.Alpha),
			new RenderColor(231, 76, 60, baseColor.Alpha)
		};
		return palette[index % palette.Length];
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

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
