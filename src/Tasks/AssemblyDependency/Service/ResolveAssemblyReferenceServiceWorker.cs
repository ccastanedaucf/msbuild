using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Google.Protobuf;
using Microsoft.Build.BackEnd;
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

                    ResolveAssemblyReferenceRequest request = ReadRequest();
                    ResolveAssemblyReferenceResponse response = await ResolveAssemblyReferencesAsync(request, cancellationToken);

                    Console.WriteLine($"({_workerId}) Writing response...");
                    SendResponse(response);

                    // Avoid replaying build events on future runs.
                    response.BuildEventArgsQueue = [];

                    Console.WriteLine($"({_workerId}) Waiting for pipe drain...");
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

        private ResolveAssemblyReferenceRequest ReadRequest()
        {
            // Read the message length.
            using BinaryReader reader = new(_pipe, Encoding.Default, leaveOpen: true);
            int messageLength = reader.ReadInt32();

            // Read raw bytes to a temporary buffer to reduce IO calls.
            using MemoryStream memoryStream = new(messageLength);
            byte[] buffer = memoryStream.GetBuffer();
            _pipe.Read(buffer, 0, buffer.Length);

            // Deserialize the request.
            memoryStream.Position = 0;
            ITranslator translator = BinaryTranslator.GetReadTranslator(memoryStream, InterningBinaryReader.PoolingBuffer);
            ResolveAssemblyReferenceRequest request = new();
            translator.Translate(ref request);

            return request;
        }

        private void SendResponse(ResolveAssemblyReferenceResponse response)
        {
            // Serialize to temporary buffer to reduce IO calls.
            using MemoryStream memoryStream = new();
            ITranslator translator = BinaryTranslator.GetWriteTranslator(memoryStream);
            translator.Translate(ref response);

            // Delimit message with length.
            _pipe.Write(Encoding.UTF8.GetBytes(memoryStream.Length.ToString()));

            // Send the serialized response.
            memoryStream.CopyTo(_pipe);
        }

        private async Task<ResolveAssemblyReferenceResponse> ResolveAssemblyReferencesAsync(ResolveAssemblyReferenceRequest request, CancellationToken cancellationToken)
        {
            bool isCacheable = request.StateFile != null;

            // TODO: Determine proper project identifier which does not rely on state file.
            if (isCacheable)
            {
                /*
                ResolveAssemblyReferenceResponse? cachedResult = await _evaluationCache.GetCachedEvaluation(request);

                if (cachedResult != null)
                {
                    Console.WriteLine($"({_workerId}) Cache hit for '{request.StateFile}'. Skipping RAR.')");
                    return cachedResult;
                }
                */
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
            ResolveAssemblyReferenceResponse result = HandleRequest(request, buildEngine);

            execTime.Stop();
            Console.WriteLine($"({_workerId}) RAR completed for '{request.StateFile}' in {execTime.ElapsedMilliseconds} ms.'");
            buildEngine.Complete();

            await buildEventTask;

            result.BuildEventArgsQueue = [.. _buildEventQueue];
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
                        ResolveAssemblyReferenceResponse response = new()
                        {
                            BuildEventArgsQueue = [.. _buildEventQueue],
                        };
                        SendResponse(response);
                        _buildEventQueue.Clear();
                    }
                }
            }
            catch (ChannelClosedException)
            {
            }
        }

        private ResolveAssemblyReferenceResponse HandleRequest(ResolveAssemblyReferenceRequest request, EventQueueBuildEngine buildEngine)
        {
            // Only load the state file on the first run.
            bool shouldLoadStateFile = !string.IsNullOrEmpty(request.StateFile) && _seenStateFiles.TryAdd(request.StateFile!, 0);

            ResolveAssemblyReference rarTask = new()
            {
                AllowedAssemblyExtensions = [.. request.AllowedAssemblyExtensions],
                AllowedRelatedFileExtensions = [.. request.AllowedRelatedFileExtensions],
                AppConfigFile = request.AppConfigFile,
                Assemblies = [.. request.Assemblies],
                AssemblyFiles = [.. request.AssemblyFiles],
                AutoUnify = request.AutoUnify,
                BuildEngine = buildEngine,
                CandidateAssemblyFiles = [.. request.CandidateAssemblyFiles],
                CopyLocalDependenciesWhenParentReferenceInGac = request.CopyLocalDependenciesWhenParentReferenceInGac,
                DoNotCopyLocalIfInGac = request.DoNotCopyLocalIfInGac,
                FindDependencies = request.FindDependencies,
                FindDependenciesOfExternallyResolvedReferences = request.FindDependenciesOfExternallyResolvedReferences,
                FindRelatedFiles = request.FindRelatedFiles,
                FindSatellites = request.FindSatellites,
                FindSerializationAssemblies = request.FindSerializationAssemblies,
                FullFrameworkAssemblyTables = [.. request.FullFrameworkAssemblyTables],
                FullFrameworkFolders = [.. request.FullFrameworkFolders],
                FullTargetFrameworkSubsetNames = [.. request.FullTargetFrameworkSubsetNames],
                IgnoreDefaultInstalledAssemblySubsetTables = request.IgnoreDefaultInstalledAssemblySubsetTables,
                IgnoreDefaultInstalledAssemblyTables = request.IgnoreDefaultInstalledAssemblyTables,
                IgnoreTargetFrameworkAttributeVersionMismatch = request.IgnoreTargetFrameworkAttributeVersionMismatch,
                IgnoreVersionForFrameworkReferences = request.IgnoreVersionForFrameworkReferences,
                InstalledAssemblyTables = [.. request.InstalledAssemblyTables],
                InstalledAssemblySubsetTables = [.. request.InstalledAssemblySubsetTables],
                LatestTargetFrameworkDirectories = [.. request.LatestTargetFrameworkDirectories],
                ProfileName = request.ProfileName,
                ResolvedSDKReferences = [.. request.ResolvedSDKReferences],
                SearchPaths = [.. request.SearchPaths],
                ShouldExecuteInProcess = true,
                Silent = request.Silent,
                StateFile = shouldLoadStateFile ? request.StateFile : null,
                SupportsBindingRedirectGeneration = request.SupportsBindingRedirectGeneration,
                TargetFrameworkDirectories = [.. request.TargetFrameworkDirectories],
                TargetFrameworkMoniker = request.TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = request.TargetFrameworkMonikerDisplayName,
                TargetFrameworkSubsets = [.. request.TargetFrameworkSubsets],
                TargetFrameworkVersion = request.TargetFrameworkVersion,
                TargetProcessorArchitecture = request.TargetProcessorArchitecture,
                TargetedRuntimeVersion = request.TargetedRuntimeVersion,
                UnresolveFrameworkAssembliesFromHigherFrameworks = request.UnresolveFrameworkAssembliesFromHigherFrameworks,
                WarnOrErrorOnTargetArchitectureMismatch = request.WarnOrErrorOnTargetArchitectureMismatch,
            };

            bool success = rarTask.ExecuteInProcess();

            ResolveAssemblyReferenceResponse resp = CreateResponse(rarTask, buildEngine, success);

            return resp;
        }

        private static ResolveAssemblyReferenceResponse CreateResponse(
            ResolveAssemblyReference rarTask,
            EventQueueBuildEngine buildEngine,
            bool success)
        {
            HashSet<ITaskItem> copyLocalFiles = new(rarTask.CopyLocalFiles);

            ResolveAssemblyReferenceResponse resp = new()
            {
                IsComplete = true,
                Success = success,
                NumCopyLocalFiles = rarTask.CopyLocalFiles.Length,
                DependsOnNetStandard = rarTask.DependsOnNETStandard,
                DependsOnSystemRuntime = rarTask.DependsOnSystemRuntime,
                FilesWritten = CreateReadOnlyTaskItems(rarTask.FilesWritten),
                RelatedFiles = CreateReadOnlyTaskItems(rarTask.RelatedFiles),
                ResolvedDependencyFiles = CreateReadOnlyTaskItems(rarTask.ResolvedDependencyFiles),
                ResolvedFiles = CreateReadOnlyTaskItems(rarTask.ResolvedFiles),
                SatelliteFiles = CreateReadOnlyTaskItems(rarTask.SatelliteFiles),
                ScatterFiles = CreateReadOnlyTaskItems(rarTask.ScatterFiles),
                SerializationAssemblyFiles = CreateReadOnlyTaskItems(rarTask.SerializationAssemblyFiles),
                SuggestedRedirects = CreateReadOnlyTaskItems(rarTask.SuggestedRedirects),
                UnresolvedAssemblyConflicts = CreateReadOnlyTaskItems(rarTask.UnresolvedAssemblyConflicts),
                Cache = rarTask.Cache,
            };

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

            TaskItemSlim[] CreateReadOnlyTaskItems(ICollection<ITaskItem> taskItems)
            {
                List<TaskItemSlim> readOnlyTaskItems = new(taskItems.Count);

                foreach (ITaskItem taskItem in taskItems)
                {
                    readOnlyTaskItems.Add(new TaskItemSlim(
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
