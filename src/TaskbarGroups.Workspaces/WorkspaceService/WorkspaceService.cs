using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;
using TaskbarGroups.Core.Validation;
using TaskbarGroups.Windows.Dpi;
using TaskbarGroups.Windows.Monitor;
using TaskbarGroups.Windows.WindowManagement;

namespace TaskbarGroups.Workspaces.WorkspaceService
{
    /// <summary>
    /// Opens a set of applications and arranges their windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A workspace is two things that have to happen in order: launch, then place.
    /// The launch phase runs on a delay so that each app has a chance to create its
    /// window before the placement phase looks for it, and each item waits for its
    /// own window with a timeout rather than assuming it appeared.
    /// </para>
    /// <para>
    /// Failure is per item. A workspace whose terminal cannot start still arranges
    /// its other three windows and reports one failure, because the point of a
    /// workspace is to get a working setup open.
    /// </para>
    /// </remarks>
    public sealed class WorkspaceService : IWorkspaceService
    {
        private readonly IWorkspaceRepository _repository;
        private readonly ILauncherResolver _launchers;
        private readonly IWindowManager _windows;
        private readonly MonitorService _monitors;
        private readonly LaunchContext _launchContext;
        private readonly IAppLogger? _logger;

        public WorkspaceService(
            IWorkspaceRepository repository,
            ILauncherResolver launchers,
            IWindowManager windows,
            MonitorService monitors,
            LaunchContext? launchContext = null,
            IAppLogger? logger = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _launchers = launchers ?? throw new ArgumentNullException(nameof(launchers));
            _windows = windows ?? throw new ArgumentNullException(nameof(windows));
            _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
            _launchContext = launchContext ?? LaunchContext.CreateDefault();
            _logger = logger;
        }

        /// <summary>How long to wait for a launched app to produce a window.</summary>
        public int PlacementTimeoutMs { get; set; } = 8000;

        public List<Workspace> GetWorkspaces()
        {
            return _repository.GetAll();
        }

        public Workspace CreateWorkspace(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A workspace needs a name.", nameof(name));

            var workspace = new Workspace
            {
                Name = name.Trim(),
                LaunchDelayMs = _launchContext.DefaultWorkingDirectory.Length > 0 ? 750 : 750
            };

            _repository.Upsert(workspace);
            _logger?.Log(SystemLogLevel.Information, "Workspace", "Created workspace " + workspace.Name);
            return workspace;
        }

        public Workspace DuplicateWorkspace(Guid id)
        {
            Workspace? source = _repository.GetById(id);
            if (source == null) throw new InvalidOperationException("That workspace no longer exists.");

            Workspace copy = source.Clone();
            copy.Name = NextAvailableName(source.Name + " copy");

            _repository.Upsert(copy);
            return copy;
        }

        public void DeleteWorkspace(Guid id)
        {
            _repository.Delete(id);
        }

        /// <summary>
        /// "Development" -&gt; "Development copy" -&gt; "Development copy 2" ...
        /// </summary>
        internal string NextAvailableName(string desired)
        {
            if (_repository.GetByName(desired) == null) return desired;

            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = desired + " " + suffix;
                if (_repository.GetByName(candidate) == null) return candidate;
            }

