using System.Net.Http.Headers;
using ReportViewerCore.Rendering;

namespace ReportViewerCore.Headless;

public interface IReportServerAuthenticator
{
	ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}

public sealed class HttpReportServerTransport : IReportServerTransport
{
	private readonly HttpClient _httpClient;
	private readonly IReportServerAuthenticator? _authenticator;

	public HttpReportServerTransport(HttpClient httpClient, IReportServerAuthenticator? authenticator = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_authenticator = authenticator;
	}

	public async Task<ReportOutput> RenderAsync(ReportServerRenderRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		using var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(request));
		message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypeFor(request.Format)));
		if (_authenticator is not null)
		{
			await _authenticator.AuthenticateAsync(message, cancellationToken).ConfigureAwait(false);
		}

		using HttpResponseMessage response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		string mimeType = response.Content.Headers.ContentType?.ToString() ?? MimeTypeFor(request.Format);
		return new ReportOutput(request.Format, mimeType, ExtensionFor(request.Format), data);
	}

	private static Uri BuildUri(ReportServerRenderRequest request)
	{
		if (!request.Endpoint.IsAbsoluteUri || request.Endpoint.Scheme is not ("http" or "https"))
		{
			throw new ArgumentException("The report server endpoint must be an absolute HTTP(S) URI.", nameof(request));
		}

		var parameters = new List<string>
		{
			EscapeQueryPath(request.ReportPath),
			"rs%3ACommand=Render",
			$"rs%3AFormat={Uri.EscapeDataString(FormatName(request.Format))}"
		};
		if (!string.IsNullOrWhiteSpace(request.DeviceInfo))
		{
			parameters.Add($"rc%3ADeviceInfo={Uri.EscapeDataString(request.DeviceInfo)}");
		}
		if (request.Parameters is not null)
		{
			foreach ((string name, string value) in request.Parameters.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
			{
				parameters.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value ?? string.Empty)}");
			}
		}

		string endpoint = request.Endpoint.GetLeftPart(UriPartial.Path);
		string separator = string.IsNullOrEmpty(request.Endpoint.Query) ? "?" : "&";
		return new Uri(endpoint + separator + string.Join('&', parameters), UriKind.Absolute);
	}

	private static string EscapeQueryPath(string value) => Uri.EscapeDataString(value).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

	private static string FormatName(ReportOutputFormat format) => format switch
	{
		ReportOutputFormat.Pdf => "PDF",
		ReportOutputFormat.Html => "HTML5",
		ReportOutputFormat.ExcelOpenXml => "EXCELOPENXML",
		ReportOutputFormat.WordOpenXml => "WORDOPENXML",
		_ => throw new ArgumentOutOfRangeException(nameof(format), format, "The report server transport does not support this format.")
	};

	private static string MimeTypeFor(ReportOutputFormat format) => format switch
	{
		ReportOutputFormat.Pdf => "application/pdf",
		ReportOutputFormat.Html => "text/html",
		ReportOutputFormat.ExcelOpenXml => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
		ReportOutputFormat.WordOpenXml => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
		_ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
	};

	private static string ExtensionFor(ReportOutputFormat format) => format switch
	{
		ReportOutputFormat.Pdf => "pdf",
		ReportOutputFormat.Html => "html",
		ReportOutputFormat.ExcelOpenXml => "xlsx",
		ReportOutputFormat.WordOpenXml => "docx",
		_ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
	};
}
