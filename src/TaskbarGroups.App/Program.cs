using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TaskbarGroups.App.Configuration;
using TaskbarGroups.App.Services;
using TaskbarGroups.Core.Interfaces;

namespace TaskbarGroups.App
{
    /// <summary>
    /// Process entry point.
    /// </summary>
    /// <remarks>
    /// The same executable serves two roles, which is the mechanism the whole
    /// product depends on. Invoked with no arguments it opens the dashboard;
    /// invoked with a group name it opens that group's popup. The taskbar shortcut
    /// each group owns launches this executable with its name as argument 1 and a
    /// per-group AppUserModelID, and Windows groups the resulting button by that
    /// identifier. Users who pinned a group under 1.x therefore keep working after
    /// the upgrade, which is why the argument contract in
    /// <c>AppContract</c> must not change.
    /// </remarks>
    internal static class Program
    {
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

        [STAThread]
        private static int Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppRuntime runtime = AppRuntime.Create();

            // The global handlers are installed before anything else runs, so an
            // exception during start-up is reported the same way as one later on.
            CrashReporter.Install(runtime.Services);

            string[] arguments = args ?? Array.Empty<string>();

            try
            {
                if (arguments.Length > 0)
                {
                    string groupName = ExtractGroupName(arguments);
                    if (groupName.Length == 0)
                    {
                        runtime.Services.Logger.Log(SystemLogLevel.Warning, "App",
                            "Started with no usable group argument; opening the dashboard instead.");
                        runtime.StartDashboard();
                        return 0;
                    }

                    SetCurrentProcessExplicitAppUserModelID(
                        Core.Constants.AppContract.BuildGroupAppId(groupName));

                    runtime.StartGroupPopup(groupName);
                    return 0;
                }

                SetCurrentProcessExplicitAppUserModelID(Core.Constants.AppContract.MainAppId);
                runtime.StartDashboard();
                return 0;
            }
            catch (Exception ex)
            {
                CrashReporter.ReportFatal(ex, runtime.Services);
                return 1;
            }
        }

        /// <summary>
        /// Reads the group name from the command line.
        /// </summary>
        /// <remarks>
        /// Accepts both the 1.x form (the bare group name as argument 1) and a
        /// <c>--group &lt;name&gt;</c> pair. The Explorer context-menu integration
        /// appends <c>--add &lt;path&gt;</c>, which is handled separately by the
        /// dashboard.
        /// </remarks>
        internal static string ExtractGroupName(string[] arguments)
        {
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];

                if (argument.StartsWith(Core.Constants.AppContract.GroupArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    // Either "--group Name" or "--group=Name".
                    string attached = argument.Substring(Core.Constants.AppContract.GroupArgumentName.Length).TrimStart('=', ':');
                    if (attached.Length > 0) return attached;

                    if (index + 1 < arguments.Length) return arguments[index + 1];
                    return string.Empty;
                }

                if (argument.Equals("--add", StringComparison.OrdinalIgnoreCase) ||
                    argument.Equals("--tray", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (argument.StartsWith("-", StringComparison.Ordinal)) continue;

                return argument;
            }

            return string.Empty;
        }
    }
}