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
using Google.Protobuf;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class EvaluationCacheV2
    {
        private ConcurrentDictionary<string, ResolveAssemblyReferenceResponse> _evaluationCache { get; } = [];

        private readonly SemaphoreSlim _ioSemaphore;

        internal EvaluationCacheV2(int ioParallelism)
        {
            _ioSemaphore = new SemaphoreSlim(ioParallelism);
        }

        public async Task<ResolveAssemblyReferenceResponse?> GetCachedEvaluation(ResolveAssemblyReferenceRequest request)
        {
            string requestHash = request.ByteString!;

            if (!_evaluationCache.TryGetValue(requestHash, out ResolveAssemblyReferenceResponse cachedEvaluation))
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

        public void CacheEvaluation(ResolveAssemblyReferenceRequest request, ResolveAssemblyReferenceResponse response)
        {
            string requestHash = request.ByteString!;
            _evaluationCache[requestHash] = response;
        }
    }
}