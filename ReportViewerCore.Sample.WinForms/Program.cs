using Microsoft.Reporting.WinForms;
using System;
using System.IO;
using System.Windows.Forms;

namespace ReportViewerCore
{
	class Program
	{
		[STAThread]
		static void Main(string[] args)
		{
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += (_, eventArgs) => ReportFailure(eventArgs.Exception);
			try
			{
				using var form = new ReportViewerForm();
				form.ShowDialog();
			}
			catch (Exception exception)
			{
				ReportFailure(exception);
			}
		}

		static void ReportFailure(Exception exception)
		{
			string logPath = Path.Combine(Path.GetTempPath(), "ReportViewerCore.Sample.WinForms.crash.log");
			try { File.WriteAllText(logPath, exception.ToString()); } catch { }
			try { MessageBox.Show($"Windows sample failed.\n\nDetails: {logPath}\n\n{exception.Message}", "ReportViewerCore sample error", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
		}
	}
}
