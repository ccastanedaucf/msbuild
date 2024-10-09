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
    internal class ResolveAssemblyReferenceServiceWorker
    {
        private readonly string _workerId;

        private readonly NamedPipeServerStream _pipe;

        private readonly ConcurrentDictionary<string, byte> _seenStateFiles;

        private readonly EvaluationCacheV2 _evaluationCache;

        private readonly Queue<ResolveAssemblyReferenceBuildEventArgs> _buildEventQueue;

        private const int MaxBuildEvents = 100;

        internal ResolveAssemblyReferenceServiceWorker(
            string workerId,
            string pipeName,
            EvaluationCacheV2 evaluationCache,
            ConcurrentDictionary<string, byte> seenStateFiles)
        {
            _workerId = workerId;
            _pipe = new(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: Environment.ProcessorCount,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                inBufferSize: 16384,
                outBufferSize: 16384);
            _evaluationCache = evaluationCache;
            _seenStateFiles = seenStateFiles;
            _buildEventQueue = new(MaxBuildEvents);
        }

        internal async Task RunServerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _pipe.WaitForConnectionAsync(cancellationToken);

                Stopwatch e2eTime = new();
                e2eTime.Start();
                Console.WriteLine($"({_workerId}) Connected to client.");

                try
                {
                    ResolveAssemblyReferencesRequest request = ResolveAssemblyReferencesRequest.Parser.ParseDelimitedFrom(_pipe);
                    ResolveAssemblyReferencesReply reply = await ResolveAssemblyReferencesAsync(request, cancellationToken);

                    MessageExtensions.WriteDelimitedTo(reply, _pipe);

                    // Avoid replaying build events on future runs.
                    reply.BuildEventArgsQueue.Clear();

                    _pipe.WaitForPipeDrain();

                    e2eTime.Stop();
                    Console.WriteLine($"({_workerId}) Request completed for '{request.StateFile}' in {e2eTime.ElapsedMilliseconds} ms.");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                }

                _pipe.Disconnect();
            }
        }

        private async Task<ResolveAssemblyReferencesReply> ResolveAssemblyReferencesAsync(ResolveAssemblyReferencesRequest request, CancellationToken cancellationToken)
        {
            bool isCacheable = request.StateFile != null;

            // TODO: Determine proper project identifier which does not rely on state file.
            if (isCacheable)
            {
                ResolveAssemblyReferencesReply? cachedResult = await _evaluationCache.GetCachedEvaluation(request);

                if (cachedResult != null)
                {
                    Console.WriteLine($"({_workerId}) Cache hit for '{request.StateFile}'. Skipping RAR.')");
                    return cachedResult;
                }
            }

            Console.WriteLine($"({_workerId}) Executing RAR for '{request.StateFile}'.");
            Stopwatch execTime = new();
            execTime.Start();

            EventQueueBuildEngine buildEngine = new(
                (MessageImportance)request.MinimumMessageImportance,
                request.IsTaskLoggingEnabled);
            Task buildEventTask = Task.Run(
                () => ProcessBuildEvents(buildEngine, cancellationToken),
                cancellationToken);
            ResolveAssemblyReferencesReply result = HandleRequest(request, buildEngine);

            execTime.Stop();
            Console.WriteLine($"({_workerId}) RAR completed for '{request.StateFile}' in {execTime.ElapsedMilliseconds} ms.'");
            buildEngine.Complete();

            await buildEventTask;

            result.BuildEventArgsQueue.Add(_buildEventQueue);
            _buildEventQueue.Clear();

            if (isCacheable)
            {
                _evaluationCache.CacheEvaluation(request, result);
            }

            return result;
        }

        private async Task ProcessBuildEvents(EventQueueBuildEngine buildEngine, CancellationToken cancellationToken)
        {
            try
            {
                while (cancellationToken.IsCancellationRequested)
                {
                    ResolveAssemblyReferenceBuildEventArgs buildEventArgs = await buildEngine.EventQueue.ReadAsync(cancellationToken);
                    _buildEventQueue.Enqueue(buildEventArgs);

                    if (_buildEventQueue.Count == MaxBuildEvents)
                    {
                        Console.WriteLine($"({_workerId}) Flushing build events.");
                        ResolveAssemblyReferencesReply response = new();
                        response.BuildEventArgsQueue.Add(_buildEventQueue);
                        MessageExtensions.WriteDelimitedTo(response, _pipe);
                        _buildEventQueue.Clear();
                    }
                }
            }
            catch (ChannelClosedException)
            {
            }
        }

        private async Task ProcessBuildEvents(
            Queue<ResolveAssemblyReferenceBuildEventArgs> buildEventQueue,
            ChannelReader<ResolveAssemblyReferenceBuildEventArgs> buildEngineQueue,
            CancellationToken cancellationToken)
        {
            try
            {
                while (cancellationToken.IsCancellationRequested)
                {
                    // TODO: Merge queues / simplify batching?
                    ResolveAssemblyReferenceBuildEventArgs buildEventArgs = await buildEngineQueue.ReadAsync(cancellationToken);
                    buildEventQueue.Enqueue(buildEventArgs);

                    if (buildEventQueue.Count == MaxBuildEvents)
                    {
                        Console.WriteLine($"({_workerId}) Flushing build events.");
                        ResolveAssemblyReferencesReply response = new();
                        response.BuildEventArgsQueue.Add(buildEventQueue);
                        MessageExtensions.WriteDelimitedTo(response, _pipe);
                        buildEventQueue.Clear();
                    }
                }
            }
            catch (ChannelClosedException)
            {
            }
        }

        private ResolveAssemblyReferencesReply HandleRequest(ResolveAssemblyReferencesRequest req, EventQueueBuildEngine buildEngine)
        {
            // Only load the state file on the first run.
            bool shouldLoadStateFile = !string.IsNullOrEmpty(req.StateFile) && _seenStateFiles.TryAdd(req.StateFile, 0);

            ResolveAssemblyReference rarTask = new()
            {
                AllowedAssemblyExtensions = [.. req.AllowedAssemblyExtensions],
                AllowedRelatedFileExtensions = [.. req.AllowedRelatedFileExtensions],
                AppConfigFile = req.AppConfigFile,
                Assemblies = [.. req.Assemblies],
                AssemblyFiles = [.. req.AssemblyFiles],
                AutoUnify = req.AutoUnify,
                BuildEngine = buildEngine,
                CandidateAssemblyFiles = [.. req.CandidateAssemblyFiles],
                CopyLocalDependenciesWhenParentReferenceInGac = req.CopyLocalDependenciesWhenParentReferenceInGac,
                DoNotCopyLocalIfInGac = req.DoNotCopyLocalIfInGac,
                FindDependencies = req.FindDependencies,
                FindDependenciesOfExternallyResolvedReferences = req.FindDependenciesOfExternallyResolvedReferences,
                FindRelatedFiles = req.FindRelatedFiles,
                FindSatellites = req.FindSatellites,
                FindSerializationAssemblies = req.FindSerializationAssemblies,
                FullFrameworkAssemblyTables = [.. req.FullFrameworkAssemblyTables],
                FullFrameworkFolders = [.. req.FullFrameworkFolders],
                FullTargetFrameworkSubsetNames = [.. req.FullTargetFrameworkSubsetNames],
                IgnoreDefaultInstalledAssemblySubsetTables = req.IgnoreDefaultInstalledAssemblySubsetTables,
                IgnoreDefaultInstalledAssemblyTables = req.IgnoreDefaultInstalledAssemblyTables,
                IgnoreTargetFrameworkAttributeVersionMismatch = req.IgnoreTargetFrameworkAttributeVersionMismatch,
                IgnoreVersionForFrameworkReferences = req.IgnoreVersionForFrameworkReferences,
                InstalledAssemblySubsetTables = [.. req.InstalledAssemblySubsetTables],
                InstalledAssemblyTables = [.. req.InstalledAssemblyTables],
                LatestTargetFrameworkDirectories = [.. req.LatestTargetFrameworkDirectories],
                ProfileName = req.ProfileName,
                ResolvedSDKReferences = [.. req.ResolvedSdkReferences],
                SearchPaths = [.. req.SearchPaths],
                ShouldExecuteInProcess = true,
                Silent = req.Silent,
                StateFile = shouldLoadStateFile ? req.StateFile : null,
                SupportsBindingRedirectGeneration = req.SupportsBindingRedirectGeneration,
                TargetFrameworkDirectories = [.. req.TargetFrameworkDirectories],
                TargetFrameworkMoniker = req.TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = req.TargetFrameworkMonikerDisplayName,
                TargetFrameworkSubsets = [.. req.TargetFrameworkSubsets],
                TargetFrameworkVersion = req.TargetFrameworkVersion,
                TargetProcessorArchitecture = req.TargetProcessorArchitecture,
                TargetedRuntimeVersion = req.TargetedRuntimeVersion,
                UnresolveFrameworkAssembliesFromHigherFrameworks = req.UnresolveFrameworkAssembliesFromHigherFrameworks,
                WarnOrErrorOnTargetArchitectureMismatch = req.WarnOrErrorOnTargetArchitectureMismatch,
            };

            bool success = rarTask.ExecuteInProcess();

            ResolveAssemblyReferencesReply resp = CreateResponse(rarTask, buildEngine, success);

            return resp;
        }

        private static ResolveAssemblyReferencesReply CreateResponse(
            ResolveAssemblyReference rarTask,
            EventQueueBuildEngine buildEngine,
            bool success)
        {
            HashSet<ITaskItem> copyLocalFiles = new(rarTask.CopyLocalFiles);

            ResolveAssemblyReferencesReply resp = new()
            {
                IsCompleted = true,
                Success = success,
                NumCopyLocalFiles = rarTask.CopyLocalFiles.Length,
                DependsOnNetStandard = rarTask.DependsOnNETStandard,
                DependsOnSystemRuntime = rarTask.DependsOnSystemRuntime,
                Cache = rarTask.Cache,
            };

            resp.FilesWritten.Add(CreateReadOnlyTaskItems(rarTask.FilesWritten));
            resp.RelatedFiles.Add(CreateReadOnlyTaskItems(rarTask.RelatedFiles));
            resp.ResolvedDependencyFiles.Add(CreateReadOnlyTaskItems(rarTask.ResolvedDependencyFiles));
            resp.ResolvedFiles.Add(CreateReadOnlyTaskItems(rarTask.ResolvedFiles));
            resp.SatelliteFiles.Add(CreateReadOnlyTaskItems(rarTask.SatelliteFiles));
            resp.ScatterFiles.Add(CreateReadOnlyTaskItems(rarTask.ScatterFiles));
            resp.SerializationAssemblyFiles.Add(CreateReadOnlyTaskItems(rarTask.SerializationAssemblyFiles));
            resp.SuggestedRedirects.Add(CreateReadOnlyTaskItems(rarTask.SuggestedRedirects));
            resp.UnresolvedAssemblyConflicts.Add(CreateReadOnlyTaskItems(rarTask.UnresolvedAssemblyConflicts));

            foreach (string path in rarTask.TrackedPaths)
            {
                if (IsDirectory(path))
                {
                    resp.TrackedDirectories.Add(path);
                }
                else
                {
                    resp.TrackedFiles.Add(path);
                }
            }

            return resp;

            ReadOnlyTaskItem[] CreateReadOnlyTaskItems(ICollection<ITaskItem> taskItems)
            {
                List<ReadOnlyTaskItem> readOnlyTaskItems = new(taskItems.Count);

                foreach (ITaskItem taskItem in taskItems)
                {
                    readOnlyTaskItems.Add(new ReadOnlyTaskItem(
                        taskItem,
                        isCopyLocalFile: copyLocalFiles.Contains(taskItem)));
                }

                return [.. readOnlyTaskItems];
            }

            static bool IsDirectory(string path)
            {
                if (path.Length == 0)
                {
                    return false;
                }

                char ch = path[path.Length - 1];

                return ch == Path.DirectorySeparatorChar
                    || ch == Path.AltDirectorySeparatorChar
                    || ch == Path.VolumeSeparatorChar;
            }
        }
    }
}
