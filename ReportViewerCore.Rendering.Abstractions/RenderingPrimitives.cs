namespace ReportViewerCore.Rendering;

public readonly record struct RenderColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
	public static RenderColor Black => new(0, 0, 0);
	public static RenderColor White => new(255, 255, 255);
}

public readonly record struct RenderPoint(float X, float Y);

public readonly record struct RenderSize(float Width, float Height);

public readonly record struct RenderRect(float X, float Y, float Width, float Height)
{
	public float Right => X + Width;
	public float Bottom => Y + Height;
}

public readonly record struct FontRequest(
	string Family,
	float Size,
	bool Bold = false,
	bool Italic = false);

public enum TextDirection
{
	LeftToRight,
	RightToLeft,
	TopToBottom,
	BottomToTop
}

public readonly record struct FontMetrics(
	float Ascent,
	float Descent,
	float Leading,
	float LineHeight);

public readonly record struct ShapedGlyph(
	uint GlyphId,
	int Cluster,
	float AdvanceX,
	float AdvanceY,
	float OffsetX,
	float OffsetY);

public sealed record ShapedText(
	IReadOnlyList<ShapedGlyph> Glyphs,
	float AdvanceX,
	float AdvanceY);

public sealed record RenderImage(
	int Width,
	int Height,
	ReadOnlyMemory<byte> PngData);

public sealed record RenderImageRequest(
	string Source,
	string Value,
	string? MimeType,
	ReadOnlyMemory<byte> EncodedData);

public sealed record RenderChartBar(string Label, float Value);
