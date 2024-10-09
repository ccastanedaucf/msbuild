using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Google.Protobuf;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    public class ResolveAssemblyReferenceService
    {
        internal const string PipeName = "ResolveAssemblyReferences.Pipe";

        private readonly ResolveAssemblyReferenceServiceWorker[] _workers;

        public ResolveAssemblyReferenceService()
            : this(Environment.ProcessorCount)
        {
        }

        public ResolveAssemblyReferenceService(int degreeOfParallelism)
        {
            ConcurrentDictionary<string, byte> seenStateFiles = new(StringComparer.OrdinalIgnoreCase);
            EvaluationCacheV2 evaluationCache = new(degreeOfParallelism);
            _workers = new ResolveAssemblyReferenceServiceWorker[degreeOfParallelism];

            for (int i = 0; i < _workers.Length; i++)
            {
                string workerId = i.ToString();
                ResolveAssemblyReferenceServiceWorker worker = new(
                    workerId,
                    PipeName,
                    evaluationCache,
                    seenStateFiles);
                _workers[i] = worker;
            }
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default(CancellationToken))
        {

            List<Task> serverTasks = new(_workers.Length);

            foreach (ResolveAssemblyReferenceServiceWorker worker in _workers)
            {
                Task serverTask = Task.Run(
                    () => worker.RunServerAsync(cancellationToken),
                    cancellationToken);
                serverTasks.Add(serverTask);
            }

            await Task.WhenAll([.. serverTasks]);
        }
    }
}
