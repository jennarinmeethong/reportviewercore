using ReportViewerCore.Rendering;

namespace ReportViewerCore.Headless;

public sealed record ReportServerRenderRequest(
	Uri Endpoint,
	string ReportPath,
	ReportOutputFormat Format,
	string? DeviceInfo = null,
	IReadOnlyDictionary<string, string>? Parameters = null);

public interface IReportServerTransport
{
	Task<ReportOutput> RenderAsync(ReportServerRenderRequest request, CancellationToken cancellationToken = default);
}

public sealed class ServerReport
{
	private readonly IReportServerTransport _transport;

	public ServerReport(IReportServerTransport transport, Uri endpoint, string reportPath)
	{
		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
		ReportPath = string.IsNullOrWhiteSpace(reportPath) ? throw new ArgumentException("A report path is required.", nameof(reportPath)) : reportPath;
	}

	public Uri Endpoint { get; }

	public string ReportPath { get; }

	public Task<ReportOutput> RenderAsync(
		ReportOutputFormat format,
		string? deviceInfo = null,
		IReadOnlyDictionary<string, string>? parameters = null,
		CancellationToken cancellationToken = default)
	{
		var request = new ReportServerRenderRequest(Endpoint, ReportPath, format, deviceInfo, parameters);
		return _transport.RenderAsync(request, cancellationToken);
	}
}
