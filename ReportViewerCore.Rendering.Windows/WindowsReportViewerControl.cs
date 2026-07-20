using ReportViewerCore.Rendering;
using ReportViewerCore.Rendering.Skia;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ReportViewerCore.Rendering.Windows;

/// <summary>
/// Windows-only WinForms adapter over the same backend-neutral ReportDocument used by every RID.
/// The control owns display and page navigation; SkiaSharp owns report rendering.
/// </summary>
public class WindowsReportViewerControl : UserControl
{
	private readonly PictureBox _page = new()
	{
		BackColor = Color.White,
		SizeMode = PictureBoxSizeMode.AutoSize,
		TabStop = false
	};
	private ReportDocument? _document;
	private int _pageIndex;

	public WindowsReportViewerControl()
	{
		AutoScroll = true;
		BackColor = Color.FromArgb(229, 231, 235);
		Controls.Add(_page);
	}

	[Browsable(false)]
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public ReportDocument? Document
	{
		get => _document;
		set
		{
			_document = value;
			_pageIndex = 0;
			RenderCurrentPage();
		}
	}

	[Browsable(false)]
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public int PageIndex
	{
		get => _pageIndex;
		set
		{
			int pageCount = _document?.Pages.Count ?? 0;
			if (value < 0 || value >= pageCount)
			{
				throw new ArgumentOutOfRangeException(nameof(value));
			}

			_pageIndex = value;
			RenderCurrentPage();
		}
	}

	public int PageCount => _document?.Pages.Count ?? 0;

	public void NextPage()
	{
		if (_pageIndex + 1 < PageCount)
		{
			PageIndex++;
		}
	}

	public void PreviousPage()
	{
		if (_pageIndex > 0)
		{
			PageIndex--;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Image? image = _page.Image;
			_page.Image = null;
			image?.Dispose();
			_page.Dispose();
		}

		base.Dispose(disposing);
	}

	private void RenderCurrentPage()
	{
		if (_document is null)
		{
			ReplaceImage(null);
			return;
		}

		ReportPage page = _document.Pages[_pageIndex];
		using var renderer = new SkiaBitmapRenderer();
		RenderImage rendered = renderer.Render(page.Size, page.Render);
		using var stream = new MemoryStream(rendered.PngData.ToArray(), writable: false);
		using Image decoded = Image.FromStream(stream);
		ReplaceImage(new Bitmap(decoded));
	}

	private void ReplaceImage(Image? image)
	{
		Image? previous = _page.Image;
		_page.Image = image;
		previous?.Dispose();
		if (image is null)
		{
			_page.Size = ClientSize;
			AutoScrollMinSize = Size.Empty;
			return;
		}

		_page.Size = image.Size;
		AutoScrollMinSize = image.Size;
	}
}
