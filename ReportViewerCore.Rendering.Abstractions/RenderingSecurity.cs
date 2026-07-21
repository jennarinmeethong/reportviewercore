namespace ReportViewerCore.Rendering;

public static class RenderUrlPolicy
{
	public static void ValidateHyperlink(string url)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(url);
		if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? parsed)
			|| url.StartsWith("//", StringComparison.Ordinal)
			|| (parsed.IsAbsoluteUri && parsed.Scheme is not ("http" or "https" or "mailto")))
		{
			throw new ArgumentException("Only http, https, mailto, and relative URLs are supported.", nameof(url));
		}
	}
}