            return desired + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        public async Task<WorkspaceLaunchResult> LaunchWorkspaceAsync(
            Workspace workspace,
            IProgress<LaunchResult>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new WorkspaceLaunchResult { Success = true };

            if (workspace == null) throw new ArgumentNullException(nameof(workspace));

            _logger?.Log(SystemLogLevel.Information, "Workspace", "Launching workspace " + workspace.Name);

            // Phase 1: launch, in order, with the configured stagger.
            var items = workspace.Items
                .Where(i => i != null)
                .OrderBy(i => i.SortOrder)
                .ToList();

            int stagger = Math.Max(0, workspace.LaunchDelayMs);

            foreach (WorkspaceItem item in items)
            {
                if (cancellationToken.IsCancellationRequested) break;

                int delay = stagger + Math.Max(0, item.LaunchDelayMs);
                if (delay > 0)
                {
                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // An item with no executable is an "attach to what is already
                // running" entry, so there is nothing to launch.
                if (string.IsNullOrWhiteSpace(item.Executable))
                {
                    result.LaunchResults.Add(LaunchResult.Skipped(
                        ShortcutType.Unknown, string.Empty,
                        LaunchErrorCode.Disabled, "Attach-only entry"));
                    continue;
                }

                var shortcut = new ShortcutItem
                {
                    Type = DetectType(item.Executable),
                    Target = item.Executable,
                    WorkingDirectory = GuessWorkingDirectory(item.Executable),
                    Enabled = true,
                    RunAsAdministrator = false
                };

                LaunchResult launchResult = _launchers.Launch(shortcut, _launchContext);
                result.LaunchResults.Add(launchResult);
                progress?.Report(launchResult);

                if (!launchResult.Success)
                {
                    result.Success = false;
                    result.Message = string.IsNullOrEmpty(result.Message)
                        ? launchResult.ErrorMessage
                        : result.Message;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                result.Success = false;
                result.Message = "Cancelled.";
                return result;
            }

            // Phase 2: place the windows that appeared.
            foreach (WorkspaceItem item in items)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(item.ProcessMatch)) continue;

                IntPtr window = FindWorkspaceWindow(item, cancellationToken);
                if (window == IntPtr.Zero)
                {
                    result.WindowsNotFound++;
                    continue;
                }

                MonitorInfo? monitor = _monitors.GetMonitorByDeviceName(item.MonitorDeviceName)
                                        ?? _monitors.GetPrimary();

                double scale = monitor?.ScaleFactor ?? 1d;

                WindowPlacement placement = BuildPlacement(item, monitor);
                if (_windows.ApplyPlacement(window, placement, scale))
                    result.WindowsPlaced++;
                else
                    result.WindowsNotFound++;
            }

            if (result.WindowsNotFound > 0)
            {
                result.Success = false;
                result.Message = result.WindowsNotFound + " window(s) could not be arranged.";
            }

            return result;
        }

