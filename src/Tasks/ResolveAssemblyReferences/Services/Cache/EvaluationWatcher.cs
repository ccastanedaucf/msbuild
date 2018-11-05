using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

using Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Services.Cache
{
    internal class EvaluationWatcher : IEvaluationWatcher
    {
        private struct FileSystemChange
        {
            internal string Path { get; }

            internal DateTime ChangeTime { get; }

            internal FileSystemChange(string path)
            {
                Path = path;
                ChangeTime = DateTime.Now;
            }
        }

        private readonly ConcurrentDictionary<string, DateTime> _directoryToLastModifiedTime =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, DateTime> _fileToLastModifiedTime =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, FileSystemWatcher> _directoryToWatcher =
            new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentQueue<FileSystemChange> _fileSystemChangeQueue = new ConcurrentQueue<FileSystemChange>();

        public bool IsClean(ResolveAssemblyReferenceEvaluation evaluation)
        {
            return IsAnyModifiedNewer(_directoryToLastModifiedTime, evaluation.TouchedDirectories, evaluation.LastVerifiedCleanTime)
                   && IsAnyModifiedNewer(_fileToLastModifiedTime, evaluation.TouchedFiles, evaluation.LastVerifiedCleanTime);
        }

        public void Watch(ResolveAssemblyReferenceEvaluation evaluation)
        {
            foreach (string directory in evaluation.TouchedDirectories)
            {
                WatchDirectory(directory);
            }
            foreach (string file in evaluation.TouchedFiles)
            {
                WatchFile(file);
            }

            evaluation.MarkClean();
        }

        private static bool IsAnyModifiedNewer
        (
            ConcurrentDictionary<string, DateTime> upToDateDict,
            HashSet<string> potentialOutOfDateSet,
            DateTime lastVerifiedCleanTime
        )
        {
            foreach (string key in potentialOutOfDateSet)
            {
                if (!upToDateDict.TryGetValue(key, out DateTime upToDateTime))
                {
                    return false;
                }
                if (upToDateTime > lastVerifiedCleanTime)
                {
                    return false;
                }
            }

            return true;
        }

        private void WatchDirectory(string path)
        {
            if (_directoryToWatcher.ContainsKey(path))
            {
                return;
            }

            var watcher = new FileSystemWatcher(path);
            FileSystemEventHandler onChange = OnChangeUpdateLastModified(watcher);
            RenamedEventHandler onRenamed = OnRenamedUpdateLastModified(watcher);
            watcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName
                                   | NotifyFilters.LastAccess | NotifyFilters.LastWrite;
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += onRenamed;

            DateTime lastModifiedTime = DateTime.Now;
            _directoryToLastModifiedTime[path] = lastModifiedTime;

            _directoryToWatcher[path] = watcher;
            watcher.EnableRaisingEvents = true;
        }

        private void WatchFile(string path)
        {
            string directory = Path.GetDirectoryName(path);
            WatchDirectory(directory);
            _fileToLastModifiedTime[path] = _directoryToLastModifiedTime[directory];
        }

        private FileSystemEventHandler OnChangeUpdateLastModified(FileSystemWatcher watcher)
        {
            return (sender, args) =>
            {
                string directory = watcher.Path;
                string file = args.FullPath;
                DateTime lastModifiedTime = DateTime.Now;

                _directoryToLastModifiedTime[directory] = lastModifiedTime;
                _fileToLastModifiedTime[file] = lastModifiedTime;
            };
        }

        private RenamedEventHandler OnRenamedUpdateLastModified(FileSystemWatcher watcher)
        {
            return (sender, args) =>
            {
                string directory = watcher.Path;
                string file = args.FullPath;
                DateTime lastModifiedTime = DateTime.Now;

                _directoryToLastModifiedTime[directory] = lastModifiedTime;
                _fileToLastModifiedTime[file] = lastModifiedTime;
            };
        }
    }
}
