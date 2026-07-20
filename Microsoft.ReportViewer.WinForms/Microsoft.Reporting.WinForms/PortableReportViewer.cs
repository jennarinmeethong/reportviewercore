#if NET10_0_OR_GREATER
using ReportViewerCore.Rendering.Windows;

namespace Microsoft.Reporting.WinForms
{
	/// <summary>
	/// Explicit v2 WinForms adapter. It displays pages produced by the shared Skia backend;
	/// the legacy ReportViewer control and its GDI pipeline remain unchanged.
	/// </summary>
	public sealed class PortableReportViewer : WindowsReportViewerControl
	{
	}
}
#endif
