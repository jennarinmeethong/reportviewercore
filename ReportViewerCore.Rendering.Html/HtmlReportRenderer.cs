using System.Globalization;
using System.Net;
using System.Text;
using ReportViewerCore.Rendering;

namespace ReportViewerCore.Rendering.Html;

public sealed class HtmlReportRenderer : IReportRenderer
{
	public ReportOutputFormat Format => ReportOutputFormat.Html;

	public ReportOutput Render(ReportDocument document, ReportRenderOptions options)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(options);
		if (options.Format != Format)
		{
			throw new ArgumentException($"This renderer only supports {Format}.", nameof(options));
		}

		var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Report</title><style>body{margin:0;background:#e5e7eb}.report-page{margin:16px auto;background:#fff;box-shadow:0 1px 4px #0003;overflow:hidden}.report-page svg{display:block}</style></head><body>");
		foreach (ReportPage page in document.Pages)
		{
			using var canvas = new HtmlRenderCanvas(page.Size);
			page.Render(canvas);
			html.Append("<section class=\"report-page\" style=\"width:")
				.Append(FormatNumber(page.Size.Width))
				.Append("px;height:")
				.Append(FormatNumber(page.Size.Height))
				.Append("px\"><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
				.Append(FormatNumber(page.Size.Width))
				.Append("\" height=\"")
				.Append(FormatNumber(page.Size.Height))
				.Append("\" viewBox=\"0 0 ")
				.Append(FormatNumber(page.Size.Width))
				.Append(' ')
				.Append(FormatNumber(page.Size.Height))
				.Append("\">")
				.Append(canvas.Markup)
				.Append("</svg></section>");
		}

		html.Append("</body></html>");
		return new ReportOutput(Format, "text/html; charset=utf-8", "html", Encoding.UTF8.GetBytes(html.ToString()));
	}

	private static string FormatNumber(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal sealed class HtmlRenderCanvas : IRenderCanvas
{
	private readonly StringBuilder _markup = new();
	private bool _disposed;

	public HtmlRenderCanvas(RenderSize size)
	{
		Size = size;
	}

	public RenderSize Size { get; }

	public string Markup
	{
		get
		{
			ThrowIfDisposed();
			return _markup.ToString();
		}
	}

	public void Clear(RenderColor color)
	{
		FillRectangle(new RenderRect(0, 0, Size.Width, Size.Height), color);
	}

	public void FillRectangle(RenderRect rectangle, RenderColor color)
	{
		AppendRectangle(rectangle, color, null);
	}

	public void DrawRectangle(RenderRect rectangle, RenderColor color, float strokeWidth)
	{
		AppendRectangle(rectangle, null, $"stroke=\"{Color(color)}\" stroke-opacity=\"{Opacity(color)}\" stroke-width=\"{Number(strokeWidth)}\" fill=\"none\"");
	}

	public void DrawLine(RenderPoint start, RenderPoint end, RenderColor color, float strokeWidth)
	{
		ThrowIfDisposed();
		_markup.Append("<line x1=\"").Append(Number(start.X)).Append("\" y1=\"").Append(Number(start.Y)).Append("\" x2=\"").Append(Number(end.X)).Append("\" y2=\"").Append(Number(end.Y)).Append("\" stroke=\"").Append(Color(color)).Append("\" stroke-opacity=\"").Append(Opacity(color)).Append("\" stroke-width=\"").Append(Number(strokeWidth)).Append("\"/>");
	}

	public void DrawText(string text, RenderPoint baseline, FontRequest font, RenderColor color, TextDirection direction = TextDirection.LeftToRight)
	{
		AppendText(text, baseline, font, color, direction, null);
	}

	public void DrawHyperlink(string text, RenderPoint baseline, FontRequest font, RenderColor color, string url, TextDirection direction = TextDirection.LeftToRight)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(url);
		RenderUrlPolicy.ValidateHyperlink(url);
		AppendText(text, baseline, font, color, direction, url);
	}

	public void DrawImage(RenderImage image, RenderRect destination)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(image);
		string data = Convert.ToBase64String(image.PngData.Span);
		_markup.Append("<image x=\"").Append(Number(destination.X)).Append("\" y=\"").Append(Number(destination.Y)).Append("\" width=\"").Append(Number(destination.Width)).Append("\" height=\"").Append(Number(destination.Height)).Append("\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,").Append(data).Append("\"/>");
	}

	public void DrawBarChart(string title, IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(bars);
		AppendText(title, new RenderPoint(destination.X + 4, destination.Y + font.Size + 2), font with { Bold = true }, color, TextDirection.LeftToRight, null);
		float max = MathF.Max(1, bars.Count == 0 ? 1 : bars.Max(bar => MathF.Abs(bar.Value)));
		float rowHeight = MathF.Max(font.Size * 1.8f, (destination.Height - font.Size - 8) / MathF.Max(1, bars.Count));
		float labelWidth = MathF.Min(destination.Width * 0.35f, 120);
		for (int index = 0; index < bars.Count; index++)
		{
			RenderChartBar bar = bars[index];
			float y = destination.Y + font.Size + 8 + index * rowHeight;
			AppendText(bar.Label, new RenderPoint(destination.X + 4, y + font.Size), font, color, TextDirection.LeftToRight, null);
			float width = MathF.Max(0, (destination.Width - labelWidth - 8) * MathF.Abs(bar.Value) / max);
			FillRectangle(new RenderRect(destination.X + labelWidth, y + 2, width, MathF.Max(2, font.Size)), color);
			AppendText(bar.Value.ToString("0.##", CultureInfo.InvariantCulture), new RenderPoint(destination.X + labelWidth + width + 4, y + font.Size), font, color, TextDirection.LeftToRight, null);
		}
	}

	public void Dispose()
	{
		_disposed = true;
	}

	private void AppendRectangle(RenderRect rectangle, RenderColor? fill, string? attributes)
	{
		ThrowIfDisposed();
		_markup.Append("<rect x=\"").Append(Number(rectangle.X)).Append("\" y=\"").Append(Number(rectangle.Y)).Append("\" width=\"").Append(Number(rectangle.Width)).Append("\" height=\"").Append(Number(rectangle.Height)).Append("\"");
		if (fill is RenderColor fillColor)
		{
			_markup.Append(" fill=\"").Append(Color(fillColor)).Append("\" fill-opacity=\"").Append(Opacity(fillColor)).Append("\"");
		}
		if (attributes is not null)
		{
			_markup.Append(' ').Append(attributes);
		}
		_markup.Append("/>");
	}

	private void AppendText(string text, RenderPoint baseline, FontRequest font, RenderColor color, TextDirection direction, string? url)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);
		var element = new StringBuilder("<text x=\"").Append(Number(baseline.X)).Append("\" y=\"").Append(Number(baseline.Y)).Append("\" font-family=\"").Append(WebUtility.HtmlEncode(font.Family)).Append("\" font-size=\"").Append(Number(font.Size)).Append("px\" fill=\"").Append(Color(color)).Append("\" fill-opacity=\"").Append(Opacity(color)).Append("\"");
		if (font.Bold)
		{
			element.Append(" font-weight=\"700\"");
		}
		if (font.Italic)
		{
			element.Append(" font-style=\"italic\"");
		}
		if (direction == TextDirection.RightToLeft)
		{
			element.Append(" direction=\"rtl\" unicode-bidi=\"plaintext\"");
		}
		else if (direction is TextDirection.TopToBottom or TextDirection.BottomToTop)
		{
			element.Append(" writing-mode=\"tb\"");
		}
		element.Append('>').Append(WebUtility.HtmlEncode(text)).Append("</text>");
		if (url is not null)
		{
			_markup.Append("<a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\">").Append(element).Append("</a>");
		}
		else
		{
			_markup.Append(element);
		}
	}

	private static string Color(RenderColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

	private static string Opacity(RenderColor color) => (color.Alpha / 255f).ToString("0.###", CultureInfo.InvariantCulture);

	private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
