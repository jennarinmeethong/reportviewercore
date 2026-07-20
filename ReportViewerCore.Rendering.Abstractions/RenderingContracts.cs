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

	void DrawHyperlink(string text, RenderPoint baseline, FontRequest font, RenderColor color, string url, TextDirection direction = TextDirection.LeftToRight);

	void DrawImage(RenderImage image, RenderRect destination);

	void DrawBarChart(string title, IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color);
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
