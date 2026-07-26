using HarfBuzzSharp;
using ReportViewerCore.Rendering;
using SkiaSharp;

namespace ReportViewerCore.Rendering.Skia;

public sealed class SkiaFontResolver : IFontResolver, IDisposable
{
	private readonly SKFontManager _fontManager = SKFontManager.Default;
	private readonly Dictionary<string, RegisteredFont> _registeredFonts = new(StringComparer.OrdinalIgnoreCase);

	public void RegisterFont(string family, string fontFile)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(family);
		ArgumentException.ThrowIfNullOrWhiteSpace(fontFile);

		if (!File.Exists(fontFile))
		{
			throw new FileNotFoundException("The registered font file was not found.", fontFile);
		}

		using SKTypeface? typeface = SKTypeface.FromFile(fontFile);
		if (typeface is null)
		{
			throw new InvalidDataException($"The font file '{fontFile}' could not be loaded.");
		}

		_registeredFonts[family] = new RegisteredFont(fontFile);
	}

	public FontMetrics GetMetrics(FontRequest request)
	{
		using SkiaFont font = Resolve(request);
		SKFontMetrics metrics = font.Font.Metrics;
		return new FontMetrics(
			-metrics.Ascent,
			metrics.Descent,
			metrics.Leading,
			metrics.Descent - metrics.Ascent + metrics.Leading);
	}

	internal SkiaFont Resolve(FontRequest request)
	{
		if (!float.IsFinite(request.Size) || request.Size <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(request), "Font size must be finite and greater than zero.");
		}

		RegisteredFont? registered = null;
		_registeredFonts.TryGetValue(request.Family, out registered);
		SKTypeface? typeface = registered is null
			? _fontManager.MatchFamily(request.Family, CreateStyle(request))
			: SKTypeface.FromFile(registered.Path);

		if (typeface is null)
		{
			throw new FontNotFoundException(request.Family);
		}

		if (registered is null && !string.Equals(typeface.FamilyName, request.Family, StringComparison.OrdinalIgnoreCase))
		{
			typeface.Dispose();
			throw new FontNotFoundException(request.Family);
		}

		return new SkiaFont(typeface, request.Size, registered?.Path);
	}

	private static SKFontStyle CreateStyle(FontRequest request)
	{
		return new SKFontStyle(
			request.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
			SKFontStyleWidth.Normal,
			request.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
	}

	public void Dispose()
	{
	}

	private sealed record RegisteredFont(string Path);
}

internal sealed class SkiaFont : IDisposable
{
	private bool _disposed;

	internal SkiaFont(SKTypeface typeface, float size, string? fontPath)
	{
		Typeface = typeface;
		Font = new SKFont(typeface, size);
		FontPath = fontPath;
	}

	internal SKTypeface Typeface { get; }

	internal SKFont Font { get; }

	internal string? FontPath { get; }

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		Font.Dispose();
		Typeface.Dispose();
		_disposed = true;
	}

}
