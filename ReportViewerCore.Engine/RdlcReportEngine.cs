using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReportViewerCore.Rendering;

namespace ReportViewerCore.Engine;

public sealed record RdlcDataContext(
	IReadOnlyDictionary<string, IEnumerable>? DataSets = null,
	IReadOnlyDictionary<string, object?>? Parameters = null,
	IImageResolver? ImageResolver = null,
	IRdlcSubreportResolver? SubreportResolver = null);

public interface IRdlcSubreportResolver
{
	Stream Open(string reportName);
}

public sealed class RdlcReportEngine
{
	private static readonly Regex FieldExpression = new(@"^Fields!([^\.]+)\.Value$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	private static readonly Regex ParameterExpression = new(@"^Parameters!([^\.]+)\.Value$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	public ReportDocument CreateDocument(Stream definition, RdlcDataContext? data = null)
	{
		ArgumentNullException.ThrowIfNull(definition);
		return CreateDocument(definition, data ?? new RdlcDataContext(), 0);
	}

	private ReportDocument CreateDocument(Stream definition, RdlcDataContext context, int depth)
	{
		XDocument report = XDocument.Load(definition, LoadOptions.PreserveWhitespace);
		XElement root = report.Root ?? throw new InvalidDataException("The RDLC document has no root element.");
		XNamespace ns = root.Name.Namespace;
		context = ApplyParameterDefaults(root, ns, context);

		XElement section = root.Element(ns + "ReportSections")?.Element(ns + "ReportSection") ?? root;
		XElement body = section.Element(ns + "Body") ?? throw new InvalidDataException("The RDLC document has no report body.");
		RenderSize pageSize = new(ParseSize(section.Element(ns + "PageWidth")?.Value, 595), ParseSize(section.Element(ns + "PageHeight")?.Value, 842));
		IReadOnlyDictionary<string, RenderImageRequest> embeddedImages = ReadEmbeddedImages(root, ns);
		IReadOnlyList<XElement> tablixes = body.Element(ns + "ReportItems")?.Elements(ns + "Tablix").ToArray() ?? Array.Empty<XElement>();
		if (tablixes.Count > 0)
		{
			return CreateTablixDocument(tablixes, body, ns, pageSize, context, section, embeddedImages);
		}

		XElement? subreport = body.Element(ns + "ReportItems")?.Elements(ns + "Subreport").FirstOrDefault();
		if (subreport is not null)
		{
			return CreateSubreportDocument(subreport, ns, pageSize, context, depth);
		}

		return CreateTextboxDocument(section, body, ns, pageSize, context, embeddedImages);
	}

	private static RdlcDataContext ApplyParameterDefaults(XElement root, XNamespace ns, RdlcDataContext context)
	{
		IEnumerable<XElement> definitions = root.Element(ns + "ReportParameters")?.Elements(ns + "ReportParameter") ?? Enumerable.Empty<XElement>();
		if (!definitions.Any())
		{
			return context;
		}

		var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		if (context.Parameters is not null)
		{
			foreach ((string name, object? value) in context.Parameters)
			{
				parameters[name] = value;
			}
		}

		foreach (XElement definition in definitions)
		{
			string name = definition.Attribute("Name")?.Value ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name) || parameters.ContainsKey(name))
			{
				continue;
			}

			XElement[] defaultValues = definition.Element(ns + "DefaultValues")?.Elements(ns + "DefaultValue").ToArray() ?? Array.Empty<XElement>();
			if (defaultValues.Length > 0)
			{
				bool isMultiValue = string.Equals(definition.Attribute("MultiValue")?.Value, "true", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(definition.Element(ns + "MultiValue")?.Value, "true", StringComparison.OrdinalIgnoreCase);
				parameters[name] = isMultiValue
					? defaultValues.Select(value => value.Value).ToArray()
					: defaultValues[0].Value;
			}
		}

		return context with { Parameters = parameters };
	}

	private ReportDocument CreateSubreportDocument(XElement subreport, XNamespace ns, RenderSize pageSize, RdlcDataContext context, int depth)
	{
		if (depth >= 8)
		{
			throw new InvalidDataException("The RDLC subreport nesting limit of 8 was exceeded.");
		}

		string reportName = subreport.Element(ns + "ReportName")?.Value ?? string.Empty;
		if (context.SubreportResolver is null)
		{
			throw new InvalidDataException($"The RDLC subreport '{reportName}' requires RdlcDataContext.SubreportResolver.");
		}
		if (string.IsNullOrWhiteSpace(reportName))
		{
			throw new InvalidDataException("The RDLC subreport has no ReportName.");
		}

		using Stream childDefinition = context.SubreportResolver.Open(reportName) ?? throw new InvalidDataException($"The RDLC subreport '{reportName}' could not be resolved.");
		var childParameters = new Dictionary<string, object?>(context.Parameters ?? new Dictionary<string, object?>(), StringComparer.OrdinalIgnoreCase);
		foreach (XElement parameter in subreport.Element(ns + "Parameters")?.Elements(ns + "Parameter") ?? Enumerable.Empty<XElement>())
		{
			string name = parameter.Attribute("Name")?.Value ?? string.Empty;
			string expression = parameter.Element(ns + "Value")?.Value ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new InvalidDataException("The RDLC subreport parameter has no Name.");
			}
			childParameters[name] = ResolveValue(expression, null, context);
		}

