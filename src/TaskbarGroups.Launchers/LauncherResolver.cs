using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TaskbarGroups.Core.Enums;
using TaskbarGroups.Core.Interfaces;
using TaskbarGroups.Core.Models;

namespace TaskbarGroups.Launchers
{
    /// <summary>
    /// Picks the right launcher for an item and drives it.
    /// </summary>
    /// <remarks>
    /// This type is the only place that decides how a target is started. The
    /// legacy code had two places - <c>frmMain.OpenFile</c> and
    /// <c>ucShortcut.ucShortcut_Click</c> - which disagreed with each other: the
    /// second one special-cased Store apps, the first one did not, so the same
    /// shortcut behaved differently depending on which control the user clicked.
    ///
    /// Resolution order is deterministic and type-first, with a conservative
    /// re-classification fallback: an item whose stored type is
    /// <see cref="ShortcutType.Unknown"/> or stale is classified again from its
    /// target before a launcher is chosen, so a hand-edited database or a migrated
    /// record still launches correctly.
    /// </remarks>
    public sealed class LauncherResolver : ILauncherResolver
    {
        private readonly Dictionary<ShortcutType, IApplicationLauncher> _byType;
        private readonly List<IApplicationLauncher> _launchers;
        private readonly IShortcutResolver? _shortcutResolver;
        private readonly IAppLogger? _logger;

        public LauncherResolver(
            IEnumerable<IApplicationLauncher> launchers,
            IShortcutResolver? shortcutResolver = null,
            IAppLogger? logger = null)
        {
            _launchers = (launchers ?? throw new ArgumentNullException(nameof(launchers))).Where(l => l != null).ToList();
            _shortcutResolver = shortcutResolver;
            _logger = logger;

            _byType = new Dictionary<ShortcutType, IApplicationLauncher>();
            foreach (var launcher in _launchers)
            {
                foreach (ShortcutType type in launcher.SupportedTypes)
                {
                    // First registration wins, so launcher order in the composition
                    // root is the priority order.
                    if (!_byType.ContainsKey(type))
                        _byType[type] = launcher;
                }
            }
        }

        /// <summary>Builds the default launcher set.</summary>
        /// <remarks>
        /// A <see cref="Detection.ShortcutResolver"/> is always created unless one
        /// is supplied. Without it, the resolver's fallback path - reclassifying an
        /// item whose stored type is stale or unknown - has nothing to classify
        /// with, and a hand-edited or migrated record would be refused instead of
        /// launched.
        /// </remarks>
        public static LauncherResolver CreateDefault(
            Func<string, string?>? pwaBrowserLocator = null,
            IAppLogger? logger = null,
            IShortcutResolver? shortcutResolver = null)
        {
            IShortcutResolver resolver = shortcutResolver ?? new Detection.ShortcutResolver();

            var launchers = new IApplicationLauncher[]
            {
                new global::TaskbarGroups.Launchers.Launchers.ExeLauncher(),
                new global::TaskbarGroups.Launchers.Launchers.LnkLauncher(),
                new global::TaskbarGroups.Launchers.Launchers.FolderLauncher(),
                new global::TaskbarGroups.Launchers.Launchers.UwpLauncher(),
                new global::TaskbarGroups.Launchers.Launchers.SteamLauncher(),
                new global::TaskbarGroups.Launchers.Launchers.PwaLauncher(pwaBrowserLocator ?? (_ => null)),
                new global::TaskbarGroups.Launchers.Launchers.ScriptLauncher(logger),
                new global::TaskbarGroups.Launchers.Launchers.UrlLauncher(),
                new Launchers.ProtocolLauncher(
                    ShortcutType.Epic, ShortcutType.EA, ShortcutType.Ubisoft,
                    ShortcutType.Gog, ShortcutType.BattleNet, ShortcutType.Xbox)
            };

            return new LauncherResolver(launchers, resolver, logger);
        }

        public IApplicationLauncher? Resolve(ShortcutItem item)
        {
            if (item == null) return null;

            if (_byType.TryGetValue(item.Type, out IApplicationLauncher? launcher))
            {
                if (launcher.CanLaunch(item)) return launcher;
            }

            // The stored type is unknown or no longer matches the target. Classify
            // again rather than refusing.
            ShortcutType reclassified = Reclassify(item);
            if (reclassified != item.Type && _byType.TryGetValue(reclassified, out IApplicationLauncher? fallback))
            {
                return fallback;
            }

            // Last resort: ask each launcher whether it wants the item.
            foreach (var candidate in _launchers)
            {
                try
                {
                    if (candidate.CanLaunch(item)) return candidate;
                }
                catch (Exception)
                {
                    // A launcher that throws while being asked must not stop the
                    // others from being considered.
                }
            }

            return null;
        }

