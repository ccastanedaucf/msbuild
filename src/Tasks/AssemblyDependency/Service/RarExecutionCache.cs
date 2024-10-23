// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class RarExecutionCache
    {
        private ConcurrentDictionary<string, RarExecutionResponse> _evaluationCache { get; } = [];

        private readonly SemaphoreSlim _ioSemaphore;

        internal RarExecutionCache(int ioParallelism)
        {
            _ioSemaphore = new SemaphoreSlim(ioParallelism);
        }

        public async Task<RarExecutionResponse?> GetCachedEvaluation(RarExecutionRequest request)
        {
            string requestHash = request.ByteString!;

            if (!_evaluationCache.TryGetValue(requestHash, out RarExecutionResponse? cachedEvaluation))
            {
                return null;
            }

            SystemState cache = cachedEvaluation.Cache!;

            List<Task> workerTasks = new(cache.instanceLocalFileStateCache.Count);

            foreach (string filePath in cache.instanceLocalFileStateCache.Keys)
            {
                workerTasks.Add(Task.Run(async () =>
                {
                    await _ioSemaphore.WaitAsync();

                    try
                    {
                        _ = File.GetLastWriteTimeUtc(filePath);
                    }
                    finally
                    {
                        _ioSemaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(workerTasks.ToArray());

            return cachedEvaluation;
        }

        public void CacheEvaluation(RarExecutionRequest request, RarExecutionResponse response)
        {
            string requestHash = request.ByteString!;
            _evaluationCache[requestHash] = response;
        }
    }
}