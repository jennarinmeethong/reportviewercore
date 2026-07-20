using System.Collections;
using ReportViewerCore.Engine;
using ReportViewerCore.Rendering;

namespace ReportViewerCore.Headless;

public sealed class LocalReport : IDisposable
{
	private readonly RdlcReportEngine _engine;
	private readonly HeadlessReportRenderer _renderer;
	private readonly IImageResolver? _imageResolver;
	private readonly IRdlcSubreportResolver? _subreportResolver;
	private byte[]? _definition;
	private IReadOnlyDictionary<string, IEnumerable> _dataSets = new Dictionary<string, IEnumerable>(StringComparer.OrdinalIgnoreCase);
	private IReadOnlyDictionary<string, object?> _parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

	public LocalReport(RdlcReportEngine engine, HeadlessReportRenderer renderer, IImageResolver? imageResolver = null, IRdlcSubreportResolver? subreportResolver = null)
	{
		_engine = engine ?? throw new ArgumentNullException(nameof(engine));
		_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		_imageResolver = imageResolver;
		_subreportResolver = subreportResolver;
	}

	public void LoadReportDefinition(Stream definition)
	{
		ArgumentNullException.ThrowIfNull(definition);
		using var copy = new MemoryStream();
		definition.CopyTo(copy);
		_definition = copy.ToArray();
	}

	public void SetDataSources(IReadOnlyDictionary<string, IEnumerable> dataSets)
	{
		ArgumentNullException.ThrowIfNull(dataSets);
		_dataSets = dataSets;
	}

	public void SetParameters(IReadOnlyDictionary<string, object?> parameters)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		_parameters = parameters;
	}

	public ReportDocument CreateDocument()
	{
		if (_definition is null)
		{
			throw new InvalidOperationException("Load an RDLC report definition before creating a document.");
		}

		using var definition = new MemoryStream(_definition, writable: false);
		return _engine.CreateDocument(definition, new RdlcDataContext(_dataSets, _parameters, _imageResolver, _subreportResolver));
	}

	public ReportOutput Render(ReportOutputFormat format, string? deviceInfo = null)
	{
		return _renderer.Render(CreateDocument(), new ReportRenderOptions(format, deviceInfo));
	}

	public void Dispose()
	{
		_definition = null;
	}
}
