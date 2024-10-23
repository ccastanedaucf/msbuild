// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal record struct FileSystemChange(string Directory, string File);


    internal class CacheWatcher
    {

        private Dictionary<string, FileSystemWatcher> _directoryToWatcher { get; } =
            new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);

        private ConcurrentQueue<FileSystemChange> _fileSystemChangeQueue { get; } = new ConcurrentQueue<FileSystemChange>();

        private List<FileSystemChange> _noChanges { get; } = new List<FileSystemChange>();

        internal List<FileSystemChange> RecentChanges
        {
            get
            {
                if (_fileSystemChangeQueue.IsEmpty)
                {
                    return _noChanges;
                }

                var recentChanges = new List<FileSystemChange>(_fileSystemChangeQueue.Count);

                while (!_fileSystemChangeQueue.IsEmpty)
                {
                    if (_fileSystemChangeQueue.TryDequeue(out FileSystemChange fileSystemChange))
                    {
                        recentChanges.Add(fileSystemChange);
                    }
                }

                return recentChanges;
            }
        }

        public void Watch(string directory)
        {
            bool isWatching = _directoryToWatcher.ContainsKey(directory);

            if (isWatching)
            {
                return;
            }

            var watcher = new FileSystemWatcher(directory);
            FileSystemEventHandler onChange = OnChangeEnqueue(watcher);
            RenamedEventHandler onRenamed = OnRenamedEnqueue(watcher);
            watcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName
                                                               | NotifyFilters.LastAccess | NotifyFilters.LastWrite;
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += onRenamed;

            _directoryToWatcher[directory] = watcher;
            watcher.EnableRaisingEvents = true;
        }

        private FileSystemEventHandler OnChangeEnqueue(FileSystemWatcher watcher)
        {
            return (sender, args) =>
            {
                string directory = watcher.Path;
                string file = args.FullPath;

                _fileSystemChangeQueue.Enqueue(new FileSystemChange(directory, file));
            };
        }

        private RenamedEventHandler OnRenamedEnqueue(FileSystemWatcher watcher)
        {
            return (sender, args) =>
            {
                string directory = watcher.Path;
                string file = args.FullPath;

                _fileSystemChangeQueue.Enqueue(new FileSystemChange(directory, file));
            };
        }
    }
}