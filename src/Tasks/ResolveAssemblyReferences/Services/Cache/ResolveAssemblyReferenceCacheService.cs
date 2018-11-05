using System;
using System.Collections.Generic;

using Microsoft.Build.Tasks.ResolveAssemblyReferences.Abstractions;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Services.Cache
{
    internal class ResolveAssemblyReferenceCacheService : IResolveAssemblyReferenceService
    {
        private IResolveAssemblyReferenceService RarService { get; }

        private IEvaluationWatcher Watcher { get; }

        private Dictionary<string, ResolveAssemblyReferenceEvaluation> EvaluationCache { get; } =
            new Dictionary<string, ResolveAssemblyReferenceEvaluation>(StringComparer.OrdinalIgnoreCase);

        internal ResolveAssemblyReferenceCacheService(
            IResolveAssemblyReferenceService rarService,
            IEvaluationWatcher watcher
        )
        {
            RarService = rarService;
            Watcher = watcher;
        }

        public ResolveAssemblyReferenceResponse ResolveAssemblyReferences
        (
            ResolveAssemblyReferenceRequest req
        )
        {
            string projectId = req.StateFile;

            bool first =
                EvaluationCache.TryGetValue(projectId, out ResolveAssemblyReferenceEvaluation cachedEvaluation);
            bool sec = first && ResolveAssemblyReferenceRequestComparer.Equals(req, cachedEvaluation.Input);
            bool third = first && Watcher.IsClean(cachedEvaluation);

            if
            (
                first && sec && third
            )
            {
                return cachedEvaluation.Output;
            }

            EvaluationCache.Remove(projectId);

            ResolveAssemblyReferenceResponse resp = RarService.ResolveAssemblyReferences(req);

            var evaluation = new ResolveAssemblyReferenceEvaluation
            {
                Input = req,
                Output = resp,
                TouchedDirectories = resp.TrackedDirectories,
                TouchedFiles = resp.TrackedFiles
            };

            EvaluationCache[projectId] = evaluation;
            Watcher.Watch(evaluation);

            return resp;
        }
    }
}