        /// <summary>
        /// Finds a workspace item's window. An executable name is preferred because
        /// window titles change and are localised; the title match is the fallback
        /// for apps that run under a shared host process.
        /// </summary>
        private IntPtr FindWorkspaceWindow(WorkspaceItem item, CancellationToken cancellationToken)
        {
            int timeout = Math.Max(200, PlacementTimeoutMs);

            if (!string.IsNullOrWhiteSpace(item.Executable))
            {
                string fileName = Path.GetFileName(item.Executable);
                IntPtr window = _windows.FindWindow(fileName);
                if (window != IntPtr.Zero) return window;
            }

            if (!string.IsNullOrWhiteSpace(item.ProcessMatch))
            {
                IntPtr window = _windows.FindWindow(item.ProcessMatch, matchTitle: true);
                if (window != IntPtr.Zero) return window;
            }

            // The app may still be starting. Poll until the timeout rather than
            // declaring it lost immediately.
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (cancellationToken.IsCancellationRequested) return IntPtr.Zero;

                Thread.Sleep(200);

                if (!string.IsNullOrWhiteSpace(item.Executable))
                {
                    IntPtr window = _windows.FindWindow(Path.GetFileName(item.Executable));
                    if (window != IntPtr.Zero) return window;
                }

                if (!string.IsNullOrWhiteSpace(item.ProcessMatch))
                {
                    IntPtr window = _windows.FindWindow(item.ProcessMatch, matchTitle: true);
                    if (window != IntPtr.Zero) return window;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Turns an item's anchor and monitor into a concrete placement, in the
        /// device-independent pixels <see cref="IWindowManager.ApplyPlacement"/>
        /// expects.
        /// </summary>
        internal static WindowPlacement BuildPlacement(WorkspaceItem item, MonitorInfo? monitor)
        {
            var placement = new WindowPlacement
            {
                Executable = item.Executable,
                ProcessMatch = item.ProcessMatch,
                MonitorId = monitor?.DeviceName ?? string.Empty,
                WindowState = item.WindowState
            };

            Rectangle workAreaDip = WorkAreaInDip(monitor);

            if (item.X != 0 || item.Y != 0)
            {
                // Explicit coordinates are stored in physical pixels by
                // "capture current layout", so they are passed through and scaled
                // back to physical by ApplyPlacement.
                placement.X = item.X;
                placement.Y = item.Y;
                placement.Width = item.Width > 0 ? item.Width : Math.Max(320, workAreaDip.Width / 2);
                placement.Height = item.Height > 0 ? item.Height : Math.Max(240, workAreaDip.Height / 2);
                return placement;
            }

            Rectangle target = WindowPlacementCalculator.Resolve(workAreaDip, item);
            placement.X = target.X;
            placement.Y = target.Y;
            placement.Width = target.Width;
            placement.Height = target.Height;

            return placement;
        }

        private static Rectangle WorkAreaInDip(MonitorInfo? monitor)
        {
            if (monitor == null) return new Rectangle(0, 0, 1920, 1040);

            // The work area is a point plus a size, and only the size is scaled,
            // so a monitor at a negative virtual-screen origin keeps its origin
            // relative to the desktop rather than being dragged towards it.
            double scale = monitor.ScaleFactor > 0 ? monitor.ScaleFactor : 1d;
            Size size = DpiService.PixelsToDip(monitor.WorkingArea.Size, scale);

            return new Rectangle(
                DpiService.PixelsToDip(monitor.WorkingArea.X, scale),
                DpiService.PixelsToDip(monitor.WorkingArea.Y, scale),
                size.Width,
                size.Height);
        }

        public WindowPlacement[] CaptureCurrentLayout()
        {
            return _windows.ListVisibleWindows().ToArray();
        }

        /// <summary>Saves the current on-screen windows as this workspace's layout.</summary>
        public void SaveCurrentLayout(Workspace workspace)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));

            IReadOnlyList<WindowPlacement> current = _windows.ListVisibleWindows();

            var placements = new List<WindowPlacement>();
            foreach (WindowPlacement window in current)
            {
                if (string.IsNullOrWhiteSpace(window.Executable)) continue;

                // Attach a captured window to a workspace item that launches the
                // same executable, so the capture is meaningful rather than a list
                // of unrelated windows.
                WorkspaceItem? owner = workspace.Items.FirstOrDefault(
                    i => i.Executable != null && PathsMatch(i.Executable, window.Executable));

                if (owner == null) continue;

                placements.Add(new WindowPlacement
                {
                    WorkspaceId = workspace.Id,
                    Executable = window.Executable,
                    ProcessMatch = window.ProcessMatch,
                    MonitorId = window.MonitorId,
                    X = window.X,
                    Y = window.Y,
                    Width = window.Width,
                    Height = window.Height,
                    WindowState = window.WindowState
                });
            }

            _repository.ReplacePlacements(workspace.Id, placements);
        }

        private static bool PathsMatch(string a, string b)
        {
            string first = System.IO.Path.GetFileName(a);
            string second = System.IO.Path.GetFileName(b);
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static ShortcutType DetectType(string target)
        {
            // A workspace item stores an executable path, not a shortcut record.
            // Classification is deliberately narrow: anything that is not a
            // script is launched as a file through the shell.
            string extension = System.IO.Path.GetExtension(target ?? string.Empty).ToLowerInvariant();
            switch (extension)
            {
                case ".bat": return ShortcutType.Bat;
                case ".cmd": return ShortcutType.Cmd;
                case ".ps1": return ShortcutType.Ps1;
                case ".url": return ShortcutType.Url;
                default: return ShortcutType.Exe;
            }
        }

        private static string GuessWorkingDirectory(string executable)
        {
            try
            {
                return System.IO.Path.GetDirectoryName(StringHelpers.ExpandPath(executable)) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}