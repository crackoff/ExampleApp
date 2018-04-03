using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace FileCheker
{
    /// <summary>
    /// Static class for file monitoring process
    /// </summary>
    internal static class Worker
    {
        /// <summary>
        /// Interval for checking the files
        /// </summary>
        /// <remarks>10 seconds by requirements</remarks>
        private const long CheckPeriodMilliseconds = 10 * 1000;

        /// <summary>
        /// Maximum lock period for file
        /// </summary>
        /// <remarks>There is no concrete value in requiremens</remarks>
        private const uint MaxFileLockPeriodMilliseconds = 5 * 1000;

        /// <summary>
        /// Option for search only directory: only top or all dolder and subfolders
        /// </summary>
        /// <remarks>There is no requirement for this option, so used default behavior.</remarks>
        private const SearchOption FolderSearchOption = SearchOption.TopDirectoryOnly;

        /// <summary>
        /// Flag for checking running status
        /// </summary>
        private static int _isStarted = 0;

        /// <summary>
        /// Flag to check if it is a first run of timer
        /// </summary>
        private static bool _isFirstRun = true;

        /// <summary>
        /// Timer for checking the files
        /// </summary>
        [CanBeNull]
        private static Timer _checkTimer;

        /// <summary>
        /// Folder to monitored files
        /// </summary>
        private static string _path;

        /// <summary>
        /// Mask of files which to monitor
        /// </summary>
        private static string _mask;

        /// <summary>
        /// Object, used for synchronize work with timer 
        /// </summary>
        [NotNull]
        private static readonly object LockObj = new object();

        /// <summary>
        /// Dictionary to store current state of monitorrd files with number of lines and modified date
        /// </summary>
        [NotNull] private static readonly ConcurrentDictionary<string, (DateTime, int)> FileLines =
            new ConcurrentDictionary<string, (DateTime, int)>();

        /// <summary>
        /// Entry point for <see cref="Worker"/> class logic
        /// </summary>
        /// <remarks>You can run this process only once</remarks>
        /// <param name="path">Folder to monitored files</param>
        /// <param name="mask">Mask of files which to monitor</param>
        /// <exception cref="InvalidOperationException">The process is already running.</exception>
        public static void StartMonitoring([NotNull] string path, [NotNull] string mask)
        {
            // Check if already running
            if (Interlocked.CompareExchange(ref _isStarted, 1, 0) != 0)
            {
                throw new InvalidOperationException("The process is already running.");
            }

            _path = path;
            _mask = mask;

            // Initialize timer and run it immediately
            lock (LockObj)
            {
                _checkTimer = new Timer(CheckFilesProc, null, 0, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Method to stop the monitoring
        /// </summary>
        public static void Stop()
        {
            lock (LockObj)
            {
                _checkTimer?.Dispose();
                _checkTimer = null;
            }
        }

        /// <summary>
        /// Checks the files by mask in the folder and prints out the changes
        /// </summary>
        /// <remarks>Called by timer</remarks>
        /// <param name="state">Timer parameter. Not used.</param>
        private static void CheckFilesProc(object state)
        {
            var sw = new Stopwatch();
            sw.Start();

            var files = Directory.GetFiles(_path, _mask, FolderSearchOption);

            // Check for add or update
            Parallel.ForEach(files, file =>
            {
                FileLines.AddOrUpdate(file,
                    s =>
                    {
                        var fileParams = GetFileParams(file);
                        if (!_isFirstRun) Console.WriteLine($"Added: [{file}] of {fileParams.Item2} lines");
                        return fileParams;
                    },
                    (s, currentParams) =>
                    {
                        var fileParams = GetFileParams(file);
                        if (fileParams.Item1 <= currentParams.Item1) return currentParams;
                        if (fileParams.Item2 < 0) return currentParams; // File deleted while processing.

                        var delta = fileParams.Item2 - currentParams.Item2;
                        if (!_isFirstRun) Console.WriteLine($"Modified: [{file}] is now {fileParams.Item2} lines ({(delta > 0 ? "+" : "")}{delta})");
                        return fileParams;

                    });
            });

            // Check for delete
            foreach (var key in FileLines.Keys)
            {
                if (Array.Exists(files, s => s == key)) continue;

                Console.WriteLine($"Deleted: [{key}]");
                FileLines.TryRemove(key, out _);
            }

            _isFirstRun = false;

            // Initialize time for next check
            lock (LockObj)
            {
                sw.Stop();
                var nextPeriod = sw.ElapsedMilliseconds >= CheckPeriodMilliseconds
                    ? 0
                    : CheckPeriodMilliseconds - sw.ElapsedMilliseconds;
                _checkTimer?.Change(nextPeriod, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Read modified date of file and number of lines inside it
        /// </summary>
        /// <param name="file">Full path of file</param>
        private static (DateTime, int) GetFileParams([NotNull] string file)
        {
            // Time to sleep the thread if file is locked
            const int sleepPeriod = 100;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (sw.ElapsedMilliseconds < MaxFileLockPeriodMilliseconds)
            {
                try
                {
                    var modifiedDate = File.GetLastWriteTime(file);
                    var cnt = 0;
                    using (var reader = File.OpenText(file))
                    {
                        while (reader.ReadLine() != null)
                        {
                            cnt++;
                        }
                    }

                    return (modifiedDate, cnt);
                }
                catch (FileNotFoundException)
                {
                    // File deleted while processing
                    return (DateTime.MinValue, -1);
                }
                catch (UnauthorizedAccessException)
                {
                    // File is locked
                    Thread.Sleep(sleepPeriod);
                }
            }

            throw new TimeoutException($"File [{file}] is locked too long time.");
        }
    }
}
