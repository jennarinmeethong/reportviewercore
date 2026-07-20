namespace ReportViewerCore.Rendering;

public sealed class FontNotFoundException : InvalidOperationException
{
	public FontNotFoundException(string family)
		: base($"The requested font family '{family}' was not found.")
	{
	}
}

public sealed class UnsupportedImageException : InvalidOperationException
{
	public UnsupportedImageException()
		: base("The image format is not supported by the configured rendering backend.")
	{
	}
}
