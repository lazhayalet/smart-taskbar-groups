using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TaskbarGroups.Core.Constants;
using TaskbarGroups.Core.Interfaces;

namespace TaskbarGroups.Diagnostics.HealthChecks
{
    /// <summary>
    /// A rolling file logger.
    /// </summary>
    /// <remarks>
    /// Written directly rather than through a logging framework so the
    /// diagnostics subsystem has no package dependency and so a crash in the
    /// application still produces a usable log: there is no logger initialisation
    /// that can itself fail.
    ///
    /// Five separate files, because the questions people ask are different. A user
    /// reporting "a shortcut did not open" needs <c>launcher.log</c> and nothing
    /// else; a user reporting a migration problem needs <c>migration.log</c>.
    /// Rotation is size-based and keeps a bounded number of previous files, so a
    /// long-running install cannot fill the disk.
    /// </remarks>
    public sealed class FileLogger : IAppLogger, IDisposable
    {
        private const long MaxBytesPerFile = 2 * 1024 * 1024;
        private const int MaxRotatedFiles = 3;

        private static readonly string[] AllFiles =
        {
            LogFileNames.Application,
            LogFileNames.Launcher,
            LogFileNames.Migration,
            LogFileNames.Diagnostics,
            LogFileNames.Crash
        };

        private readonly ConcurrentDictionary<string, object> _locks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private readonly string _directory;
        private readonly SystemLogLevel _minimum;
        private readonly bool _enabled;
        private bool _disposed;

        public FileLogger(string directory, SystemLogLevel minimum = SystemLogLevel.Information, bool enabled = true)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("A log directory is required.", nameof(directory));

            _directory = directory;
            _minimum = minimum;
            _enabled = enabled;

            try
            {
                Directory.CreateDirectory(_directory);
            }
            catch (Exception)
            {
                // A log directory we cannot create disables logging rather than
                // preventing the application from starting.
                _enabled = false;
            }
        }

        public SystemLogLevel MinimumLevel => _minimum;

        public bool IsEnabled(SystemLogLevel level)
        {
            return _enabled && level >= _minimum;
        }

        public void Log(SystemLogLevel level, string subsystem, string message, Exception? exception = null)
        {
            if (!IsEnabled(level)) return;
            if (_disposed) return;

            try
            {
                string file = FileForSubsystem(subsystem);

                // One lock per file: several threads log concurrently, but a
                // half-written line is worse than a small amount of contention.
                object gate = _locks.GetOrAdd(file, _ => new object());

                lock (gate)
                {
                    RotateIfNeeded(file);

                    StringBuilder line = new StringBuilder(256);
                    line.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
                    line.Append(" [").Append(Abbreviate(level)).Append("] ");
                    line.Append(SafeSubsystem(subsystem)).Append(": ");
                    line.Append(Sanitize(message));

                    if (exception != null)
                    {
                        line.AppendLine();
                        line.Append(exception);
                    }

                    File.AppendAllText(Path.Combine(_directory, file), line.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // Logging must never be the reason the application fails.
            }
        }

        /// <summary>Paths of every log file, for the diagnostics page.</summary>
        public IReadOnlyList<string> DescribeFiles()
        {
            var paths = new List<string>(AllFiles.Length);
            foreach (string file in AllFiles)
                paths.Add(Path.Combine(_directory, file));
            return paths;
        }

        /// <summary>Copies every log into a folder, for a support bundle.</summary>
        public string ExportLogs(string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string file in AllFiles)
            {
                string source = Path.Combine(_directory, file);
                if (!File.Exists(source)) continue;

                try
                {
                    File.Copy(source, Path.Combine(destinationDirectory, file), overwrite: true);
                }
                catch (Exception)
                {
                }
            }

            return destinationDirectory;
        }

        /// <summary>Removes rotated files beyond the retention count.</summary>
        public int Prune()
        {
            int removed = 0;
            try
            {
                foreach (string file in Directory.GetFiles(_directory, "*.log.*"))
                {
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return removed;
        }

        private static string FileForSubsystem(string? subsystem)
        {
            string name = (subsystem ?? string.Empty).ToLowerInvariant();

            if (name.Contains("launch")) return LogFileNames.Launcher;
            if (name.Contains("migrat")) return LogFileNames.Migration;
            if (name.Contains("diagnos")) return LogFileNames.Diagnostics;
            if (name.Contains("crash")) return LogFileNames.Crash;

            return LogFileNames.Application;
        }

        private void RotateIfNeeded(string file)
        {
            string path = Path.Combine(_directory, file);

            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytesPerFile) return;

            // Shift .2 -> .3, .1 -> .2, base -> .1
            for (int index = MaxRotatedFiles - 1; index >= 1; index--)
            {
                string from = path + "." + index;
                string to = path + "." + (index + 1);
                if (File.Exists(from)) File.Move(from, to, overwrite: true);
            }

            File.Move(path, path + ".1", overwrite: true);
        }

        private static string Abbreviate(SystemLogLevel level)
        {
            switch (level)
            {
                case SystemLogLevel.Trace: return "TRC";
                case SystemLogLevel.Debug: return "DBG";
                case SystemLogLevel.Information: return "INF";
                case SystemLogLevel.Warning: return "WRN";
                case SystemLogLevel.Error: return "ERR";
                case SystemLogLevel.Critical: return "CRT";
                default: return "???";
            }
        }

        private static string SafeSubsystem(string? subsystem)
        {
            if (string.IsNullOrWhiteSpace(subsystem)) return "app";
            return subsystem!.Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>
        /// Strips anything that could break a one-record-per-line format. Windows
        /// paths are kept intact; only control characters go.
        /// </summary>
        private static string Sanitize(string? message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            StringBuilder builder = new StringBuilder(message!.Length);
            foreach (char c in message)
            {
                builder.Append(c == '\r' || c == '\n' ? ' ' : c);
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}