        private ShortcutType Reclassify(ShortcutItem item)
        {
            if (_shortcutResolver == null) return item.Type;

            try
            {
                return _shortcutResolver.DetectType(item.Target);
            }
            catch (Exception)
            {
                return item.Type;
            }
        }

        public LaunchResult Launch(ShortcutItem item, LaunchContext context)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            context ??= LaunchContext.CreateDefault();

            // An item with no target is a data problem, not a routing problem, and
            // it gets its own error code so the UI can say "this shortcut has no
            // target" rather than "nothing knows how to open this".
            if (string.IsNullOrWhiteSpace(item.Target))
            {
                return LaunchResult.Fail(
                    item.Type,
                    string.Empty,
                    LaunchErrorCode.EmptyTarget,
                    "This shortcut has no target.");
            }

            IApplicationLauncher? launcher = Resolve(item);
            if (launcher == null)
            {
                _logger?.Log(SystemLogLevel.Warning, "Launcher",
                    "No launcher for " + item.Type + " -> " + item.Target);

                return LaunchResult.Fail(
                    item.Type,
                    item.Target ?? string.Empty,
                    LaunchErrorCode.UnsupportedType,
                    string.IsNullOrWhiteSpace(item.Target)
                        ? "This shortcut has no target."
                        : "Nothing on this system knows how to open this kind of shortcut.");
            }

            try
            {
                LaunchResult result = launcher.Launch(item, context);

                _logger?.Log(
                    result.Success ? SystemLogLevel.Information : SystemLogLevel.Warning,
                    "Launcher",
                    result.ToString());

                return result;
            }
            catch (Exception ex)
            {
                // A launcher that escapes with an exception is a bug, but it must
                // still not take a group popup down with it.
                _logger?.Log(SystemLogLevel.Error, "Launcher", "Launcher threw for " + item.Target, ex);
                return LaunchResult.Fail(item.Type, item.Target ?? string.Empty, LaunchErrorCode.Exception, ex.Message);
            }
        }

        public LaunchBatchResult LaunchAll(IEnumerable<ShortcutItem> items, LaunchContext context, IProgress<LaunchResult>? progress = null)
        {
            var batch = new LaunchBatchResult();
            if (items == null) return batch;

            context ??= LaunchContext.CreateDefault();

            foreach (ShortcutItem item in items)
            {
                if (item == null) continue;

                // One failure must not stop the rest: a group with one broken
                // shortcut should still open the other nine.
                LaunchResult result = Launch(item, context);
                batch.Results.Add(result);
                progress?.Report(result);
            }

            return batch;
        }

        /// <summary>
        /// Launches a group's shortcuts with the configured stagger, skipping
        /// disabled and missing items. Used by Ctrl+Enter and by the dashboard.
        /// </summary>
        public LaunchBatchResult OpenAll(
            IEnumerable<ShortcutItem> items,
            OpenAllOptions options,
            LaunchContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var batch = new LaunchBatchResult();
            if (items == null) return batch;

            context ??= LaunchContext.CreateDefault();
            options ??= new OpenAllOptions();

            foreach (ShortcutItem item in items)
            {
                if (item == null) continue;

                if (cancellationToken.IsCancellationRequested)
                {
                    batch.Results.Add(LaunchResult.Skipped(item.Type, item.Target ?? string.Empty,
                        LaunchErrorCode.Disabled, "Cancelled."));
                    break;
                }

                if (options.SkipDisabled && !item.Enabled)
                {
                    batch.Results.Add(LaunchResult.Skipped(item.Type, item.Target ?? string.Empty,
                        LaunchErrorCode.Disabled, "Disabled"));
                    continue;
                }

                if (options.SkipMissing && item.Type != ShortcutType.Url &&
                    !item.Type.IsShellActivated() &&
                    !Core.Validation.ShortcutValidator.Classify(item).Equals(ShortcutStatus.Valid))
                {
                    batch.Results.Add(LaunchResult.Skipped(item.Type, item.Target ?? string.Empty,
                        LaunchErrorCode.TargetNotFound, "Target is missing"));
                    continue;
                }

                batch.Results.Add(Launch(item, context));

                if (options.DelayMs > 0)
                    Thread.Sleep(options.DelayMs);
            }

            return batch;
        }
    }
}