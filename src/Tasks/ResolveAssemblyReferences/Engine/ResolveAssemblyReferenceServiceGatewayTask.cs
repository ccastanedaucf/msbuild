using System.IO;

using Microsoft.Build.Framework;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Abstractions;
using Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Engine
{
    internal class ResolveAssemblyReferenceServiceGatewayTask : IResolveAssemblyReferenceTask
    {
        private IResolveAssemblyReferenceService Service { get; }

        internal ResolveAssemblyReferenceServiceGatewayTask(IResolveAssemblyReferenceService service)
        {
            Service = service;
        }

        public ResolveAssemblyReferenceTaskOutput Execute(ResolveAssemblyReferenceTaskInput input)
        {
            ResolveAssemblyReferenceRequest req = ConvertTaskInputToRequest(input);
            ResolveAssemblyReferenceResponse resp = Service.ResolveAssemblyReferences(req);
            return ConvertResponseToTaskOutput(resp);
        }

        private static ResolveAssemblyReferenceRequest ConvertTaskInputToRequest(ResolveAssemblyReferenceTaskInput input)
        {
            ReadOnlyTaskItem[] assemblies = CreateReadOnlyTaskItems(input.Assemblies);
            ReadOnlyTaskItem[] assemblyFiles = CreateReadOnlyTaskItems(input.AssemblyFiles);
            ReadOnlyTaskItem[] fullFrameworkAssemblyTables = CreateReadOnlyTaskItems(input.FullFrameworkAssemblyTables);
            ReadOnlyTaskItem[] installedAssemblySubsetTables = CreateReadOnlyTaskItems(input.InstalledAssemblySubsetTables);
            ReadOnlyTaskItem[] installedAssemblyTables = CreateReadOnlyTaskItems(input.InstalledAssemblyTables);
            ReadOnlyTaskItem[] resolvedSdkReferences = CreateReadOnlyTaskItems(input.ResolvedSDKReferences);
            string stateFile = Path.GetFullPath(input.StateFile);

            return new ResolveAssemblyReferenceRequest
            {
                AllowedAssemblyExtensions = input.AllowedAssemblyExtensions,
                AllowedRelatedFileExtensions = input.AllowedRelatedFileExtensions,
                AppConfigFile = input.AppConfigFile,
                Assemblies = assemblies,
                AssemblyFiles = assemblyFiles,
                AutoUnify = input.AutoUnify,
                CandidateAssemblyFiles = input.CandidateAssemblyFiles,
                CopyLocalDependenciesWhenParentReferenceInGac = input.CopyLocalDependenciesWhenParentReferenceInGac,
                DoNotCopyLocalIfInGac = input.DoNotCopyLocalIfInGac,
                FindDependencies = input.FindDependencies,
                FindDependenciesOfExternallyResolvedReferences = input.FindDependenciesOfExternallyResolvedReferences,
                FindRelatedFiles = input.FindRelatedFiles,
                FindSatellites = input.FindSatellites,
                FindSerializationAssemblies = input.FindSerializationAssemblies,
                FullFrameworkAssemblyTables = fullFrameworkAssemblyTables,
                FullFrameworkFolders = input.FullFrameworkFolders,
                FullTargetFrameworkSubsetNames = input.FullTargetFrameworkSubsetNames,
                IgnoreDefaultInstalledAssemblySubsetTables = input.IgnoreDefaultInstalledAssemblySubsetTables,
                IgnoreDefaultInstalledAssemblyTables = input.IgnoreDefaultInstalledAssemblyTables,
                IgnoreTargetFrameworkAttributeVersionMismatch = input.IgnoreTargetFrameworkAttributeVersionMismatch,
                IgnoreVersionForFrameworkReferences = input.IgnoreVersionForFrameworkReferences,
                InstalledAssemblySubsetTables = installedAssemblySubsetTables,
                InstalledAssemblyTables = installedAssemblyTables,
                LatestTargetFrameworkDirectories = input.LatestTargetFrameworkDirectories,
                ProfileName = input.ProfileName,
                ResolvedSDKReferences = resolvedSdkReferences,
                SearchPaths = input.SearchPaths,
                Silent = input.Silent,
                StateFile = stateFile,
                SupportsBindingRedirectGeneration = input.SupportsBindingRedirectGeneration,
                TargetFrameworkDirectories = input.TargetFrameworkDirectories,
                TargetFrameworkMoniker = input.TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = input.TargetFrameworkMonikerDisplayName,
                TargetFrameworkSubsets = input.TargetFrameworkSubsets,
                TargetFrameworkVersion = input.TargetFrameworkVersion,
                TargetProcessorArchitecture = input.TargetProcessorArchitecture,
                TargetedRuntimeVersion = input.TargetedRuntimeVersion,
                UnresolveFrameworkAssembliesFromHigherFrameworks = input.UnresolveFrameworkAssembliesFromHigherFrameworks,
                WarnOrErrorOnTargetArchitectureMismatch = input.WarnOrErrorOnTargetArchitectureMismatch
            };
        }

        private static ReadOnlyTaskItem[] CreateReadOnlyTaskItems(ITaskItem[] taskItems)
        {
            var readOnlyTaskItems = new ReadOnlyTaskItem[taskItems.Length];

            for (int i = 0; i < taskItems.Length; i++)
            {
                ITaskItem taskItem = taskItems[i];
                var readOnlyTaskItem = new ReadOnlyTaskItem(taskItem.ItemSpec);
                taskItem.CopyMetadataTo(readOnlyTaskItem);
                readOnlyTaskItems[i] = readOnlyTaskItem;
            }

            return readOnlyTaskItems;
        }

        private static ResolveAssemblyReferenceTaskOutput ConvertResponseToTaskOutput(ResolveAssemblyReferenceResponse resp)
        {
            ITaskItem[] copyLocalFiles = new ITaskItem[resp.CopyLocalFilesCount];
            ITaskItem[] filesWritten = new ITaskItem[resp.FilesWrittenCount];
            ITaskItem[] relatedFiles = new ITaskItem[resp.RelatedFilesCount];
            ITaskItem[] resolvedDependencyFiles = new ITaskItem[resp.ResolvedDependencyFilesCount];
            ITaskItem[] resolvedFiles = new ITaskItem[resp.ResolvedFilesCount];
            ITaskItem[] satelliteFiles = new ITaskItem[resp.SatelliteFilesCount];
            ITaskItem[] scatterFiles = new ITaskItem[resp.ScatterFilesCount];
            ITaskItem[] serializationAssemblyFiles = new ITaskItem[resp.SerializationAssemblyFilesCount];
            ITaskItem[] suggestedRedirects = new ITaskItem[resp.SuggestedRedirectsCount];

            int copyLocalFilesId = 0;
            int filesWrittenId = 0;
            int relatedFilesId = 0;
            int resolvedDependencyFilesId = 0;
            int resolvedFilesId = 0;
            int satelliteFilesId = 0;
            int scatterFilesId = 0;
            int serializationAssemblyFilesId = 0;
            int suggestedRedirectsId = 0;

            foreach (ReadOnlyTaskItem taskItemPayload in resp.TaskItems)
            {
                foreach (int id in taskItemPayload.ResponseFieldIds)
                {
                    switch (id)
                    {
                        case 0:
                            copyLocalFiles[copyLocalFilesId] = new TaskItem(taskItemPayload);
                            copyLocalFilesId++;
                            break;
                        case 1:
                            filesWritten[filesWrittenId] = new TaskItem(taskItemPayload);
                            filesWrittenId++;
                            break;
                        case 2:
                            relatedFiles[relatedFilesId] = new TaskItem(taskItemPayload);
                            relatedFilesId++;
                            break;
                        case 3:
                            resolvedDependencyFiles[resolvedDependencyFilesId] = new TaskItem(taskItemPayload);
                            resolvedDependencyFilesId++;
                            break;
                        case 4:
                            resolvedFiles[resolvedFilesId] = new TaskItem(taskItemPayload);
                            resolvedFilesId++;
                            break;
                        case 5:
                            satelliteFiles[satelliteFilesId] = new TaskItem(taskItemPayload);
                            satelliteFilesId++;
                            break;
                        case 6:
                            scatterFiles[scatterFilesId] = new TaskItem(taskItemPayload);
                            scatterFilesId++;
                            break;
                        case 7:
                            serializationAssemblyFiles[serializationAssemblyFilesId] = new TaskItem(taskItemPayload);
                            serializationAssemblyFilesId++;
                            break;
                        case 8:
                            suggestedRedirects[suggestedRedirectsId] = new TaskItem(taskItemPayload);
                            suggestedRedirectsId++;
                            break;
                    }
                }
            }

            return new ResolveAssemblyReferenceTaskOutput
            {
                CopyLocalFiles = copyLocalFiles,
                FilesWritten = filesWritten,
                DependsOnNETStandard = resp.DependsOnNETStandard,
                DependsOnSystemRuntime = resp.DependsOnSystemRuntime,
                RelatedFiles = relatedFiles,
                ResolvedDependencyFiles = resolvedDependencyFiles,
                ResolvedFiles = resolvedFiles,
                SatelliteFiles = satelliteFiles,
                ScatterFiles = scatterFiles,
                SerializationAssemblyFiles = serializationAssemblyFiles,
                SuggestedRedirects = suggestedRedirects
            };
        }
    }
}
