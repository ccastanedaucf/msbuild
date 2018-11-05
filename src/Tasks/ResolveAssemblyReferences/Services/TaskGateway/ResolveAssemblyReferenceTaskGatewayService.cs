using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Abstractions;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Services.TaskGateway
{
    internal class ResolveAssemblyReferenceTaskGatewayService : IResolveAssemblyReferenceService
    {
        private IResolveAssemblyReferenceTask RarTask { get; }

        internal ResolveAssemblyReferenceTaskGatewayService(IResolveAssemblyReferenceTask rarTask)
        {
            RarTask = rarTask;
        }

        public ResolveAssemblyReferenceResponse ResolveAssemblyReferences(ResolveAssemblyReferenceRequest req)
        {
            var ioTracker = new ResolveAssemblyReferenceIOTracker();
            var buildEngine = new EventQueueBuildEngine();

            ResolveAssemblyReferenceTaskInput input = ConvertRequestToTaskInput(req, ioTracker, buildEngine);
            ResolveAssemblyReferenceTaskOutput output = RarTask.Execute(input);

            return ConvertTaskOutputToResponse(output, ioTracker, buildEngine);
        }

        private static ResolveAssemblyReferenceTaskInput ConvertRequestToTaskInput(
            ResolveAssemblyReferenceRequest req,
            ResolveAssemblyReferenceIOTracker ioTracker,
            EventQueueBuildEngine buildEngine
        )
        {
            return new ResolveAssemblyReferenceTaskInput
            {
                AllowedAssemblyExtensions = req.AllowedAssemblyExtensions,
                AllowedRelatedFileExtensions = req.AllowedRelatedFileExtensions,
                AppConfigFile = req.AppConfigFile,
                Assemblies = req.Assemblies,
                AssemblyFiles = req.AssemblyFiles,
                AutoUnify = req.AutoUnify,
                BuildEngine = buildEngine,
                CandidateAssemblyFiles = req.CandidateAssemblyFiles,
                CopyLocalDependenciesWhenParentReferenceInGac = req.CopyLocalDependenciesWhenParentReferenceInGac,
                DoNotCopyLocalIfInGac = req.DoNotCopyLocalIfInGac,
                FindDependencies = req.FindDependencies,
                FindDependenciesOfExternallyResolvedReferences = req.FindDependenciesOfExternallyResolvedReferences,
                FindRelatedFiles = req.FindRelatedFiles,
                FindSatellites = req.FindSatellites,
                FindSerializationAssemblies = req.FindSerializationAssemblies,
                FullFrameworkAssemblyTables = req.FullFrameworkAssemblyTables,
                FullFrameworkFolders = req.FullFrameworkFolders,
                FullTargetFrameworkSubsetNames = req.FullTargetFrameworkSubsetNames,
                IgnoreDefaultInstalledAssemblySubsetTables = req.IgnoreDefaultInstalledAssemblySubsetTables,
                IgnoreDefaultInstalledAssemblyTables = req.IgnoreDefaultInstalledAssemblyTables,
                IgnoreTargetFrameworkAttributeVersionMismatch = req.IgnoreTargetFrameworkAttributeVersionMismatch,
                IgnoreVersionForFrameworkReferences = req.IgnoreVersionForFrameworkReferences,
                InstalledAssemblySubsetTables = req.InstalledAssemblySubsetTables,
                InstalledAssemblyTables = req.InstalledAssemblyTables,
                IoTracker = ioTracker,
                LatestTargetFrameworkDirectories = req.LatestTargetFrameworkDirectories,
                ProfileName = req.ProfileName,
                ResolvedSDKReferences = req.ResolvedSDKReferences,
                SearchPaths = req.SearchPaths,
                ShouldUseOutOFProcRar = false,
                Silent = req.Silent,
                StateFile = req.StateFile,
                SupportsBindingRedirectGeneration = req.SupportsBindingRedirectGeneration,
                TargetFrameworkDirectories = req.TargetFrameworkDirectories,
                TargetFrameworkMoniker = req.TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = req.TargetFrameworkMonikerDisplayName,
                TargetFrameworkSubsets = req.TargetFrameworkSubsets,
                TargetFrameworkVersion = req.TargetFrameworkVersion,
                TargetProcessorArchitecture = req.TargetProcessorArchitecture,
                TargetedRuntimeVersion = req.TargetedRuntimeVersion,
                UnresolveFrameworkAssembliesFromHigherFrameworks = req.UnresolveFrameworkAssembliesFromHigherFrameworks,
                WarnOrErrorOnTargetArchitectureMismatch = req.WarnOrErrorOnTargetArchitectureMismatch,
            };
        }

        private static ResolveAssemblyReferenceResponse ConvertTaskOutputToResponse(
            ResolveAssemblyReferenceTaskOutput taskOutput,
            ResolveAssemblyReferenceIOTracker ioTracker,
            EventQueueBuildEngine buildEngine
        )
        {
            int estimatedTaskItemCount = EstimateTaskItemCount(taskOutput);
            var taskItemPayloadList = new List<ReadOnlyTaskItem>(estimatedTaskItemCount);
            var taskItemToPayload = new Dictionary<ITaskItem, ReadOnlyTaskItem>(estimatedTaskItemCount);

            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.CopyLocalFiles, 0);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.FilesWritten, 1);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.RelatedFiles, 2);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.ResolvedDependencyFiles, 3);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.ResolvedFiles, 4);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.SatelliteFiles, 5);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.ScatterFiles, 6);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.SerializationAssemblyFiles, 7);
            ExtractTaskItemPayloadList(taskItemPayloadList, taskItemToPayload, taskOutput.SuggestedRedirects, 8);

            var trackedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trackedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in ioTracker.TrackedPaths)
            {
                if (Path.GetFileName(path) == string.Empty)
                {
                    trackedDirectories.Add(path);
                }

                trackedFiles.Add(path);
            }

            return new ResolveAssemblyReferenceResponse
            {
                BuildEvents = buildEngine.BuildEventQueue,
                CopyLocalFilesCount = taskOutput.CopyLocalFiles.Length,
                DependsOnNETStandard = taskOutput.DependsOnNETStandard,
                DependsOnSystemRuntime = taskOutput.DependsOnSystemRuntime,
                FilesWrittenCount = taskOutput.FilesWritten.Length,
                RelatedFilesCount = taskOutput.RelatedFiles.Length,
                ResolvedDependencyFilesCount = taskOutput.ResolvedDependencyFiles.Length,
                ResolvedFilesCount = taskOutput.ResolvedFiles.Length,
                SatelliteFilesCount = taskOutput.SatelliteFiles.Length,
                ScatterFilesCount = taskOutput.ScatterFiles.Length,
                SerializationAssemblyFilesCount = taskOutput.SerializationAssemblyFiles.Length,
                SuggestedRedirectsCount = taskOutput.SuggestedRedirects.Length,
                TaskItems = taskItemPayloadList,
                TrackedDirectories = trackedDirectories,
                TrackedFiles = trackedFiles,
            };
        }

        private static int EstimateTaskItemCount(ResolveAssemblyReferenceTaskOutput taskOutput)
        {
            return taskOutput.CopyLocalFiles.Length + taskOutput.FilesWritten.Length
                                             + taskOutput.RelatedFiles.Length + taskOutput.ResolvedDependencyFiles.Length
                                             + taskOutput.ResolvedFiles.Length + taskOutput.SatelliteFiles.Length
                                             + taskOutput.ScatterFiles.Length + taskOutput.SerializationAssemblyFiles.Length
                                             + taskOutput.SuggestedRedirects.Length;
        }

        private static void ExtractTaskItemPayloadList(List<ReadOnlyTaskItem> taskItemPayloadList, Dictionary<ITaskItem, ReadOnlyTaskItem> taskItemToPayload, ITaskItem[] taskItems, int responseFieldId)
        {
            foreach (ITaskItem taskItem in taskItems)
            {
                if (!taskItemToPayload.TryGetValue(taskItem, out ReadOnlyTaskItem taskItemPayload))
                {
                    taskItemPayload = new ReadOnlyTaskItem(taskItem.ItemSpec);
                    taskItem.CopyMetadataTo(taskItemPayload);
                    taskItemToPayload[taskItem] = taskItemPayload;
                    taskItemPayloadList.Add(taskItemPayload);
                }

                taskItemPayload.AddResponseFieldId(responseFieldId);
            }
        }
    }
}
