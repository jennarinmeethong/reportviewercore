namespace ReportViewerCore.Rendering;

public interface IFontResolver
{
	FontMetrics GetMetrics(FontRequest request);
}

public interface ITextShaper
{
	ShapedText Shape(string text, FontRequest request, TextDirection direction = TextDirection.LeftToRight);
}

public interface IImageCodec
{
	RenderImage Decode(ReadOnlyMemory<byte> encodedImage);

	ReadOnlyMemory<byte> EncodePng(RenderImage image);
}

public interface IImageResolver
{
	RenderImage? Resolve(RenderImageRequest request);
}

public interface IRenderCanvas : IDisposable
{
	RenderSize Size { get; }

	void Clear(RenderColor color);

	void FillRectangle(RenderRect rectangle, RenderColor color);

	void DrawRectangle(RenderRect rectangle, RenderColor color, float strokeWidth);

	void DrawLine(RenderPoint start, RenderPoint end, RenderColor color, float strokeWidth);

	void DrawText(string text, RenderPoint baseline, FontRequest font, RenderColor color, TextDirection direction = TextDirection.LeftToRight);

	void DrawTableCell(string text, RenderPoint baseline, RenderRect bounds, FontRequest font, RenderColor color, string? url = null, TextDirection direction = TextDirection.LeftToRight, int columnSpan = 1, int rowSpan = 1)
	{
		if (url is null)
		{
			DrawText(text, baseline, font, color, direction);
		}
		else
		{
			DrawHyperlink(text, baseline, font, color, url, direction);
		}
	}

	void DrawHyperlink(string text, RenderPoint baseline, FontRequest font, RenderColor color, string url, TextDirection direction = TextDirection.LeftToRight);

	void DrawImage(RenderImage image, RenderRect destination);

	void DrawBarChart(string title, IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color);

	void DrawChart(RenderChartType chartType, string title, IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color)
	{
		if (chartType == RenderChartType.Bar)
		{
			DrawBarChart(title, points, destination, font, color);
			return;
		}

		throw new NotSupportedException($"The renderer does not support {chartType} charts.");
	}
}

public interface IRenderDocument : IDisposable
{
	IRenderCanvas BeginPage(RenderSize size);

	void EndPage();

	void Complete();
}

public interface IReportRenderer
{
	ReportOutputFormat Format { get; }

	ReportOutput Render(ReportDocument document, ReportRenderOptions options);
}
