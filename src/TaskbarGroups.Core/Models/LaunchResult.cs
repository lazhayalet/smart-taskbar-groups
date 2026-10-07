using System;
using System.Collections.Generic;
using TaskbarGroups.Core.Enums;

namespace TaskbarGroups.Core.Models
{
    /// <summary>Outcome of a launch attempt.</summary>
    /// <remarks>
    /// The legacy code surfaced launches through <c>MessageBox.Show(ex.Message)</c>,
    /// which gave the user nothing actionable. This record carries a stable error
    /// code for logging and a human-readable message for the UI, and it is the
    /// return type of every launcher so "open all" can report a summary.
    /// </remarks>
    public sealed class LaunchResult
    {
        private LaunchResult(bool success, ShortcutType launcherType, string resolvedTarget,
            LaunchErrorCode errorCode, string errorMessage, int? processId)
        {
            Success = success;
            LauncherType = launcherType;
            ResolvedTarget = resolvedTarget ?? string.Empty;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
            ProcessId = processId;
        }

        public bool Success { get; }

        public LaunchErrorCode ErrorCode { get; }

        /// <summary>Message suitable for a tooltip or a status line.</summary>
        public string ErrorMessage { get; }

        /// <summary>Process id when the launch produced a process we can track.</summary>
        public int? ProcessId { get; }

        /// <summary>What was actually launched, after resolving links and environment variables.</summary>
        public string ResolvedTarget { get; }

        public ShortcutType LauncherType { get; }

        public static LaunchResult Ok(ShortcutType launcherType, string resolvedTarget, int? processId = null)
            => new LaunchResult(true, launcherType, resolvedTarget, LaunchErrorCode.None, string.Empty, processId);

        public static LaunchResult Fail(ShortcutType launcherType, string resolvedTarget,
            LaunchErrorCode code, string message)
            => new LaunchResult(false, launcherType, resolvedTarget, code, message, null);

        public static LaunchResult Skipped(ShortcutType launcherType, string resolvedTarget,
            LaunchErrorCode code, string message)
            => new LaunchResult(false, launcherType, resolvedTarget, code, message, null);

        public override string ToString()
        {
            return Success
                ? string.Format("OK {0} -> {1}", LauncherType, ResolvedTarget)
                : string.Format("FAIL {0} -> {1}: {2} ({3})", LauncherType, ResolvedTarget, ErrorMessage, ErrorCode);
        }
    }

    /// <summary>Health of a single stored shortcut.</summary>
    public sealed class ShortcutHealthReport
    {
        public ShortcutHealthReport()
        {
            ShortcutId = Guid.Empty;
            Name = string.Empty;
            Target = string.Empty;
            Type = ShortcutType.Unknown;
            Status = ShortcutStatus.Unknown;
            Detail = string.Empty;
        }

        public Guid ShortcutId { get; set; }

        public string Name { get; set; }

        public string Target { get; set; }

        public ShortcutType Type { get; set; }

        public ShortcutStatus Status { get; set; }

        /// <summary>Concrete explanation, e.g. "target moved to X" or "access denied".</summary>
        public string Detail { get; set; }

        public bool NeedsAttention => Status != ShortcutStatus.Valid && Status != ShortcutStatus.UriLauncher;
    }

    /// <summary>Health of a whole installation.</summary>
    public sealed class ShortcutValidationReport
    {
        public List<ShortcutHealthReport> Items { get; } = new List<ShortcutHealthReport>();

        public int Total => Items.Count;

        public int Valid => CountOf(ShortcutStatus.Valid);

        public int Missing => CountOf(ShortcutStatus.Missing);

        public int Broken => CountOf(ShortcutStatus.InvalidShortcut);

        public int Denied => CountOf(ShortcutStatus.PermissionDenied);

        public int Disabled => CountOf(ShortcutStatus.Disabled);

        public bool IsClean => Broken == 0 && Missing == 0 && Denied == 0;

        private int CountOf(ShortcutStatus status)
        {
            int n = 0;
            foreach (var item in Items)
            {
                if (item.Status == status) n++;
            }
            return n;
        }
    }

    }