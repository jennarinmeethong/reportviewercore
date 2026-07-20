using System.Collections;
using Microsoft.Reporting.WinForms;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();

using var definition = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Report.rdlc"));
using var report = new LocalReport();
report.LoadReportDefinition(definition);
report.DataSources.Add(new ReportDataSource("Items", new[]
{
	new { Description = "Alpha", Price = 10.5m, Qty = 2, Total = 21m },
	new { Description = "Beta", Price = 7.25m, Qty = 3, Total = 21.75m }
}));

using var form = new Form
{
	Text = "ReportViewer Core v2 — Windows adapter",
	WindowState = FormWindowState.Maximized
};
var viewer = new PortableReportViewer
{
	Dock = DockStyle.Fill,
	Document = report.CreatePortableDocument()
};
form.Controls.Add(viewer);
Application.Run(form);
