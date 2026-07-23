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
		DrawChart(RenderChartType.Bar, title, bars, destination, font, color);
	}

	public void DrawChart(RenderChartType chartType, string title, IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(points);
		AppendText(title, new RenderPoint(destination.X + 4, destination.Y + font.Size + 2), font with { Bold = true }, color, TextDirection.LeftToRight, null);

		switch (chartType)
		{
			case RenderChartType.Bar:
				DrawBars(points, destination, font, color);
				break;
			case RenderChartType.Column:
				DrawColumns(points, destination, font, color);
				break;
			case RenderChartType.Line:
			case RenderChartType.Area:
				DrawLineChart(chartType == RenderChartType.Area, points, destination, font, color);
				break;
			case RenderChartType.Pie:
				DrawPieChart(points, destination, font, color);
				break;
			case RenderChartType.Doughnut:
				DrawDoughnutChart(points, destination, font, color);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(chartType), chartType, "Unknown chart type.");
		}
	}

	private void DrawColumns(IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		if (points.Count == 0)
		{
			return;
		}

		float plotTop = destination.Y + font.Size + 8;
		float plotBottom = destination.Bottom - font.Size - 18;
		float min = MathF.Min(0, points.Min(point => point.Value));
		float max = MathF.Max(1, points.Max(point => point.Value));
		float range = MathF.Max(1, max - min);
		float plotHeight = MathF.Max(1, plotBottom - plotTop);
		float baseline = plotBottom - (0 - min) / range * plotHeight;
		float slotWidth = (destination.Width - 8) / points.Count;
		float columnWidth = MathF.Max(2, slotWidth * 0.65f);
		FontRequest labelFont = font with { Size = MathF.Min(font.Size, 9) };
		for (int index = 0; index < points.Count; index++)
		{
			RenderChartBar point = points[index];
			float x = destination.X + 4 + index * slotWidth + (slotWidth - columnWidth) / 2;
			float valueY = plotBottom - (point.Value - min) / range * plotHeight;
			float y = MathF.Min(baseline, valueY);
			float height = MathF.Max(2, MathF.Abs(baseline - valueY));
			FillRectangle(new RenderRect(x, y, columnWidth, height), PieColor(color, index));
			AppendText(point.Label, new RenderPoint(CenterLabelX(point.Label, labelFont, x + columnWidth / 2, destination.X, destination.Right), destination.Bottom - labelFont.Size), labelFont, color, TextDirection.LeftToRight, null);
			AppendText(point.Value.ToString("0.##", CultureInfo.InvariantCulture), new RenderPoint(x, y - 2), font, color, TextDirection.LeftToRight, null);
		}
	}

	private void DrawBars(IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color)
	{
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

	private void DrawLineChart(bool area, IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		if (points.Count == 0)
		{
			return;
		}

		float plotTop = destination.Y + font.Size + 8;
		float plotBottom = destination.Bottom - font.Size - 4;
		float min = MathF.Min(0, points.Min(point => point.Value));
		float max = MathF.Max(1, points.Max(point => point.Value));
		float range = MathF.Max(1, max - min);
		float step = points.Count == 1 ? 0 : (destination.Width - 8) / (points.Count - 1);
		var coordinates = points.Select((point, index) => new RenderPoint(
			destination.X + 4 + index * step,
			plotBottom - (point.Value - min) / range * MathF.Max(1, plotBottom - plotTop))).ToArray();
		string linePoints = string.Join(' ', coordinates.Select(point => $"{Number(point.X)},{Number(point.Y)}"));
		if (area)
		{
			string polygonPoints = $"{Number(coordinates[0].X)},{Number(plotBottom)} {linePoints} {Number(coordinates[^1].X)},{Number(plotBottom)}";
			_markup.Append("<polygon points=\"").Append(polygonPoints).Append("\" fill=\"").Append(Color(color)).Append("\" fill-opacity=\"0.35\" stroke=\"none\"/>");
		}
		_markup.Append("<polyline points=\"").Append(linePoints).Append("\" fill=\"none\" stroke=\"").Append(Color(color)).Append("\" stroke-width=\"").Append(Number(MathF.Max(1, font.Size / 10))).Append("\"/>");
		FontRequest labelFont = font with { Size = MathF.Min(font.Size, 9) };
		for (int index = 0; index < points.Count; index++)
		{
			AppendText(points[index].Label, new RenderPoint(CenterLabelX(points[index].Label, labelFont, coordinates[index].X, destination.X, destination.Right), plotBottom + labelFont.Size), labelFont, color, TextDirection.LeftToRight, null);
		}
	}

	private static float CenterLabelX(string label, FontRequest font, float centerX, float left, float right)
	{
		float estimatedWidth = MathF.Max(font.Size, label.Length * font.Size * 0.7f);
		float maxX = MathF.Max(left, right - estimatedWidth);
		return Math.Clamp(centerX - estimatedWidth / 2, left, maxX);
	}

	private void DrawPieChart(IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		float total = points.Sum(point => MathF.Max(0, point.Value));
		if (total <= 0)
		{
			return;
		}

		float diameter = MathF.Min(destination.Width * 0.58f, destination.Height - font.Size - 8);
		float centerX = destination.X + 4 + diameter / 2;
		float centerY = destination.Y + font.Size + 8 + diameter / 2;
		float startAngle = -90;
		for (int index = 0; index < points.Count; index++)
		{
			float sweep = MathF.Max(0, points[index].Value) / total * 360;
			float endAngle = startAngle + sweep;
			RenderColor sliceColor = PieColor(color, index);
			if (sweep >= 359.99f)
			{
				_markup.Append("<circle cx=\"").Append(Number(centerX)).Append("\" cy=\"").Append(Number(centerY)).Append("\" r=\"").Append(Number(diameter / 2)).Append("\" fill=\"").Append(Color(sliceColor)).Append("\"/>");
			}
			else
			{
				string path = PiePath(centerX, centerY, diameter / 2, startAngle, endAngle);
				_markup.Append("<path d=\"").Append(path).Append("\" fill=\"").Append(Color(sliceColor)).Append("\"/>");
			}
			AppendText(points[index].Label, new RenderPoint(destination.X + diameter + 12, destination.Y + font.Size + 16 + index * (font.Size * 1.4f)), font, color, TextDirection.LeftToRight, null);
			startAngle = endAngle;
		}
	}

	private void DrawDoughnutChart(IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		float total = points.Sum(point => MathF.Max(0, point.Value));
		if (total <= 0)
		{
			return;
		}

		float diameter = MathF.Min(destination.Width * 0.58f, destination.Height - font.Size - 8);
		float centerX = destination.X + 4 + diameter / 2;
		float centerY = destination.Y + font.Size + 8 + diameter / 2;
		float startAngle = -90;
		float innerRadius = diameter * 0.23f;
		for (int index = 0; index < points.Count; index++)
		{
			float sweep = MathF.Max(0, points[index].Value) / total * 360;
			float endAngle = startAngle + sweep;
			RenderColor sliceColor = PieColor(color, index);
			if (sweep >= 359.99f)
			{
				_markup.Append("<circle cx=\"").Append(Number(centerX)).Append("\" cy=\"").Append(Number(centerY)).Append("\" r=\"").Append(Number(diameter / 2)).Append("\" fill=\"").Append(Color(sliceColor)).Append("\"/>");
			}
			else
			{
				string path = DoughnutPath(centerX, centerY, diameter / 2, innerRadius, startAngle, endAngle);
				_markup.Append("<path d=\"").Append(path).Append("\" fill=\"").Append(Color(sliceColor)).Append("\" fill-rule=\"evenodd\"/>");
			}
			AppendText(points[index].Label, new RenderPoint(destination.X + diameter + 12, destination.Y + font.Size + 16 + index * (font.Size * 1.4f)), font, color, TextDirection.LeftToRight, null);
			startAngle = endAngle;
		}
		_markup.Append("<circle cx=\"").Append(Number(centerX)).Append("\" cy=\"").Append(Number(centerY)).Append("\" r=\"").Append(Number(innerRadius)).Append("\" fill=\"white\"/>");
	}

	private static string DoughnutPath(float centerX, float centerY, float outerRadius, float innerRadius, float startAngle, float endAngle)
	{
		static RenderPoint Point(float x, float y, float r, float degrees)
		{
			float radians = degrees * MathF.PI / 180;
			return new RenderPoint(x + r * MathF.Cos(radians), y + r * MathF.Sin(radians));
		}

		RenderPoint outerStart = Point(centerX, centerY, outerRadius, startAngle);
		RenderPoint outerEnd = Point(centerX, centerY, outerRadius, endAngle);
		RenderPoint innerEnd = Point(centerX, centerY, innerRadius, endAngle);
		RenderPoint innerStart = Point(centerX, centerY, innerRadius, startAngle);
		int largeArc = endAngle - startAngle > 180 ? 1 : 0;
		return $"M {Number(outerStart.X)},{Number(outerStart.Y)} A {Number(outerRadius)},{Number(outerRadius)} 0 {largeArc} 1 {Number(outerEnd.X)},{Number(outerEnd.Y)} L {Number(innerEnd.X)},{Number(innerEnd.Y)} A {Number(innerRadius)},{Number(innerRadius)} 0 {largeArc} 0 {Number(innerStart.X)},{Number(innerStart.Y)} Z";
	}

	private static string PiePath(float centerX, float centerY, float radius, float startAngle, float endAngle)
	{
		static RenderPoint Point(float x, float y, float r, float degrees)
		{
			float radians = degrees * MathF.PI / 180;
			return new RenderPoint(x + r * MathF.Cos(radians), y + r * MathF.Sin(radians));
		}

		RenderPoint start = Point(centerX, centerY, radius, startAngle);
		RenderPoint end = Point(centerX, centerY, radius, endAngle);
		int largeArc = endAngle - startAngle > 180 ? 1 : 0;
		return $"M {Number(centerX)},{Number(centerY)} L {Number(start.X)},{Number(start.Y)} A {Number(radius)},{Number(radius)} 0 {largeArc} 1 {Number(end.X)},{Number(end.Y)} Z";
	}

	private static RenderColor PieColor(RenderColor baseColor, int index)
	{
		RenderColor[] palette =
		{
			baseColor,
			new RenderColor(52, 152, 219, baseColor.Alpha),
			new RenderColor(46, 204, 113, baseColor.Alpha),
			new RenderColor(241, 196, 15, baseColor.Alpha),
			new RenderColor(231, 76, 60, baseColor.Alpha)
		};
		return palette[index % palette.Length];
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
		element.Append('>');
		string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
		if (lines.Length == 1)
		{
			element.Append(WebUtility.HtmlEncode(lines[0]));
		}
		else
		{
			for (int index = 0; index < lines.Length; index++)
			{
				element.Append("<tspan x=\"").Append(Number(baseline.X)).Append("\" dy=\"").Append(Number(index == 0 ? 0 : font.Size * 1.2f)).Append("\">")
					.Append(WebUtility.HtmlEncode(lines[index])).Append("</tspan>");
			}
		}
		element.Append("</text>");
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
