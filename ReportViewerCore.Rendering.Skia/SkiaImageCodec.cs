using ReportViewerCore.Rendering;
using SkiaSharp;

namespace ReportViewerCore.Rendering.Skia;

public sealed class SkiaImageCodec : IImageCodec
{
	public RenderImage Decode(ReadOnlyMemory<byte> encodedImage)
	{
		using SKData data = SKData.CreateCopy(encodedImage.Span);
		using SKImage? image = SKImage.FromEncodedData(data);
		if (image is null)
		{
			throw new UnsupportedImageException();
		}

		using SKData? png = image.Encode(SKEncodedImageFormat.Png, 100);
		if (png is null)
		{
			throw new UnsupportedImageException();
		}

		return new RenderImage(image.Width, image.Height, png.ToArray());
	}

	public ReadOnlyMemory<byte> EncodePng(RenderImage image)
	{
		ArgumentNullException.ThrowIfNull(image);
		return image.PngData;
	}
}

public sealed class SkiaImageResolver : IImageResolver
{
	private readonly IImageCodec _codec;

	public SkiaImageResolver(IImageCodec? codec = null)
	{
		_codec = codec ?? new SkiaImageCodec();
	}

	public RenderImage? Resolve(RenderImageRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.EncodedData.IsEmpty)
		{
			return null;
		}

		return _codec.Decode(request.EncodedData);
	}
}
