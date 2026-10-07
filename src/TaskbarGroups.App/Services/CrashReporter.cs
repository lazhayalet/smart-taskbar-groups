using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaskbarGroups.Core.Interfaces;

namespace TaskbarGroups.App.Services
{
    /// <summary>
    /// Catches what would otherwise terminate the process, and keeps the popup
    /// alive when one shortcut misbehaves.
    /// </summary>
    /// <remarks>
    /// Three handlers, because the .NET runtime raises unhandled exceptions in
    /// three different places and installing only one leaves real crashes
    /// unhandled:
    /// <list type="bullet">
    /// <item><see cref="Application.ThreadException"/> covers the UI thread.</item>
    /// <item><see cref="AppDomain.UnhandledException"/> covers other threads.</item>
    /// <item><see cref="TaskScheduler.UnobservedTaskException"/> covers async work
    /// whose task nobody awaited; those are observed and logged rather than
    /// rethrown, because by then there is nothing to recover.</item>
    /// </list>
    /// A stack trace is never the only thing the user sees. The dialog says what
    /// happened in one sentence, offers the log and the report, and keeps the
    /// application running when it is safe to.
    /// </remarks>
    public static class CrashReporter
    {
        private static ServiceContainer? _services;
        private static bool _installed;

        public static void Install(ServiceContainer services)
        {
            _services = services;

            if (_installed) return;
            _installed = true;

            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try
            {
                Log(SystemLogLevel.Error, "ui", "Unhandled exception on the UI thread", e.Exception);
                WriteCrashLog(e.Exception, "ui-thread");
            }
            catch (Exception)
            {
                // Nothing left to do if even this fails.
            }

            try
            {
                ShowDialog(e.Exception, recoverable: true);
            }
            catch (Exception)
            {
            }
        }

        private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                Log(SystemLogLevel.Critical, "app", "Unhandled exception on a background thread", exception);
                WriteCrashLog(exception, "background-thread");

                // The runtime is already tearing down, so this cannot be a dialog.
                // The crash log is the record.
            }
            catch (Exception)
            {
            }
        }

        private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                Log(SystemLogLevel.Error, "app", "Unobserved task exception", e.Exception);
                WriteCrashLog(e.Exception, "task");

                // Observed so the process is not torn down. The work that failed
                // has already reported its own failure to the user.
                e.SetObserved();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Records and reports a failure the caller caught itself.</summary>
        public static void ReportFatal(Exception exception, ServiceContainer services)
        {
            try
            {
                Log(SystemLogLevel.Critical, "app", "Fatal error during start-up", exception);
                WriteCrashLog(exception, "startup");

                ShowDialog(exception, recoverable: false, services: services);
            }
            catch (Exception)
            {
            }
        }

        private static void ShowDialog(Exception? exception, bool recoverable, ServiceContainer? services = null)
        {
            ServiceContainer? container = services ?? _services;
            string detail = Describe(exception);

            StringBuilder message = new StringBuilder();
            message.AppendLine(recoverable
                ? "Something went wrong, but Taskbar Groups is still running."
                : "Taskbar Groups could not start.");
            message.AppendLine();
            message.AppendLine(detail);
            message.AppendLine();

            if (container != null)
            {
                message.AppendLine("A crash report was written to:");
                message.AppendLine(container.Paths.LogsDirectory);
            }

            DialogResult answer = MessageBox.Show(
                message.ToString(),
                "Taskbar Groups",
                recoverable ? MessageBoxButtons.OKCancel : MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            if (answer != DialogResult.Cancel || container == null) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = container.Paths.LogsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// A one-sentence description the user can act on. The stack trace goes in
        /// the log, not in the dialog.
        /// </summary>
        internal static string Describe(Exception? exception)
        {
            if (exception == null) return "An unknown error occurred.";

            if (exception is UnauthorizedAccessException)
                return "Taskbar Groups does not have permission to perform that action.";

            if (exception is System.IO.FileNotFoundException || exception is System.IO.DirectoryNotFoundException)
                return "A file or folder that Taskbar Groups expected could not be found. It may have been moved or deleted.";

            if (exception is System.IO.IOException)
                return "A file was locked by another program. Close any program using the group and try again.";

            if (exception is TimeoutException)
                return "The operation took too long and was cancelled.";

            string message = exception.Message;

            // Long messages are usually an internal error the user cannot act on.
            if (message.Length > 220) message = message.Substring(0, 220) + "...";

            return message.Length == 0 ? exception.GetType().Name : message;
        }

        private static void WriteCrashLog(Exception? exception, string context)
        {
            ServiceContainer? container = _services;
            if (container == null || exception == null) return;

            try
            {
                string path = Path.Combine(container.Paths.LogsDirectory,
                    Core.Constants.LogFileNames.Crash);

                var builder = new StringBuilder();
                builder.AppendLine("=== " + DateTimeOffset.Now.ToString("O") + " (" + context + ") ===");
                builder.AppendLine(exception.ToString());
                builder.AppendLine();

                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private static void Log(SystemLogLevel level, string subsystem, string message, Exception? exception)
        {
            try
            {
                _services?.Logger.Log(level, subsystem, message, exception);
            }
            catch (Exception)
            {
            }
        }
    }
}