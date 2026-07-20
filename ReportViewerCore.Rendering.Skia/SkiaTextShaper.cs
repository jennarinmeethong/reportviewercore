using HarfBuzzSharp;
using ReportViewerCore.Rendering;
using SkiaSharp;

namespace ReportViewerCore.Rendering.Skia;

public sealed class SkiaTextShaper : ITextShaper
{
	private readonly SkiaFontResolver _fonts;

	public SkiaTextShaper(SkiaFontResolver fonts)
	{
		_fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
	}

	public ShapedText Shape(string text, FontRequest request, TextDirection direction = TextDirection.LeftToRight)
	{
		ArgumentNullException.ThrowIfNull(text);

		using SkiaFont font = _fonts.Resolve(request);
		if (font.FontPath is null)
		{
			return ShapeWithSkia(text, font, direction);
		}

		using Blob blob = Blob.FromFile(font.FontPath);
		using Face face = new(blob, 0);
		using HarfBuzzSharp.Font hbFont = new(face);
		hbFont.SetScale((int)MathF.Round(request.Size * 64), (int)MathF.Round(request.Size * 64));

		using var buffer = new HarfBuzzSharp.Buffer();
		buffer.AddUtf16(text);
		buffer.Direction = ToHarfBuzzDirection(direction);
		buffer.GuessSegmentProperties();
		hbFont.Shape(buffer, Array.Empty<Feature>());

		GlyphInfo[] infos = buffer.GlyphInfos.ToArray();
		GlyphPosition[] positions = buffer.GlyphPositions.ToArray();
		var glyphs = new ShapedGlyph[infos.Length];
		float advanceX = 0;
		float advanceY = 0;

		for (int i = 0; i < infos.Length; i++)
		{
			GlyphPosition position = positions[i];
			float xAdvance = position.XAdvance / 64f;
			float yAdvance = position.YAdvance / 64f;
			glyphs[i] = new ShapedGlyph(
				infos[i].Codepoint,
				checked((int)infos[i].Cluster),
				xAdvance,
				yAdvance,
				position.XOffset / 64f,
				position.YOffset / 64f);
			advanceX += xAdvance;
			advanceY += yAdvance;
		}

		return new ShapedText(glyphs, advanceX, advanceY);
	}

	private static ShapedText ShapeWithSkia(string text, SkiaFont font, TextDirection direction)
	{
		using var paint = new SkiaSharp.SKPaint
		{
			Typeface = font.Typeface,
			TextSize = font.Font.Size
		};
		ushort[] glyphIds = new ushort[text.Length];
		font.Font.GetGlyphs(text, glyphIds);
		float advance = paint.MeasureText(text);
		if (direction is TextDirection.RightToLeft or TextDirection.BottomToTop)
		{
			Array.Reverse(glyphIds);
		}
		if (direction is TextDirection.TopToBottom or TextDirection.BottomToTop)
		{
			SKFontMetrics metrics = font.Font.Metrics;
			float lineHeight = MathF.Max(1, metrics.Descent - metrics.Ascent + metrics.Leading);
			float advanceY = direction == TextDirection.TopToBottom ? -lineHeight : lineHeight;
			var verticalGlyphs = glyphIds
				.Select((glyph, index) => new ShapedGlyph(glyph, index, 0, advanceY, 0, 0))
				.ToArray();
			return new ShapedText(verticalGlyphs, 0, advanceY * verticalGlyphs.Length);
		}

		var glyphs = glyphIds
			.Select((glyph, index) => new ShapedGlyph(glyph, index, 0, 0, 0, 0))
			.ToArray();
		return new ShapedText(glyphs, advance, 0);
	}

	private static Direction ToHarfBuzzDirection(TextDirection direction)
	{
		return direction switch
		{
			TextDirection.RightToLeft => Direction.RightToLeft,
			TextDirection.TopToBottom => Direction.TopToBottom,
			TextDirection.BottomToTop => Direction.BottomToTop,
			_ => Direction.LeftToRight
		};
	}
}