		var childContext = new RdlcDataContext(context.DataSets, childParameters, context.ImageResolver, context.SubreportResolver);
		ReportDocument child = CreateDocument(childDefinition, childContext, depth + 1);
		float left = ParseSize(subreport.Element(ns + "Left")?.Value, 0);
		float top = ParseSize(subreport.Element(ns + "Top")?.Value, 0);
		return new ReportDocument(child.Pages.Select(childPage => new ReportPage(pageSize, canvas =>
		{
			canvas.Clear(RenderColor.White);
			using var offset = new OffsetRenderCanvas(canvas, left, top, childPage.Size);
			childPage.Render(offset);
		})));
	}

	private static ReportDocument CreateTablixDocument(IReadOnlyList<XElement> tablixes, XElement body, XNamespace ns, RenderSize pageSize, RdlcDataContext context, XElement section, IReadOnlyDictionary<string, RenderImageRequest> embeddedImages)
	{
		var placements = new List<PlacedText>();
		var images = new List<PlacedImage>();
		var charts = new List<PlacedChart>();
		var shapes = new List<PlacedShape>();
		for (int tablixIndex = 0; tablixIndex < tablixes.Count; tablixIndex++)
		{
			XElement tablix = tablixes[tablixIndex];
			List<float> columnWidths = tablix.Element(ns + "TablixBody")?.Element(ns + "TablixColumns")?.Elements(ns + "TablixColumn").Select(column => ParseSize(column.Element(ns + "Width")?.Value, 100)).ToList() ?? new List<float>();
			List<XElement> rowTemplates = tablix.Element(ns + "TablixBody")?.Element(ns + "TablixRows")?.Elements(ns + "TablixRow").ToList() ?? new List<XElement>();
			if (rowTemplates.Count == 0)
			{
				throw new InvalidDataException("The RDLC tablix has no rows.");
			}

			float left = ParseSize(tablix.Element(ns + "Left")?.Value, 0);
			float top = ParseSize(tablix.Element(ns + "Top")?.Value, tablixIndex * 120);
			string? dataSetName = tablix.Element(ns + "DataSetName")?.Value;
			IReadOnlyList<object?> rows = SortRows(tablix, ns, ResolveRows(dataSetName, context), context).ToArray();
			float contentTop = 0;
			XElement? headerTemplate = rowTemplates.FirstOrDefault(row => ExtractTexts(row, ns).Any(text => !IsExpression(text.Value)));
			if (headerTemplate is not null)
			{
				AddRow(placements, images, charts, shapes, headerTemplate, ns, columnWidths, contentTop, null, context, embeddedImages, rows, left, top);
				contentTop += ReadRowHeight(headerTemplate, ns);
			}

			string[] groupExpressions = ReadGroupExpressions(tablix, ns);
			ValidateSupportedGroupHierarchy(tablix, ns);
			ValidateSupportedGroupPageBreaks(tablix, ns);
			int groupHeaderCount = groupExpressions.Length > 0 && headerTemplate is not null
				? Math.Min(groupExpressions.Length, Math.Max(0, rowTemplates.Count - 2))
				: 0;
			bool hasGroupFooter = groupHeaderCount > 0 && rowTemplates.Count > groupHeaderCount + 2;
			XElement detailTemplate = groupHeaderCount > 0
				? rowTemplates[rowTemplates.Count - (hasGroupFooter ? 2 : 1)]
				: headerTemplate is null
					? rowTemplates[^1]
					: rowTemplates.FirstOrDefault(row => row != headerTemplate) ?? rowTemplates[0];
			XElement? groupFooterTemplate = hasGroupFooter ? rowTemplates[^1] : null;
			IReadOnlySet<int> groupPageBreakLevels = ReadGroupPageBreakLevels(tablix, ns);
			var renderedGroupPrefixes = new HashSet<GroupPrefixKey>(GroupPrefixKeyComparer.Instance);
			GroupScope? previousGroupScope = null;
			foreach (GroupScope groupScope in GroupRows(tablix, ns, rows, context))
			{
				IReadOnlyList<object?> scopeRows = groupScope.Rows;
				if (previousGroupScope is not null && scopeRows.Count > 0 && groupPageBreakLevels.Any(level => !groupScope.Keys.Take(level + 1).SequenceEqual(previousGroupScope.Keys.Take(level + 1), StringComparer.CurrentCulture)))
				{
					contentTop = MoveToNextPageTop(top, contentTop, pageSize.Height);
				}

				if (groupHeaderCount > 0 && scopeRows.Count > 0)
				{
					for (int groupLevel = 0; groupLevel < groupHeaderCount; groupLevel++)
					{
						var groupPrefix = new GroupPrefixKey(groupLevel, groupScope.Keys.Take(groupLevel + 1).ToArray());
						if (renderedGroupPrefixes.Add(groupPrefix))
						{
							IReadOnlyList<object?> headerScopeRows = groupScope.PrefixRows[groupLevel];
							XElement groupHeader = rowTemplates[groupLevel + 1];
							AddRow(placements, images, charts, shapes, groupHeader, ns, columnWidths, contentTop, headerScopeRows[0], context, embeddedImages, headerScopeRows, left, top);
							contentTop += ReadRowHeight(groupHeader, ns);
						}
					}
				}

				foreach (object? row in scopeRows)
				{
					AddRow(placements, images, charts, shapes, detailTemplate, ns, columnWidths, contentTop, row, context, embeddedImages, scopeRows, left, top);
					contentTop += ReadRowHeight(detailTemplate, ns);
				}

				if (groupFooterTemplate is not null && scopeRows.Count > 0)
				{
					AddRow(placements, images, charts, shapes, groupFooterTemplate, ns, columnWidths, contentTop, scopeRows[^1], context, embeddedImages, scopeRows, left, top);
					contentTop += ReadRowHeight(groupFooterTemplate, ns);
				}

				if (scopeRows.Count > 0)
				{
					previousGroupScope = groupScope;
				}
			}

			string noRowsMessage = tablix.Element(ns + "NoRowsMessage")?.Value.Trim() ?? string.Empty;
			if (rows.Count == 0 && noRowsMessage.Length > 0)
			{
				XElement? messageTextbox = (headerTemplate ?? detailTemplate).Descendants(ns + "Textbox").FirstOrDefault();
				FontRequest messageFont = messageTextbox is null ? new FontRequest("Arial", 12) : ReadFont(messageTextbox, ns);
				placements.Add(new PlacedText(noRowsMessage, new RenderPoint(left + 4, top + contentTop + messageFont.Size), messageFont));
				contentTop += messageFont.Size * 1.5f;
			}

			if (contentTop == 0)
			{
				AddRow(placements, images, charts, shapes, rowTemplates[0], ns, columnWidths, contentTop, null, context, embeddedImages, rows, left, top);
			}
		}

		AddReportItems(body.Element(ns + "ReportItems"), ns, context, embeddedImages, placements, images, charts, shapes);
		return CreateDocument(pageSize, placements, images, charts, shapes, ReadPageDecorations(section, ns, pageSize, context));
	}

	private static ReportDocument CreateTextboxDocument(XElement section, XElement body, XNamespace ns, RenderSize pageSize, RdlcDataContext context, IReadOnlyDictionary<string, RenderImageRequest> embeddedImages)
	{
		var placements = new List<PlacedText>();
		var images = new List<PlacedImage>();
		var charts = new List<PlacedChart>();
		var shapes = new List<PlacedShape>();
		AddReportItems(body.Element(ns + "ReportItems") ?? body, ns, context, embeddedImages, placements, images, charts, shapes);

		return CreateDocument(pageSize, placements, images, charts, shapes, ReadPageDecorations(section, ns, pageSize, context));
	}

	private static IReadOnlyList<PlacedText> ReadPageDecorations(XElement section, XNamespace ns, RenderSize pageSize, RdlcDataContext context)
	{
		var decorations = new List<PlacedText>();
		XElement? header = section.Element(ns + "PageHeader");
		AddTextboxes(decorations, header?.Element(ns + "ReportItems"), ns, context);

		XElement? footer = section.Element(ns + "PageFooter");
		float footerOffset = pageSize.Height - ParseSize(footer?.Element(ns + "Height")?.Value, 0);
		AddTextboxes(decorations, footer?.Element(ns + "ReportItems"), ns, context, footerOffset);
		return decorations;
	}

	private static void AddTextboxes(List<PlacedText> placements, XElement? container, XNamespace ns, RdlcDataContext context, float topOffset = 0)
	{
		if (container is null)
		{
			return;
		}

		foreach (XElement textbox in container.Descendants(ns + "Textbox"))
		{
			if (IsHidden(textbox, ns, context, null))
			{
				continue;
			}

			string value = textbox.Descendants(ns + "TextRun").Elements(ns + "Value").FirstOrDefault()?.Value ?? string.Empty;
			float top = ParseSize(textbox.Element(ns + "Top")?.Value, placements.Count * 20) + topOffset;
			placements.Add(CreateTextPlacement(textbox, ns, context, null, new RenderPoint(ParseSize(textbox.Element(ns + "Left")?.Value, 0), top)));
		}
	}

	private static void AddReportItems(XElement? container, XNamespace ns, RdlcDataContext context, IReadOnlyDictionary<string, RenderImageRequest> embeddedImages, List<PlacedText> placements, List<PlacedImage> images, List<PlacedChart> charts, List<PlacedShape> shapes, float parentLeft = 0, float parentTop = 0)
	{
		if (container is null)
		{
			return;
		}

		foreach (XElement item in container.Elements())
		{
			if (IsHidden(item, ns, context, null))
			{
				continue;
			}

			float left = parentLeft + ParseSize(item.Element(ns + "Left")?.Value, 0);
			float top = parentTop + ParseSize(item.Element(ns + "Top")?.Value, 0);
			switch (item.Name.LocalName)
			{
				case "Textbox":
					AddTextbox(placements, item, ns, context, left, top);
					break;
				case "Image":
					images.Add(ReadImage(item, ns, context, embeddedImages, parentLeft, parentTop));
					break;
				case "Chart":
					charts.Add(ReadChart(item, ns, context, parentLeft, parentTop));
					break;
				case "Rectangle":
					shapes.Add(ReadRectangle(item, ns, parentLeft, parentTop));
					AddReportItems(item.Element(ns + "ReportItems"), ns, context, embeddedImages, placements, images, charts, shapes, left, top);
					break;
				case "Line":
					shapes.Add(ReadLine(item, ns, parentLeft, parentTop));
					break;
				case "Map":
				case "GaugePanel":
				case "CustomReportItem":
					throw new NotSupportedException($"The constrained RDLC engine does not support '{item.Name.LocalName}' report items.");
				default:
					AddReportItems(item.Element(ns + "ReportItems"), ns, context, embeddedImages, placements, images, charts, shapes, left, top);
					break;
			}
		}
	}

	private static void AddTextbox(List<PlacedText> placements, XElement textbox, XNamespace ns, RdlcDataContext context, float left, float top)
	{
		placements.Add(CreateTextPlacement(textbox, ns, context, null, new RenderPoint(left, top)));
	}

	private static PlacedText CreateTextPlacement(XElement textbox, XNamespace ns, RdlcDataContext context, object? dataRow, RenderPoint baseline, IReadOnlyList<object?>? scopeRows = null)
	{
		string value = textbox.Descendants(ns + "TextRun").Elements(ns + "Value").FirstOrDefault()?.Value ?? string.Empty;
		return new PlacedText(ResolveValue(value, dataRow, context, scopeRows), baseline, ReadFont(textbox, ns))
		{
			Color = ReadTextColor(textbox, ns),
			Direction = ReadTextDirection(textbox, ns),
			Hyperlink = ReadHyperlink(textbox, ns, dataRow, context, scopeRows)
		};
	}

	private static ReportDocument CreateDocument(RenderSize pageSize, IReadOnlyList<PlacedText> placements, IReadOnlyList<PlacedImage>? images = null, IReadOnlyList<PlacedChart>? charts = null, IReadOnlyList<PlacedShape>? shapes = null, IReadOnlyList<PlacedText>? repeatingTexts = null)
	{
		images ??= Array.Empty<PlacedImage>();
		charts ??= Array.Empty<PlacedChart>();
		shapes ??= Array.Empty<PlacedShape>();
		repeatingTexts ??= Array.Empty<PlacedText>();
		int imagePages = images.Count == 0 ? 1 : images.Max(placement => Math.Max(0, (int)MathF.Floor(placement.Destination.Y / pageSize.Height))) + 1;
		int chartPages = charts.Count == 0 ? 1 : charts.Max(placement => Math.Max(0, (int)MathF.Floor(placement.Destination.Y / pageSize.Height))) + 1;
		int shapePages = shapes.Count == 0 ? 1 : shapes.Max(placement => Math.Max(0, (int)MathF.Floor(placement.Bounds.Y / pageSize.Height))) + 1;
		int pageCount = placements.Count == 0
			? Math.Max(Math.Max(imagePages, chartPages), shapePages)
			: Math.Max(placements.Max(placement => Math.Max(0, (int)MathF.Floor(placement.Baseline.Y / pageSize.Height))) + 1, Math.Max(Math.Max(imagePages, chartPages), shapePages));
		var pages = new List<ReportPage>(pageCount);
		for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
		{
			int currentPage = pageIndex;
			IReadOnlyList<PlacedText> pagePlacements = placements
				.Where(placement => Math.Max(0, (int)MathF.Floor(placement.Baseline.Y / pageSize.Height)) == currentPage)
				.Select(placement => placement with
				{
					Baseline = placement.Baseline with { Y = placement.Baseline.Y - currentPage * pageSize.Height }
				})
				.Concat(repeatingTexts)
				.ToArray();
			IReadOnlyList<PlacedImage> pageImages = images
				.Where(placement => Math.Max(0, (int)MathF.Floor(placement.Destination.Y / pageSize.Height)) == currentPage)
				.Select(placement => placement with
				{
					Destination = placement.Destination with { Y = placement.Destination.Y - currentPage * pageSize.Height }
				})
				.ToArray();
			IReadOnlyList<PlacedChart> pageCharts = charts
				.Where(placement => Math.Max(0, (int)MathF.Floor(placement.Destination.Y / pageSize.Height)) == currentPage)
				.Select(placement => placement with
				{
					Destination = placement.Destination with { Y = placement.Destination.Y - currentPage * pageSize.Height }
				})
				.ToArray();
			IReadOnlyList<PlacedShape> pageShapes = shapes
				.Where(placement => Math.Max(0, (int)MathF.Floor(placement.Bounds.Y / pageSize.Height)) == currentPage)
				.Select(placement => placement with
				{
					Bounds = placement.Bounds with { Y = placement.Bounds.Y - currentPage * pageSize.Height }
				})
				.ToArray();
			pages.Add(new ReportPage(pageSize, canvas =>
			{
				canvas.Clear(RenderColor.White);
				foreach (PlacedShape placement in pageShapes)
				{
					if (placement.IsLine)
					{
						canvas.DrawLine(new RenderPoint(placement.Bounds.X, placement.Bounds.Y), new RenderPoint(placement.Bounds.Right, placement.Bounds.Bottom), placement.Stroke ?? RenderColor.Black, placement.StrokeWidth);
						continue;
					}

					if (placement.Fill is RenderColor fill)
					{
						canvas.FillRectangle(placement.Bounds, fill);
					}
					if (placement.Stroke is RenderColor stroke)
					{
						canvas.DrawRectangle(placement.Bounds, stroke, placement.StrokeWidth);
					}
				}
				foreach (PlacedText placement in pagePlacements)
				{
					if (placement.Hyperlink is string hyperlink)
					{
						canvas.DrawHyperlink(placement.Text, placement.Baseline, placement.Font, placement.Color, hyperlink, placement.Direction);
					}
					else
					{
						canvas.DrawText(placement.Text, placement.Baseline, placement.Font, placement.Color, placement.Direction);
					}
				}
				foreach (PlacedImage placement in pageImages)
				{
					canvas.DrawImage(placement.Image, placement.Destination);
				}
				foreach (PlacedChart placement in pageCharts)
				{
					canvas.DrawChart(placement.Type, placement.Title, placement.Bars, placement.Destination, placement.Font, RenderColor.Black);
				}
			}));
		}

		return new ReportDocument(pages);
	}

	private static void AddRow(List<PlacedText> placements, List<PlacedImage> images, List<PlacedChart> charts, List<PlacedShape> shapes, XElement row, XNamespace ns, IReadOnlyList<float> columnWidths, float rowTop, object? dataRow, RdlcDataContext context, IReadOnlyDictionary<string, RenderImageRequest> embeddedImages, IReadOnlyList<object?> scopeRows, float leftOffset = 0, float topOffset = 0)
	{
		float x = 0;
		float height = ReadRowHeight(row, ns);
		foreach ((XElement cell, int index) in row.Element(ns + "TablixCells")?.Elements(ns + "TablixCell").Select((cell, index) => (cell, index)) ?? Enumerable.Empty<(XElement, int)>())
		{
			XElement? textbox = cell.Descendants(ns + "Textbox").FirstOrDefault();
			if (textbox is not null && !IsHidden(textbox, ns, context, dataRow, scopeRows))
			{
				string value = textbox.Descendants(ns + "TextRun").Elements(ns + "Value").FirstOrDefault()?.Value ?? string.Empty;
				placements.Add(CreateTextPlacement(textbox, ns, context, dataRow, new RenderPoint(leftOffset + x + 4, topOffset + rowTop + height * 0.75f), scopeRows));
			}

				foreach (XElement image in cell.Descendants(ns + "Image"))
				{
					if (!IsHidden(image, ns, context, dataRow, scopeRows))
					{
						images.Add(ReadImage(image, ns, context, embeddedImages, leftOffset + x, topOffset + rowTop, dataRow));
					}
				}

				foreach (XElement chart in cell.Descendants(ns + "Chart"))
				{
					if (!IsHidden(chart, ns, context, dataRow, scopeRows))
					{
						charts.Add(ReadChart(chart, ns, context, leftOffset + x, topOffset + rowTop));
					}
				}

				foreach (XElement rectangle in cell.Descendants(ns + "Rectangle"))
				{
					if (!IsHidden(rectangle, ns, context, dataRow, scopeRows))
					{
						shapes.Add(ReadRectangle(rectangle, ns, leftOffset + x, topOffset + rowTop));
					}
				}

				foreach (XElement line in cell.Descendants(ns + "Line"))
				{
					if (!IsHidden(line, ns, context, dataRow, scopeRows))
					{
						shapes.Add(ReadLine(line, ns, leftOffset + x, topOffset + rowTop));
					}
				}
				x += index < columnWidths.Count ? columnWidths[index] : 100;
		}
	}

	private static float ReadRowHeight(XElement row, XNamespace ns) => ParseSize(row.Element(ns + "Height")?.Value, 20);

	private static IReadOnlySet<int> ReadGroupPageBreakLevels(XElement tablix, XNamespace ns)
	{
		var levels = new HashSet<int>();
		XElement? members = tablix.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers");
		int level = 0;
		if (members is not null)
		{
			ReadGroupPageBreakLevels(members, ns, levels, ref level);
		}

		return levels;
	}

	private static void ReadGroupPageBreakLevels(XElement members, XNamespace ns, ISet<int> levels, ref int level)
	{
		foreach (XElement member in members.Elements(ns + "TablixMember"))
		{
			XElement? group = member.Element(ns + "Group");
			int expressionCount = group?.Element(ns + "GroupExpressions")?.Elements(ns + "GroupExpression")
				.Count(expression => !string.IsNullOrWhiteSpace(expression.Value)) ?? 0;
			if (expressionCount > 0)
			{
				bool breaksBetween = group?.Element(ns + "PageBreak")?.Element(ns + "BreakLocation") is XElement location
					&& string.Equals(location.Value.Trim(), "Between", StringComparison.OrdinalIgnoreCase);
				if (breaksBetween)
				{
					levels.Add(level + expressionCount - 1);
				}

				level += expressionCount;
			}

			XElement? nestedMembers = member.Element(ns + "TablixMembers");
			if (nestedMembers is not null)
			{
				ReadGroupPageBreakLevels(nestedMembers, ns, levels, ref level);
			}
		}
	}

	private static void ValidateSupportedGroupPageBreaks(XElement tablix, XNamespace ns)
	{
		foreach (XElement location in tablix.Element(ns + "TablixRowHierarchy")?.Descendants(ns + "PageBreak")
			.Elements(ns + "BreakLocation") ?? Enumerable.Empty<XElement>())
		{
			string value = location.Value.Trim();
			if (!string.IsNullOrWhiteSpace(value)
				&& !string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(value, "Between", StringComparison.OrdinalIgnoreCase))
			{
				throw new NotSupportedException($"The constrained RDLC engine only supports grouped page breaks at 'Between', not '{value}'.");
			}
		}
	}

	private static float MoveToNextPageTop(float topOffset, float contentTop, float pageHeight)
	{
		float absoluteTop = topOffset + contentTop;
		float pageStart = MathF.Floor(absoluteTop / pageHeight) * pageHeight;
		float remainder = absoluteTop - pageStart;
		return remainder < 0.01f ? contentTop : pageStart + pageHeight - topOffset;
	}

	private static IEnumerable<object?> ResolveRows(string? dataSetName, RdlcDataContext context)
	{
		if (dataSetName is not null && context.DataSets is not null)
		{
			if (context.DataSets.TryGetValue(dataSetName, out IEnumerable? rows))
			{
				return rows.Cast<object?>();
			}

			foreach ((string name, IEnumerable candidateRows) in context.DataSets)
			{
				if (string.Equals(name, dataSetName, StringComparison.OrdinalIgnoreCase))
				{
					return candidateRows.Cast<object?>();
				}
			}
		}
		return Enumerable.Empty<object?>();
	}

	private static IEnumerable<object?> SortRows(XElement tablix, XNamespace ns, IEnumerable rows, RdlcDataContext context)
	{
		var sortExpressions = tablix.Element(ns + "SortExpressions")?.Elements(ns + "SortExpression")
			.Select(sort => (Expression: sort.Element(ns + "Value")?.Value ?? string.Empty, Descending: string.Equals(sort.Element(ns + "Direction")?.Value, "Descending", StringComparison.OrdinalIgnoreCase)))
			.Where(sort => !string.IsNullOrWhiteSpace(sort.Expression))
			.ToArray() ?? Array.Empty<(string Expression, bool Descending)>();
		if (sortExpressions.Length == 0)
		{
			return rows.Cast<object?>();
		}

		IEnumerable<object?> values = rows.Cast<object?>().ToArray();
		IOrderedEnumerable<object?>? ordered = null;
		foreach ((string expression, bool descending) in sortExpressions)
		{
			Func<object?, string> selector = row => ResolveValue(expression, row, context);
			ordered = ordered is null
				? descending ? values.OrderByDescending(selector, StringValueComparer.Instance) : values.OrderBy(selector, StringValueComparer.Instance)
				: descending ? ordered.ThenByDescending(selector, StringValueComparer.Instance) : ordered.ThenBy(selector, StringValueComparer.Instance);
		}

		return ordered ?? values;
	}

	private static IReadOnlyList<GroupScope> GroupRows(XElement tablix, XNamespace ns, IReadOnlyList<object?> rows, RdlcDataContext context)
	{
		string[] expressions = ReadGroupExpressions(tablix, ns);
		if (expressions.Length == 0)
		{
			return new[] { new GroupScope(rows, Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>()) };
		}

		var groups = new Dictionary<IReadOnlyList<string>, (List<object?> Rows, string[] Keys)>(StringSequenceComparer.Instance);
		var keyedRows = new List<(object? Row, string[] Keys)>();
		var orderedGroups = new List<GroupScope>();
		foreach (object? row in rows)
		{
			string[] keys = expressions.Select(expression => ResolveValue(expression, row, context)).ToArray();
			keyedRows.Add((row, keys));
			if (!groups.TryGetValue(keys, out (List<object?> Rows, string[] Keys) group))
			{
				group = (new List<object?>(), keys);
				groups.Add(keys, group);
				orderedGroups.Add(new GroupScope(group.Rows, group.Keys, Array.Empty<IReadOnlyList<object?>>()));
			}

			group.Rows.Add(row);
		}

		return orderedGroups.Select(group => group with
		{
			PrefixRows = Enumerable.Range(0, expressions.Length)
				.Select(level => (IReadOnlyList<object?>)keyedRows
					.Where(item => item.Keys.Take(level + 1).SequenceEqual(group.Keys.Take(level + 1), StringComparer.CurrentCulture))
					.Select(item => item.Row)
					.ToArray())
				.ToArray()
		}).ToArray();
	}

	private static string[] ReadGroupExpressions(XElement tablix, XNamespace ns)
	{
		XElement? members = tablix.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers");
		return members is null ? Array.Empty<string>() : ReadMemberGroupExpressions(members, ns).ToArray();
	}

	private static void ValidateSupportedGroupHierarchy(XElement tablix, XNamespace ns)
	{
		XElement? members = tablix.Element(ns + "TablixRowHierarchy")?.Element(ns + "TablixMembers");
		if (members is not null)
		{
			ValidateGroupMembers(members, ns);
		}
	}

	private static void ValidateGroupMembers(XElement members, XNamespace ns)
	{
		XElement[] groupedBranches = members.Elements(ns + "TablixMember")
			.Where(member => MemberContainsGroup(member, ns))
			.ToArray();
		if (groupedBranches.Length > 1)
		{
			throw new NotSupportedException("The constrained RDLC engine supports one linear row-group branch; sibling row-group branches require full member-tree layout.");
		}

		foreach (XElement branch in groupedBranches)
		{
			XElement? nestedMembers = branch.Element(ns + "TablixMembers");
			if (nestedMembers is not null)
			{
				ValidateGroupMembers(nestedMembers, ns);
			}
		}
	}

	private static bool MemberContainsGroup(XElement member, XNamespace ns) => member.Element(ns + "Group")?.Element(ns + "GroupExpressions")?.Elements(ns + "GroupExpression")
		.Any(expression => !string.IsNullOrWhiteSpace(expression.Value)) == true
		|| member.Element(ns + "TablixMembers")?.Elements(ns + "TablixMember").Any(child => MemberContainsGroup(child, ns)) == true;

	private static IEnumerable<string> ReadMemberGroupExpressions(XElement members, XNamespace ns)
	{
		foreach (XElement member in members.Elements(ns + "TablixMember"))
		{
			foreach (string expression in member.Element(ns + "Group")?.Element(ns + "GroupExpressions")?.Elements(ns + "GroupExpression")
				.Select(group => group.Value.Trim())
				.Where(value => !string.IsNullOrWhiteSpace(value)) ?? Enumerable.Empty<string>())
			{
				yield return expression;
			}

			XElement? nestedMembers = member.Element(ns + "TablixMembers");
			if (nestedMembers is not null)
			{
				foreach (string expression in ReadMemberGroupExpressions(nestedMembers, ns))
				{
					yield return expression;
				}
			}
		}
	}

	private sealed record GroupScope(IReadOnlyList<object?> Rows, IReadOnlyList<string> Keys, IReadOnlyList<IReadOnlyList<object?>> PrefixRows);
	private readonly record struct GroupPrefixKey(int Level, IReadOnlyList<string> Keys);

	private sealed class GroupPrefixKeyComparer : IEqualityComparer<GroupPrefixKey>
	{
		public static GroupPrefixKeyComparer Instance { get; } = new();

		public bool Equals(GroupPrefixKey left, GroupPrefixKey right) => left.Level == right.Level && StringSequenceComparer.Instance.Equals(left.Keys, right.Keys);

		public int GetHashCode(GroupPrefixKey value) => HashCode.Combine(value.Level, StringSequenceComparer.Instance.GetHashCode(value.Keys));
	}

	private sealed class StringSequenceComparer : IEqualityComparer<IReadOnlyList<string>>
	{
		public static StringSequenceComparer Instance { get; } = new();

		public bool Equals(IReadOnlyList<string>? left, IReadOnlyList<string>? right) => left is not null && right is not null && left.SequenceEqual(right, StringComparer.CurrentCulture);

		public int GetHashCode(IReadOnlyList<string> values)
		{
			HashCode hash = new();
			foreach (string value in values)
			{
				hash.Add(value, StringComparer.CurrentCulture);
			}

			return hash.ToHashCode();
		}
	}

	private static IEnumerable<XElement> ExtractTexts(XElement row, XNamespace ns) => row.Descendants(ns + "TextRun").Elements(ns + "Value");

	private static bool IsExpression(string value) => value.StartsWith('=');

	private sealed class StringValueComparer : IComparer<string>
	{
		public static StringValueComparer Instance { get; } = new();

		public int Compare(string? left, string? right)
		{
			if (decimal.TryParse(left, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal leftNumber) && decimal.TryParse(right, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal rightNumber))
			{
				return leftNumber.CompareTo(rightNumber);
			}

			return string.Compare(left, right, StringComparison.CurrentCulture);
		}
	}

	private static string ResolveValue(string expression, object? dataRow, RdlcDataContext context, IReadOnlyList<object?>? scopeRows = null)
	{
		if (!expression.StartsWith('='))
		{
			return expression;
		}

		string body = expression[1..].Trim();
		return string.Concat(SplitTopLevel(body, '&').Select(part => ResolveAtom(part, dataRow, context, scopeRows)));
	}

	private static bool IsHidden(XElement item, XNamespace ns, RdlcDataContext context, object? dataRow, IReadOnlyList<object?>? scopeRows = null)
	{
		string expression = item.Element(ns + "Visibility")?.Element(ns + "Hidden")?.Value ?? string.Empty;
		if (string.IsNullOrWhiteSpace(expression))
		{
			return false;
		}

		return bool.TryParse(ResolveValue(expression.Trim(), dataRow, context, scopeRows), out bool hidden) && hidden;
	}

	private static string ResolveAtom(string expression, object? dataRow, RdlcDataContext context, IReadOnlyList<object?>? scopeRows = null)
	{
		string atom = expression.Trim();
		if (atom.Length >= 2 && atom[0] == '"' && atom[^1] == '"')
		{
			return atom[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
		}

		if (atom.StartsWith("Join(", StringComparison.OrdinalIgnoreCase) && atom.EndsWith(')'))
		{
			IReadOnlyList<string> arguments = SplitTopLevel(atom[5..^1], ',');
			if (arguments.Count == 2)
			{
				string separator = ResolveAtom(arguments[1], dataRow, context, scopeRows);
				Match multiValueParameter = ParameterExpression.Match(arguments[0].Trim());
				if (multiValueParameter.Success && context.Parameters is not null && context.Parameters.TryGetValue(multiValueParameter.Groups[1].Value, out object? multiValue))
				{
					return string.Join(separator, EnumerateValues(multiValue));
				}
			}

			return string.Empty;
		}

		if ((atom.StartsWith("Len(", StringComparison.OrdinalIgnoreCase)
			|| atom.StartsWith("Trim(", StringComparison.OrdinalIgnoreCase)
			|| atom.StartsWith("UCase(", StringComparison.OrdinalIgnoreCase)
			|| atom.StartsWith("LCase(", StringComparison.OrdinalIgnoreCase)) && atom.EndsWith(')'))
		{
			int openParenthesis = atom.IndexOf('(');
			IReadOnlyList<string> arguments = SplitTopLevel(atom[(openParenthesis + 1)..^1], ',');
			if (arguments.Count == 1)
			{
				string resolvedString = ResolveAtom(arguments[0], dataRow, context, scopeRows);
				if (atom.StartsWith("Len(", StringComparison.OrdinalIgnoreCase))
				{
					return resolvedString.Length.ToString(CultureInfo.CurrentCulture);
				}
				if (atom.StartsWith("Trim(", StringComparison.OrdinalIgnoreCase))
				{
					return resolvedString.Trim();
				}
				if (atom.StartsWith("UCase(", StringComparison.OrdinalIgnoreCase))
				{
					return resolvedString.ToUpper(CultureInfo.CurrentCulture);
				}
				return resolvedString.ToLower(CultureInfo.CurrentCulture);
			}

			return string.Empty;
		}

		if ((atom.StartsWith("InStr(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("Replace(", StringComparison.OrdinalIgnoreCase)) && atom.EndsWith(')'))
		{
			IReadOnlyList<string> arguments = SplitTopLevel(atom[(atom.IndexOf('(') + 1)..^1], ',');
			if (atom.StartsWith("InStr(", StringComparison.OrdinalIgnoreCase) && arguments.Count == 2)
			{
				string sourceText = ResolveAtom(arguments[0], dataRow, context, scopeRows);
				string search = ResolveAtom(arguments[1], dataRow, context, scopeRows);
				int index = sourceText.IndexOf(search, StringComparison.CurrentCulture);
				return (index < 0 ? 0 : index + 1).ToString(CultureInfo.CurrentCulture);
			}

			if (atom.StartsWith("Replace(", StringComparison.OrdinalIgnoreCase) && arguments.Count == 3)
			{
				string sourceText = ResolveAtom(arguments[0], dataRow, context, scopeRows);
				string search = ResolveAtom(arguments[1], dataRow, context, scopeRows);
				string replacement = ResolveAtom(arguments[2], dataRow, context, scopeRows);
				return sourceText.Replace(search, replacement, StringComparison.CurrentCulture);
			}

			return string.Empty;
		}

		if (atom.StartsWith("IIF(", StringComparison.OrdinalIgnoreCase) && atom.EndsWith(')'))
		{
			IReadOnlyList<string> arguments = SplitTopLevel(atom[4..^1], ',');
			return arguments.Count == 3
				? ResolveAtom(ResolveCondition(arguments[0], dataRow, context, scopeRows) ? arguments[1] : arguments[2], dataRow, context, scopeRows)
				: string.Empty;
		}

		if (atom.StartsWith("Format(", StringComparison.OrdinalIgnoreCase) && atom.EndsWith(')'))
		{
			IReadOnlyList<string> arguments = SplitTopLevel(atom[7..^1], ',');
			if (arguments.Count == 2)
			{
				string formattedValue = ResolveAtom(arguments[0], dataRow, context, scopeRows);
				string format = ResolveAtom(arguments[1], dataRow, context, scopeRows);
				if (decimal.TryParse(formattedValue, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal number))
				{
					return number.ToString(format, CultureInfo.CurrentCulture);
				}
				if (DateTime.TryParse(formattedValue, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime date))
				{
					return date.ToString(format, CultureInfo.CurrentCulture);
				}
			}

			return string.Empty;
		}

		if (atom.StartsWith("IsNothing(", StringComparison.OrdinalIgnoreCase) && atom.EndsWith(')'))
		{
			string argument = atom[10..^1].Trim();
			Match nothingField = FieldExpression.Match(argument);
			if (nothingField.Success)
			{
				return (GetMemberValue(dataRow, nothingField.Groups[1].Value) is null).ToString();
			}

			Match nothingParameter = ParameterExpression.Match(argument);
			if (nothingParameter.Success && context.Parameters is not null && context.Parameters.TryGetValue(nothingParameter.Groups[1].Value, out object? parameterValue))
			{
				return (parameterValue is null).ToString();
			}

			return string.Empty;
		}

		if (atom.StartsWith("Not(", StringComparison.OrdinalIgnoreCase) && atom.EndsWith(')'))
		{
			IReadOnlyList<string> arguments = SplitTopLevel(atom[4..^1], ',');
			if (arguments.Count == 1 && TryResolveCondition(arguments[0], dataRow, context, scopeRows, out bool operand))
			{
				return (!operand).ToString();
			}

			return string.Empty;
		}

		if ((atom.StartsWith("And(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("Or(", StringComparison.OrdinalIgnoreCase)) && atom.EndsWith(')'))
		{
			IReadOnlyList<string> arguments = SplitTopLevel(atom[(atom.IndexOf('(') + 1)..^1], ',');
			if (arguments.Count < 2)
			{
				return string.Empty;
			}

			bool isAnd = atom.StartsWith("And(", StringComparison.OrdinalIgnoreCase);
			bool result = isAnd;
			foreach (string argument in arguments)
			{
				if (!TryResolveCondition(argument, dataRow, context, scopeRows, out bool operand))
				{
					return string.Empty;
				}

				result = isAnd ? result && operand : result || operand;
			}

			return result.ToString();
		}

		if (atom.Equals("True", StringComparison.OrdinalIgnoreCase) || atom.Equals("False", StringComparison.OrdinalIgnoreCase))
		{
			return atom.Equals("True", StringComparison.OrdinalIgnoreCase).ToString();
		}

		if (atom.StartsWith("CountRows(", StringComparison.OrdinalIgnoreCase) && atom.EndsWith(')'))
		{
			return scopeRows?.Count.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
		}

		if ((atom.StartsWith("Count(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("First(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("Last(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("Sum(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("Avg(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("Min(", StringComparison.OrdinalIgnoreCase) || atom.StartsWith("Max(", StringComparison.OrdinalIgnoreCase)) && atom.EndsWith(')'))
		{
			IReadOnlyList<string> arguments = SplitTopLevel(atom[(atom.IndexOf('(') + 1)..^1], ',');
			if (arguments.Count == 1 && scopeRows is not null)
			{
				string aggregateName = atom[..atom.IndexOf('(')];
				if (aggregateName.Equals("Count", StringComparison.OrdinalIgnoreCase))
				{
					return scopeRows.Count(row => !string.IsNullOrEmpty(ResolveAtom(arguments[0], row, context, scopeRows))).ToString(CultureInfo.CurrentCulture);
				}

				if (aggregateName.Equals("First", StringComparison.OrdinalIgnoreCase) || aggregateName.Equals("Last", StringComparison.OrdinalIgnoreCase))
				{
					object? selectedRow = aggregateName.Equals("First", StringComparison.OrdinalIgnoreCase) ? scopeRows.FirstOrDefault() : scopeRows.LastOrDefault();
					return ResolveAtom(arguments[0], selectedRow, context, scopeRows);
				}

				var values = scopeRows
					.Select(row => ResolveAtom(arguments[0], row, context, scopeRows))
					.Select(value => decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal number) ? (decimal?)number : null)
					.Where(value => value.HasValue)
					.Select(value => value!.Value)
					.ToArray();
				if (values.Length > 0)
				{
					decimal aggregate = atom.StartsWith("Avg(", StringComparison.OrdinalIgnoreCase)
						? values.Average()
						: atom.StartsWith("Min(", StringComparison.OrdinalIgnoreCase)
							? values.Min()
							: atom.StartsWith("Max(", StringComparison.OrdinalIgnoreCase) ? values.Max() : values.Sum();
					return aggregate.ToString(CultureInfo.CurrentCulture);
				}
			}

			return string.Empty;
		}

		Match field = FieldExpression.Match(atom);
		if (field.Success)
		{
			return Convert.ToString(GetMemberValue(dataRow, field.Groups[1].Value), CultureInfo.CurrentCulture) ?? string.Empty;
		}

		Match parameter = ParameterExpression.Match(atom);
		if (parameter.Success && context.Parameters is not null && context.Parameters.TryGetValue(parameter.Groups[1].Value, out object? value))
		{
			return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
		}

		if (decimal.TryParse(atom, NumberStyles.Float, CultureInfo.CurrentCulture, out _))
		{
			return atom;
		}

		return string.Empty;
	}

	private static bool ResolveCondition(string expression, object? dataRow, RdlcDataContext context, IReadOnlyList<object?>? scopeRows = null)
	{
		return TryResolveCondition(expression, dataRow, context, scopeRows, out bool result) && result;
	}

	private static bool TryResolveCondition(string expression, object? dataRow, RdlcDataContext context, IReadOnlyList<object?>? scopeRows, out bool result)
	{
		if (bool.TryParse(ResolveAtom(expression, dataRow, context, scopeRows), out result))
		{
			return true;
		}

		string[] operators = [">=", "<=", "<>", "=", ">", "<"];
		foreach (string op in operators)
		{
			int index = expression.IndexOf(op, StringComparison.Ordinal);
			if (index < 0)
			{
				continue;
			}

			string left = ResolveAtom(expression[..index], dataRow, context, scopeRows);
			string right = ResolveAtom(expression[(index + op.Length)..], dataRow, context, scopeRows);
			int comparison;
			if (decimal.TryParse(left, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal leftNumber) && decimal.TryParse(right, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal rightNumber))
			{
				comparison = leftNumber.CompareTo(rightNumber);
			}
			else
			{
				comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
			}

			result = op switch
			{
				">=" => comparison >= 0,
				"<=" => comparison <= 0,
				"<>" => comparison != 0,
				"=" => comparison == 0,
				">" => comparison > 0,
				"<" => comparison < 0,
				_ => false
			};
			return true;
		}

		result = false;
		return false;
	}

	private static IReadOnlyList<string> SplitTopLevel(string value, char separator)
	{
		var parts = new List<string>();
		int start = 0;
		int depth = 0;
		bool quoted = false;
		for (int index = 0; index < value.Length; index++)
		{
			char current = value[index];
			if (current == '"')
			{
				if (quoted && index + 1 < value.Length && value[index + 1] == '"')
				{
					index++;
					continue;
				}

				quoted = !quoted;
				continue;
			}

			if (quoted)
			{
				continue;
			}

			if (current == '(')
			{
				depth++;
			}
			else if (current == ')')
			{
				depth = Math.Max(0, depth - 1);
			}
			else if (current == separator && depth == 0)
			{
				parts.Add(value[start..index]);
				start = index + 1;
			}
		}

		parts.Add(value[start..]);
		return parts;
	}

	private static string? ReadHyperlink(XElement textbox, XNamespace ns, object? dataRow, RdlcDataContext context, IReadOnlyList<object?>? scopeRows = null)
	{
		string expression = textbox.Descendants(ns + "Hyperlink").FirstOrDefault()?.Value ?? string.Empty;
		if (string.IsNullOrWhiteSpace(expression))
		{
			return null;
		}

		string hyperlink = ResolveValue(expression, dataRow, context, scopeRows);
		return string.IsNullOrWhiteSpace(hyperlink) ? null : hyperlink;
	}

	private static object? GetMemberValue(object? value, string memberName)
	{
		if (value is null)
		{
			return null;
		}
		if (value is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				if (string.Equals(Convert.ToString(entry.Key), memberName, StringComparison.OrdinalIgnoreCase))
				{
					return entry.Value;
				}
			}
		}

		PropertyInfo? property = value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));
		return property?.GetValue(value);
	}

	private static IEnumerable<string> EnumerateValues(object? value)
	{
		if (value is null)
		{
			yield break;
		}

		if (value is string text)
		{
			yield return text;
			yield break;
		}

		if (value is IEnumerable values)
		{
			foreach (object? item in values)
			{
				yield return Convert.ToString(item, CultureInfo.CurrentCulture) ?? string.Empty;
			}
			yield break;
		}

		yield return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
	}

	private static FontRequest ReadFont(XElement textbox, XNamespace ns)
	{
		XElement style = textbox.Element(ns + "Style") ?? textbox.Descendants(ns + "Style").FirstOrDefault() ?? new XElement(ns + "Style");
		string family = style.Element(ns + "FontFamily")?.Value ?? "Arial";
		float size = ParseSize(style.Element(ns + "FontSize")?.Value, 12);
		bool bold = string.Equals(style.Element(ns + "FontWeight")?.Value, "Bold", StringComparison.OrdinalIgnoreCase);
		bool italic = string.Equals(style.Element(ns + "FontStyle")?.Value, "Italic", StringComparison.OrdinalIgnoreCase);
		return new FontRequest(family, size, bold, italic);
	}

	private static RenderColor ReadTextColor(XElement textbox, XNamespace ns)
	{
		XElement style = textbox.Element(ns + "Style") ?? textbox.Descendants(ns + "Style").FirstOrDefault() ?? new XElement(ns + "Style");
		return ReadColor(style.Element(ns + "Color")?.Value) ?? RenderColor.Black;
	}

	private static TextDirection ReadTextDirection(XElement textbox, XNamespace ns)
	{
		XElement style = textbox.Element(ns + "Style") ?? textbox.Descendants(ns + "Style").FirstOrDefault() ?? new XElement(ns + "Style");
		return style.Element(ns + "WritingMode")?.Value.Trim().ToLowerInvariant() switch
		{
			"tb-rl" or "tb-lr" or "vertical" => TextDirection.TopToBottom,
			"bt-rl" or "bt-lr" => TextDirection.BottomToTop,
			"rl-tb" => TextDirection.RightToLeft,
			_ => TextDirection.LeftToRight
		};
	}

	private static PlacedImage ReadImage(XElement image, XNamespace ns, RdlcDataContext context, IReadOnlyDictionary<string, RenderImageRequest> embeddedImages, float leftOffset = 0, float topOffset = 0, object? dataRow = null)
	{
		string source = image.Element(ns + "Source")?.Value ?? "External";
		string value = ResolveValue(image.Element(ns + "Value")?.Value ?? string.Empty, dataRow, context);
		string? mimeType = image.Element(ns + "MIMEType")?.Value;
		RenderImageRequest request = embeddedImages.TryGetValue(value, out RenderImageRequest? embedded)
			? embedded with { Source = source, MimeType = mimeType ?? embedded.MimeType }
			: new RenderImageRequest(source, value, mimeType, ReadOnlyMemory<byte>.Empty);
		RenderImage? resolved = context.ImageResolver?.Resolve(request);
		if (resolved is null)
		{
			throw new InvalidDataException($"The RDLC image '{value}' could not be resolved. Configure RdlcDataContext.ImageResolver.");
		}

		return new PlacedImage(resolved, new RenderRect(
			leftOffset + ParseSize(image.Element(ns + "Left")?.Value, 0),
			topOffset + ParseSize(image.Element(ns + "Top")?.Value, 0),
			ParseSize(image.Element(ns + "Width")?.Value, resolved.Width),
			ParseSize(image.Element(ns + "Height")?.Value, resolved.Height)));
	}

	private static PlacedChart ReadChart(XElement chart, XNamespace ns, RdlcDataContext context, float leftOffset = 0, float topOffset = 0)
	{
		string chartType = (chart.Attribute("ChartType")?.Value ?? chart.Element(ns + "ChartType")?.Value ?? string.Empty).Trim();
		RenderChartType type = chartType.ToLowerInvariant() switch
		{
			"" or "bar" => RenderChartType.Bar,
			"column" => RenderChartType.Column,
			"line" => RenderChartType.Line,
			"area" => RenderChartType.Area,
			"pie" => RenderChartType.Pie,
			"doughnut" => RenderChartType.Doughnut,
			_ => throw new NotSupportedException($"The constrained RDLC engine does not support '{chartType}' chart types.")
		};

		string dataSetName = chart.Element(ns + "DataSetName")?.Value ?? string.Empty;
		string categoryExpression = chart.Element(ns + "CategoryExpression")?.Value ?? "=Fields!Name.Value";
		string valueExpression = chart.Element(ns + "ValueExpression")?.Value ?? "=Fields!Amount.Value";
		var bars = new List<RenderChartBar>();
		foreach (object? row in ResolveRows(dataSetName, context))
		{
			string label = ResolveValue(categoryExpression, row, context);
			string valueText = ResolveValue(valueExpression, row, context);
			if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out float value) || float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
			{
				bars.Add(new RenderChartBar(label, value));
			}
		}

		return new PlacedChart(
			type,
			chart.Element(ns + "Title")?.Value ?? chart.Attribute("Name")?.Value ?? "Chart",
			bars,
			new RenderRect(leftOffset + ParseSize(chart.Element(ns + "Left")?.Value, 0), topOffset + ParseSize(chart.Element(ns + "Top")?.Value, 0), ParseSize(chart.Element(ns + "Width")?.Value, 360), ParseSize(chart.Element(ns + "Height")?.Value, 220)),
			ReadFont(chart, ns));
	}

	private static PlacedShape ReadRectangle(XElement rectangle, XNamespace ns, float leftOffset = 0, float topOffset = 0)
	{
		XElement style = rectangle.Element(ns + "Style") ?? new XElement(ns + "Style");
		return new PlacedShape(ReadBounds(rectangle, ns, leftOffset, topOffset), false, ReadColor(style.Element(ns + "BackgroundColor")?.Value), ReadColor(style.Element(ns + "BorderColor")?.Value), ReadSize(style.Element(ns + "BorderWidth")?.Value, 1));
	}

	private static PlacedShape ReadLine(XElement line, XNamespace ns, float leftOffset = 0, float topOffset = 0)
	{
		XElement style = line.Element(ns + "Style") ?? new XElement(ns + "Style");
		return new PlacedShape(ReadBounds(line, ns, leftOffset, topOffset), true, null, ReadColor(style.Element(ns + "BorderColor")?.Value) ?? RenderColor.Black, ReadSize(style.Element(ns + "BorderWidth")?.Value, 1));
	}

	private static RenderRect ReadBounds(XElement item, XNamespace ns, float leftOffset = 0, float topOffset = 0) => new(
		leftOffset + ParseSize(item.Element(ns + "Left")?.Value, 0),
		topOffset + ParseSize(item.Element(ns + "Top")?.Value, 0),
		ParseSize(item.Element(ns + "Width")?.Value, 100),
		ParseSize(item.Element(ns + "Height")?.Value, 20));

	private static RenderColor? ReadColor(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		string text = value.Trim();
		if (text.StartsWith('#') && text.Length == 7 && byte.TryParse(text[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) && byte.TryParse(text[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) && byte.TryParse(text[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
		{
			return new RenderColor(red, green, blue);
		}

		return text.ToLowerInvariant() switch
		{
			"black" => RenderColor.Black,
			"white" => RenderColor.White,
			"red" => new RenderColor(255, 0, 0),
			"green" => new RenderColor(0, 128, 0),
			"blue" => new RenderColor(0, 0, 255),
			_ => null
		};
	}

	private static float ReadSize(string? value, float fallback) => ParseSize(value, fallback);

	private static IReadOnlyDictionary<string, RenderImageRequest> ReadEmbeddedImages(XElement root, XNamespace ns)
	{
		var images = new Dictionary<string, RenderImageRequest>(StringComparer.OrdinalIgnoreCase);
		foreach (XElement image in root.Element(ns + "EmbeddedImages")?.Elements(ns + "EmbeddedImage") ?? Enumerable.Empty<XElement>())
		{
			string name = image.Attribute("Name")?.Value ?? string.Empty;
			string data = image.Element(ns + "ImageData")?.Value ?? string.Empty;
			if (name.Length == 0 || data.Length == 0)
			{
				continue;
			}

			try
			{
				images[name] = new RenderImageRequest("Embedded", name, image.Element(ns + "MIMEType")?.Value, Convert.FromBase64String(data));
			}
			catch (FormatException exception)
			{
				throw new InvalidDataException($"The embedded RDLC image '{name}' is not valid base64.", exception);
			}
		}

		return images;
	}

	private static float ParseSize(string? value, float fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return fallback;
		}
		string text = value.Trim().ToLowerInvariant();
		float factor = 1;
		if (text.EndsWith("cm", StringComparison.Ordinal))
		{
			factor = 72f / 2.54f;
			text = text[..^2];
		}
		else if (text.EndsWith("in", StringComparison.Ordinal))
		{
			factor = 72;
			text = text[..^2];
		}
		else if (text.EndsWith("pt", StringComparison.Ordinal))
		{
			text = text[..^2];
		}

		return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float points) ? points * factor : fallback;
	}

	private sealed record PlacedText(string Text, RenderPoint Baseline, FontRequest Font)
	{
		public RenderColor Color { get; init; } = RenderColor.Black;
		public TextDirection Direction { get; init; } = TextDirection.LeftToRight;
		public string? Hyperlink { get; init; }
	}
	private sealed record PlacedImage(RenderImage Image, RenderRect Destination);
	private sealed record PlacedChart(RenderChartType Type, string Title, IReadOnlyList<RenderChartBar> Bars, RenderRect Destination, FontRequest Font);
	private sealed record PlacedShape(RenderRect Bounds, bool IsLine, RenderColor? Fill, RenderColor? Stroke, float StrokeWidth);

	private sealed class OffsetRenderCanvas : IRenderCanvas
	{
		private readonly IRenderCanvas _inner;
		private readonly float _left;
		private readonly float _top;
		private readonly RenderSize _childSize;

		public OffsetRenderCanvas(IRenderCanvas inner, float left, float top, RenderSize childSize)
		{
			_inner = inner;
			_left = left;
			_top = top;
			_childSize = childSize;
		}

		public RenderSize Size => _inner.Size;

		public void Clear(RenderColor color) => _inner.FillRectangle(new RenderRect(_left, _top, _childSize.Width, _childSize.Height), color);
		public void FillRectangle(RenderRect rectangle, RenderColor color) => _inner.FillRectangle(Offset(rectangle), color);
		public void DrawRectangle(RenderRect rectangle, RenderColor color, float strokeWidth) => _inner.DrawRectangle(Offset(rectangle), color, strokeWidth);
		public void DrawLine(RenderPoint start, RenderPoint end, RenderColor color, float strokeWidth) => _inner.DrawLine(Offset(start), Offset(end), color, strokeWidth);
		public void DrawText(string text, RenderPoint baseline, FontRequest font, RenderColor color, TextDirection direction = TextDirection.LeftToRight) => _inner.DrawText(text, Offset(baseline), font, color, direction);
		public void DrawHyperlink(string text, RenderPoint baseline, FontRequest font, RenderColor color, string url, TextDirection direction = TextDirection.LeftToRight) => _inner.DrawHyperlink(text, Offset(baseline), font, color, url, direction);
		public void DrawImage(RenderImage image, RenderRect destination) => _inner.DrawImage(image, Offset(destination));
		public void DrawBarChart(string title, IReadOnlyList<RenderChartBar> bars, RenderRect destination, FontRequest font, RenderColor color) => _inner.DrawBarChart(title, bars, Offset(destination), font, color);
		public void DrawChart(RenderChartType chartType, string title, IReadOnlyList<RenderChartBar> points, RenderRect destination, FontRequest font, RenderColor color) => _inner.DrawChart(chartType, title, points, Offset(destination), font, color);
		public void Dispose() { }

		private RenderPoint Offset(RenderPoint point) => new(point.X + _left, point.Y + _top);
		private RenderRect Offset(RenderRect rectangle) => rectangle with { X = rectangle.X + _left, Y = rectangle.Y + _top };
	}
}
