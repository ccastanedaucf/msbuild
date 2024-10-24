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
        private ConcurrentDictionary<ulong, RarExecutionResponse> _evaluationCache { get; } = [];

        private readonly SemaphoreSlim _ioSemaphore;

        internal RarExecutionCache(int ioParallelism)
        {
            _ioSemaphore = new SemaphoreSlim(ioParallelism);
        }

        public async Task<RarExecutionResponse?> GetCachedEvaluation(RarExecutionRequest request)
        {
            if (request.ByteHash == 0 || !_evaluationCache.TryGetValue(request.ByteHash, out RarExecutionResponse? cachedEvaluation))
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

            await Task.WhenAll([.. workerTasks]);

            return cachedEvaluation;
        }

        public void CacheEvaluation(RarExecutionRequest request, RarExecutionResponse response)
        {
            if (request.ByteHash == 0)
            {
                return;
            }

            _evaluationCache[request.ByteHash] = response;
        }
    }
}