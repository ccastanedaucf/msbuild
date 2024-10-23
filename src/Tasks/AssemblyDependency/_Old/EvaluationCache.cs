// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Google.Protobuf;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    public class EvaluationCache
    {
        private Dictionary<ByteString, ResolveAssemblyReferencesReply> _evaluationCache { get; } = [];

        private Dictionary<string, HashSet<ByteString>> _directoryToWatchingProjects { get; } = new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, HashSet<ByteString>> _fileToWatchingProjects { get; } = new(StringComparer.OrdinalIgnoreCase);

        private CacheWatcher _watcher = new();

        private object _cacheLock { get; } = new object();

        public bool TryGetEvaluation(ResolveAssemblyReferencesRequest req, [MaybeNullWhen(false)] out ResolveAssemblyReferencesReply cachedEvaluation)
        {
            cachedEvaluation = null;

            // TODO: Better concurrency model, needs more investigation on where concurrent data structures can be
            // utilized and where it is safe to remove locks as all three dictionaries are currently updated as
            // an atomic operation.
            lock (_cacheLock)
            {
                InvalidateOutOfDateEvaluations();

                if (_evaluationCache.TryGetValue(req.ToByteString(), out cachedEvaluation))
                {
                    return true;
                }
            }

            return false;
        }

        public void CacheEvaluation(ResolveAssemblyReferencesRequest req, ResolveAssemblyReferencesReply resp)
        {
            lock (_cacheLock)
            {
                ByteString requestHash = req.ToByteString();
                WatchTrackedPaths(requestHash, resp);
                _evaluationCache[requestHash] = resp;
            }
        }

        private void InvalidateOutOfDateEvaluations()
        {
            foreach (FileSystemChange fileSystemChange in _watcher.RecentChanges)
            {
                InvalidateEvaluationsForProjects(_directoryToWatchingProjects, fileSystemChange.Directory);
                InvalidateEvaluationsForProjects(_fileToWatchingProjects, fileSystemChange.File);
            }
        }

        private void InvalidateEvaluationsForProjects(Dictionary<string, HashSet<ByteString>> pathToWatchingProjects, string path)
        {
            if (pathToWatchingProjects.TryGetValue(path, out HashSet<ByteString>? projects))
            {
                foreach (ByteString projectId in projects)
                {
                    _evaluationCache.Remove(projectId);
                }

                projects.Clear();
            }
        }

        private void WatchTrackedPaths(ByteString requestHash, ResolveAssemblyReferencesReply resp)
        {
            foreach (string directory in resp.TrackedDirectories)
            {
                WatchDirectory(requestHash, directory);
            }
            foreach (string file in resp.TrackedFiles)
            {
                WatchFile(requestHash, file);
            }
        }

        private void WatchDirectory(ByteString requestHash, string directory)
        {
            _watcher.Watch(directory);

            if (!_directoryToWatchingProjects.TryGetValue(directory, out HashSet<ByteString>? watchingProjects))
            {
                watchingProjects = [];
                _directoryToWatchingProjects.Add(directory, watchingProjects);
            }

            watchingProjects.Add(requestHash);
        }

        private void WatchFile(ByteString requestHash, string file)
        {
            string directory = Path.GetDirectoryName(file);
            _watcher.Watch(directory);

            if (!_fileToWatchingProjects.TryGetValue(file, out HashSet<ByteString>? watchingProjects))
            {
                watchingProjects = [];
                _fileToWatchingProjects.Add(file, watchingProjects);
            }

            watchingProjects.Add(requestHash);
        }
    }
}