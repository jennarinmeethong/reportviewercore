using ReportViewerCore.Rendering;

namespace ReportViewerCore.Engine;

/// <summary>
/// Supplies already-paginated pages to the portable rendering pipeline.
/// Legacy RPL/SPB adapters can implement this contract without exposing their
/// internal pagination types to the portable renderer packages.
/// </summary>
public interface IReportPageSource
{
	int PageCount { get; }

	RenderSize GetPageSize(int pageIndex);

	void RenderPage(int pageIndex, IRenderCanvas canvas);
}

public static class ReportPageSourceAdapter
{
	public static ReportDocument Adapt(IReportPageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (source.PageCount <= 0)
		{
			throw new InvalidDataException("The page source must contain at least one page.");
		}

		var pages = new List<ReportPage>(source.PageCount);
		for (int pageIndex = 0; pageIndex < source.PageCount; pageIndex++)
		{
			int currentPage = pageIndex;
			RenderSize size = source.GetPageSize(currentPage);
			if (size.Width <= 0 || size.Height <= 0)
			{
				throw new InvalidDataException($"The page source returned an invalid size for page {currentPage}.");
			}

			pages.Add(new ReportPage(size, canvas => source.RenderPage(currentPage, canvas)));
		}

		return new ReportDocument(pages);
	}
}
