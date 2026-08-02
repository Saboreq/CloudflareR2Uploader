using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CloudflareR2Uploader.Forms;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader
{
    internal static class Program
    {
        private static LoggingService _log;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Enable TLS 1.2/1.3 before any request is made.
            R2ClientFactory.ConfigureTransportSecurity();

            try
            {
                AppPaths.EnsureAllDirectories();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The application data folder could not be created:" + Environment.NewLine + Environment.NewLine +
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.AppFolderName) +
                    Environment.NewLine + Environment.NewLine + ex.Message,
                    "Cloudflare R2 Uploader",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            _log = new LoggingService();
            _log.Info("App.Start", "Cloudflare R2 Uploader " + GetVersion() + " starting on " + Environment.OSVersion.VersionString + ".");

            // Nothing should reach the user as an unhandled crash dialog.
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            try
            {
                using (MainForm form = new MainForm(_log))
                {
                    Application.Run(form);
                }
            }
            finally
            {
                _log.Info("App.Exit", "Cloudflare R2 Uploader closing.");
                _log.Dispose();
            }
        }

        internal static string GetVersion()
        {
            try
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "1.0" : version.ToString(3);
            }
            catch (Exception)
            {
                return "1.0";
            }
        }

        /// <summary>Loads the embedded application icon. Returns null if it cannot be read.</summary>
        internal static Icon LoadApplicationIcon()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("CloudflareR2Uploader.Resources.app.ico"))
                {
                    if (stream != null) return new Icon(stream);
                }
            }
            catch (Exception)
            {
                // A missing icon must never stop the window from opening.
            }
            return null;
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleFatal("UI thread", e.Exception, false);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleFatal("background thread", e.ExceptionObject as Exception, e.IsTerminating);
        }

        private static void OnUnobservedTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            if (_log != null)
                _log.Error("App.UnobservedTask", "A background task failed without being observed.", e.Exception);

            // Observing it keeps the process alive; the upload that owned it has already failed
            // through its own error path.
            e.SetObserved();
        }

        private static void HandleFatal(string origin, Exception exception, bool isTerminating)
        {
            if (_log != null) _log.Error("App.Unhandled", "Unhandled exception on the " + origin + ".", exception);

            try
            {
                string message =
                    "Something went wrong and the action could not be completed." + Environment.NewLine + Environment.NewLine +
                    (exception == null ? "No details are available." : LoggingService.Sanitize(exception.Message)) +
                    Environment.NewLine + Environment.NewLine +
                    "A sanitised entry has been written to the log folder.";

                using (ErrorDialog dialog = new ErrorDialog(
                    isTerminating ? "The application has to close" : "Unexpected error",
                    message,
                    FriendlyErrorService.BuildTechnicalDetails(exception, null, null)))
                {
                    dialog.ShowDialog();
                }
            }
            catch (Exception)
            {
                // The dialog itself failed; there is nothing further to try.
            }
        }
    }
}
