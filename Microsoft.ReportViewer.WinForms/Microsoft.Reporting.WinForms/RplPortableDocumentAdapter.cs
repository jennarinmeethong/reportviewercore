#if NET10_0_OR_GREATER
using Microsoft.ReportingServices.Rendering.RPLProcessing;
using ReportViewerCore.Engine;
using ReportViewerCore.Rendering;
using ReportViewerCore.Rendering.Skia;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.Reporting.WinForms
{
	/// <summary>
	/// Converts an already-paginated v1 RPL stream into the backend-neutral v2
	/// document. This type is deliberately kept in the Windows compatibility
	/// assembly; portable packages never reference the legacy RPL object model.
	/// </summary>
	internal static class RplPortableDocumentAdapter
	{
		internal static ReportDocument Adapt(Stream rplStream)
		{
			ArgumentNullException.ThrowIfNull(rplStream);

			using (var reader = new BinaryReader(rplStream, Encoding.Unicode, true))
			{
				var report = new RPLReport(reader);
				try
				{
					var source = new RplPageSource(report, new SkiaImageCodec());
					return ReportPageSourceAdapter.Adapt(source);
				}
				finally
				{
					report.Release();
				}
			}
		}
	}

	internal sealed class RplPageSource : IReportPageSource
	{
		private const float PointsPerMillimeter = 72f / 25.4f;
		private readonly List<RplPage> _pages;

		internal RplPageSource(RPLReport report, IImageCodec imageCodec)
		{
			ArgumentNullException.ThrowIfNull(report);
			ArgumentNullException.ThrowIfNull(imageCodec);

			RPLPageContent[] paginatedPages = report.RPLPaginatedPages;
			if (paginatedPages == null || paginatedPages.Length == 0)
			{
				throw new InvalidDataException("The RPL report contains no paginated pages.");
			}

			_pages = new List<RplPage>(paginatedPages.Length);
			foreach (RPLPageContent page in paginatedPages)
			{
				_pages.Add(ReadPage(report, page, imageCodec));
			}
		}

		public int PageCount => _pages.Count;

		public RenderSize GetPageSize(int pageIndex)
		{
			return GetPage(pageIndex).Size;
		}

		public void RenderPage(int pageIndex, IRenderCanvas canvas)
		{
			ArgumentNullException.ThrowIfNull(canvas);
			RplPage page = GetPage(pageIndex);
			foreach (Action<IRenderCanvas> operation in page.Operations)
			{
				operation(canvas);
			}
		}

		private RplPage GetPage(int pageIndex)
		{
			if (pageIndex < 0 || pageIndex >= _pages.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(pageIndex));
			}

			return _pages[pageIndex];
		}

		private static RplPage ReadPage(RPLReport report, RPLPageContent page, IImageCodec imageCodec)
		{
			RPLPageLayout layout = page.PageLayout;
			float width = ToPoints(layout == null ? 0f : layout.PageWidth);
			float height = ToPoints(layout == null ? 0f : layout.PageHeight);
			if (width <= 0f)
			{
				width = ToPoints(page.MaxSectionWidth);
			}
			if (height <= 0f && page.ReportSectionSizes != null)
			{
				foreach (RPLSizes sectionSize in page.ReportSectionSizes)
				{
					height += ToPoints(sectionSize.Height);
				}
			}
			if (width <= 0f || height <= 0f)
			{
				throw new InvalidDataException("The RPL page has no usable page dimensions.");
			}

			var operations = new List<Action<IRenderCanvas>>();
			if (layout != null)
			{
				AddFillAndBorder(operations, layout.Style, new RenderRect(0f, 0f, width, height));
			}

			int sectionIndex = 0;
			float sectionTop = 0f;
			while (page.HasNextReportSection())
			{
				RPLReportSection section = page.GetNextReportSection();
				if (section == null || page.ReportSectionSizes == null || sectionIndex >= page.ReportSectionSizes.Length)
				{
					break;
				}

				RPLSizes sectionSize = page.ReportSectionSizes[sectionIndex++];
				float contentTop = sectionTop;
				if (section.Header != null)
				{
					RPLItemMeasurement header = section.Header;
					AddMeasurement(report, operations, header, contentTop, imageCodec);
					contentTop += ToPoints(header.Height);
				}

				if (section.Columns != null)
				{
					foreach (RPLItemMeasurement column in section.Columns)
					{
						if (column != null)
						{
							AddMeasurement(report, operations, column, contentTop, imageCodec);
						}
					}
					if (section.Columns.Length > 0 && section.Columns[0] != null)
					{
						contentTop += ToPoints(section.Columns[0].Height);
					}
				}

				if (section.Footer != null)
				{
					AddMeasurement(report, operations, section.Footer, contentTop, imageCodec);
				}
				sectionTop += sectionSize == null ? 0f : ToPoints(sectionSize.Height);
			}

			return new RplPage(new RenderSize(width, height), operations);
		}

		private static void AddMeasurement(RPLReport report, List<Action<IRenderCanvas>> operations, RPLItemMeasurement measurement, float parentTop, IImageCodec imageCodec)
		{
			RPLItem item = measurement.Element;
			if (item == null)
			{
				return;
			}

			var bounds = new RenderRect(
				ToPoints(measurement.Left),
				ToPoints(measurement.Top) + parentTop,
				ToPoints(measurement.Width),
				ToPoints(measurement.Height));
			AddItem(report, operations, item, bounds, imageCodec);
		}

		private static void AddItem(RPLReport report, List<Action<IRenderCanvas>> operations, RPLItem item, RenderRect bounds, IImageCodec imageCodec)
		{
			RPLElementProps properties = item.ElementProps;
			if (properties == null)
			{
				return;
			}

			AddFillAndBorder(operations, properties.Style, bounds);

			var container = item as RPLContainer;
			if (container != null)
			{
				if (container.Children != null)
				{
					foreach (RPLItemMeasurement child in container.Children)
					{
						if (child != null)
						{
							var childBounds = new RenderRect(
								bounds.X + ToPoints(child.Left),
								bounds.Y + ToPoints(child.Top),
								ToPoints(child.Width),
								ToPoints(child.Height));
							RPLItem childItem = child.Element;
							if (childItem != null)
							{
								AddItem(report, operations, childItem, childBounds, imageCodec);
							}
						}
					}
				}
			}
			else if (item is RPLTablix)
			{
				AddTablix(report, operations, (RPLTablix)item, bounds, imageCodec);
			}
			else if (item is RPLTextBox)
			{
				AddTextBox(operations, (RPLTextBox)item, properties, bounds);
			}
			else if (item is RPLImage)
			{
				AddImage(report, operations, ((RPLImageProps)properties).Image, bounds, imageCodec);
			}
			else if (item is RPLChart || item is RPLGaugePanel || item is RPLMap)
			{
				AddDynamicImage(report, operations, (RPLDynamicImageProps)properties, bounds, imageCodec);
			}
			else if (item is RPLLine)
			{
				RenderColor color = GetColor(properties.Style, 27, RenderColor.Black);
				float strokeWidth = GetSizeInPoints(properties.Style, 10, 1f);
				operations.Add(canvas => canvas.DrawLine(new RenderPoint(bounds.X, bounds.Y), new RenderPoint(bounds.Right, bounds.Bottom), color, strokeWidth));
			}

			AddBorder(operations, properties.Style, bounds);
		}

		private static void AddTablix(RPLReport report, List<Action<IRenderCanvas>> operations, RPLTablix tablix, RenderRect bounds, IImageCodec imageCodec)
		{
			int rowNumber = 0;
			RPLTablixRow row;
			while ((row = tablix.GetNextRow()) != null)
			{
				if (row is RPLTablixOmittedRow)
				{
					continue;
				}

				foreach (RPLTablixCell cell in row.RowCells ?? new List<RPLTablixCell>())
				{
					if (cell == null || cell.Element == null || tablix.ColumnWidths == null || tablix.RowHeights == null)
					{
						continue;
					}

					int column = Math.Max(0, cell.ColIndex);
					int rowIndex = Math.Max(0, rowNumber);
					if (column >= tablix.ColumnWidths.Length || rowIndex >= tablix.RowHeights.Length)
					{
						continue;
					}

					int columnSpan = Math.Max(1, cell.ColSpan);
					int rowSpan = Math.Max(1, cell.RowSpan);
					columnSpan = Math.Min(columnSpan, tablix.ColumnWidths.Length - column);
					rowSpan = Math.Min(rowSpan, tablix.RowHeights.Length - rowIndex);
					float x = bounds.X + ToPoints(Sum(tablix.ColumnWidths, column));
					float y = bounds.Y + ToPoints(Sum(tablix.RowHeights, rowIndex));
					var cellBounds = new RenderRect(x, y, ToPoints(tablix.GetColumnWidth(column, columnSpan)), ToPoints(tablix.GetRowHeight(rowIndex, rowSpan)));
					AddItem(report, operations, cell.Element, cellBounds, imageCodec);
				}

				rowNumber++;
			}
		}

		private static void AddTextBox(List<Action<IRenderCanvas>> operations, RPLTextBox textBox, RPLElementProps properties, RenderRect bounds)
		{
			var textBoxProperties = properties as RPLTextBoxProps;
			var definition = textBox.ElementPropsDef as RPLTextBoxPropsDef;
			string text = textBoxProperties == null ? null : textBoxProperties.Value;
			if (string.IsNullOrEmpty(text) && definition != null)
			{
				text = definition.Value;
			}

			string hyperlink = GetHyperlink(textBoxProperties == null ? null : textBoxProperties.ActionInfo);
			if (string.IsNullOrEmpty(text) && definition != null && !definition.IsSimple)
			{
				var lines = new List<string>();
				RPLParagraph paragraph;
				while ((paragraph = textBox.GetNextParagraph()) != null)
				{
					var runs = new List<string>();
					RPLTextRun run;
					while ((run = paragraph.GetNextTextRun()) != null)
					{
						RPLTextRunProps runProperties = run.ElementProps as RPLTextRunProps;
						RPLTextRunPropsDef runDefinition = run.ElementPropsDef as RPLTextRunPropsDef;
						string value = runProperties == null ? null : runProperties.Value;
						if (string.IsNullOrEmpty(value) && runDefinition != null)
						{
							value = runDefinition.Value;
						}
						runs.Add(value ?? string.Empty);
						if (string.IsNullOrEmpty(hyperlink) && runProperties != null)
						{
							hyperlink = GetHyperlink(runProperties.ActionInfo);
						}
					}
					lines.Add(string.Concat(runs));
				}
				text = string.Join(Environment.NewLine, lines);
			}

			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			FontRequest font = GetFont(properties.Style);
			RenderColor color = GetColor(properties.Style, 27, RenderColor.Black);
			TextDirection direction = GetTextDirection(properties.Style);
			float leftPadding = GetSizeInPoints(properties.Style, 15, 0f);
			float topPadding = GetSizeInPoints(properties.Style, 17, 0f);
			float lineHeight = GetSizeInPoints(properties.Style, 28, font.Size * 1.2f);
			string safeHyperlink = IsSafeHyperlink(hyperlink) ? hyperlink : null;
			string[] linesToRender = text.Replace("\r\n", "\n").Split('\n');
			for (int index = 0; index < linesToRender.Length; index++)
			{
				string line = linesToRender[index];
				if (line.Length == 0)
				{
					continue;
				}

				var baseline = new RenderPoint(bounds.X + leftPadding, bounds.Y + topPadding + font.Size + index * lineHeight);
				if (safeHyperlink == null)
				{
					operations.Add(canvas => canvas.DrawText(line, baseline, font, color, direction));
				}
				else
				{
					operations.Add(canvas => canvas.DrawHyperlink(line, baseline, font, color, safeHyperlink, direction));
				}
			}
		}

		private static void AddImage(RPLReport report, List<Action<IRenderCanvas>> operations, RPLImageData imageData, RenderRect bounds, IImageCodec imageCodec)
		{
			if (imageData == null)
			{
				return;
			}

			byte[] bytes = imageData.ImageData;
			if (bytes == null && imageData.ImageDataOffset >= 0)
			{
				bytes = report.GetImage(imageData.ImageDataOffset);
			}
			AddEncodedImage(operations, bytes, bounds, imageCodec);
		}

		private static void AddDynamicImage(RPLReport report, List<Action<IRenderCanvas>> operations, RPLDynamicImageProps properties, RenderRect bounds, IImageCodec imageCodec)
		{
			byte[] bytes = null;
			if (properties.DynamicImageContent != null)
			{
				Stream stream = properties.DynamicImageContent;
				if (stream.CanSeek)
				{
					stream.Position = 0;
				}
				using (var copy = new MemoryStream())
				{
					stream.CopyTo(copy);
					bytes = copy.ToArray();
				}
			}
			else if (properties.DynamicImageContentOffset >= 0)
			{
				bytes = report.GetImage(properties.DynamicImageContentOffset);
			}

			AddEncodedImage(operations, bytes, bounds, imageCodec);
		}

		private static void AddEncodedImage(List<Action<IRenderCanvas>> operations, byte[] bytes, RenderRect bounds, IImageCodec imageCodec)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return;
			}

			RenderImage image = imageCodec.Decode(bytes);
			operations.Add(canvas => canvas.DrawImage(image, bounds));
		}

		private static void AddFillAndBorder(List<Action<IRenderCanvas>> operations, RPLElementStyle style, RenderRect bounds)
		{
			if (style == null)
			{
				return;
			}

			RenderColor fill = GetColor(style, 34, new RenderColor(0, 0, 0, 0));
			if (fill.Alpha != 0)
			{
				operations.Add(canvas => canvas.FillRectangle(bounds, fill));
			}
		}

		private static void AddBorder(List<Action<IRenderCanvas>> operations, RPLElementStyle style, RenderRect bounds)
		{
			if (style == null || GetBorderStyle(style) == RPLFormat.BorderStyles.None)
			{
				return;
			}

			RenderColor color = GetColor(style, 0, RenderColor.Black);
			float width = GetSizeInPoints(style, 10, 1f);
			operations.Add(canvas => canvas.DrawRectangle(bounds, color, width));
		}

		private static FontRequest GetFont(RPLElementStyle style)
		{
			string family = style == null ? null : style[20] as string;
			float fontSize = GetSizeInPoints(style, 21, 10f);
			RPLFormat.FontWeights weight = GetEnum(style == null ? null : style[22], RPLFormat.FontWeights.Normal);
			RPLFormat.FontStyles fontStyle = GetEnum(style == null ? null : style[19], RPLFormat.FontStyles.Normal);
			return new FontRequest(string.IsNullOrEmpty(family) ? "Arial" : family, fontSize, IsBold(weight), fontStyle == RPLFormat.FontStyles.Italic);
		}

		private static TextDirection GetTextDirection(RPLElementStyle style)
		{
			RPLFormat.Directions direction = GetEnum(style == null ? null : style[29], RPLFormat.Directions.LTR);
			RPLFormat.WritingModes writingMode = GetEnum(style == null ? null : style[30], RPLFormat.WritingModes.Horizontal);
			if (writingMode == RPLFormat.WritingModes.Vertical)
			{
				return TextDirection.TopToBottom;
			}
			if (writingMode == RPLFormat.WritingModes.Rotate270)
			{
				return TextDirection.BottomToTop;
			}
			return direction == RPLFormat.Directions.RTL ? TextDirection.RightToLeft : TextDirection.LeftToRight;
		}

		private static RenderColor GetColor(RPLElementStyle style, byte property, RenderColor fallback)
		{
			string value = style == null ? null : style[property] as string;
			if (string.IsNullOrEmpty(value) || string.Equals(value, "TRANSPARENT", StringComparison.OrdinalIgnoreCase))
			{
				return fallback;
			}

			Color color = new RPLReportColor(value).ToColor();
			return color == Color.Empty ? fallback : new RenderColor(color.R, color.G, color.B, color.A);
		}

		private static float GetSizeInPoints(RPLElementStyle style, byte property, float fallback)
		{
			string value = style == null ? null : style[property] as string;
			if (string.IsNullOrEmpty(value))
			{
				return fallback;
			}

			float result = (float)new RPLReportSize(value).ToPoints();
			return result > 0f ? result : fallback;
		}

		private static RPLFormat.BorderStyles GetBorderStyle(RPLElementStyle style)
		{
			return GetEnum(style == null ? null : style[5], RPLFormat.BorderStyles.None);
		}

		private static T GetEnum<T>(object value, T fallback) where T : struct
		{
			if (value == null)
			{
				return fallback;
			}

			if (value is T)
			{
				return (T)value;
			}

			try
			{
				return (T)Enum.ToObject(typeof(T), value);
			}
			catch
			{
				return fallback;
			}
		}

		private static bool IsBold(RPLFormat.FontWeights weight)
		{
			return weight >= RPLFormat.FontWeights.SemiBold;
		}

		private static string GetHyperlink(RPLActionInfo actionInfo)
		{
			if (actionInfo == null || actionInfo.Actions == null)
			{
				return null;
			}

			foreach (RPLAction action in actionInfo.Actions)
			{
				if (action != null && !string.IsNullOrWhiteSpace(action.Hyperlink))
				{
					return action.Hyperlink;
				}
			}
			return null;
		}

		private static bool IsSafeHyperlink(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				return false;
			}

			return Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri parsed)
				&& (!parsed.IsAbsoluteUri || parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeMailto);
		}

		private static float Sum(float[] values, int count)
		{
			float result = 0f;
			for (int index = 0; index < count && index < values.Length; index++)
			{
				result += values[index];
			}
			return result;
		}

		private static float ToPoints(float millimeters)
		{
			return millimeters * PointsPerMillimeter;
		}

		private sealed class RplPage
		{
			internal RplPage(RenderSize size, List<Action<IRenderCanvas>> operations)
			{
				Size = size;
				Operations = operations;
			}

			internal RenderSize Size { get; }
			internal List<Action<IRenderCanvas>> Operations { get; }
		}
	}
}
#endif
