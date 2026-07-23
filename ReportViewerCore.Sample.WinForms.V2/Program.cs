using Microsoft.Reporting.WinForms;
using System.Windows.Forms;

internal static class Program
{
	[STAThread]
	private static int Main()
	{
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += (_, eventArgs) => ReportFailure(eventArgs.Exception);
		AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
		{
			if (eventArgs.ExceptionObject is Exception exception)
			{
				WriteCrashLog(exception);
			}
		};

		try
		{
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
			return 0;
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
			return 1;
		}
	}

	private static void ReportFailure(Exception exception)
	{
		string logPath = WriteCrashLog(exception);
		try
		{
			MessageBox.Show($"ReportViewerCore Windows sample failed.\n\nDetails: {logPath}\n\n{exception.Message}", "ReportViewerCore sample error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		catch
		{
			// The crash log remains the diagnostic source when the desktop message box cannot be created.
		}
	}

	private static string WriteCrashLog(Exception exception)
	{
		string logPath = Path.Combine(Path.GetTempPath(), "ReportViewerCore.WinForms.V2.crash.log");
		try
		{
			File.WriteAllText(logPath, exception.ToString());
		}
		catch
		{
			// Preserve the original failure even if the temporary directory is unavailable.
		}

		return logPath;
	}
}
