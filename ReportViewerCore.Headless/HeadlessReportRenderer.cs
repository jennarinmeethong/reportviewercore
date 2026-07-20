using ReportViewerCore.Rendering;

namespace ReportViewerCore.Headless;

public sealed class HeadlessReportRenderer
{
	private readonly IReadOnlyDictionary<ReportOutputFormat, IReportRenderer> _renderers;

	public HeadlessReportRenderer(IEnumerable<IReportRenderer> renderers)
	{
		ArgumentNullException.ThrowIfNull(renderers);

		var registered = new Dictionary<ReportOutputFormat, IReportRenderer>();
		foreach (IReportRenderer renderer in renderers)
		{
			ArgumentNullException.ThrowIfNull(renderer);
			if (!registered.TryAdd(renderer.Format, renderer))
			{
				throw new ArgumentException($"A renderer for {renderer.Format} is already registered.", nameof(renderers));
			}
		}

		_renderers = registered;
	}

	public ReportOutput Render(ReportDocument document, ReportRenderOptions options)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(options);

		if (!_renderers.TryGetValue(options.Format, out IReportRenderer? renderer))
		{
			throw new NotSupportedException($"No renderer is registered for {options.Format}.");
		}

		return renderer.Render(document, options);
	}
}
