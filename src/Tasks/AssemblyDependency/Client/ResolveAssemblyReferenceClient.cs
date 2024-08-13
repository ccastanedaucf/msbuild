using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class ResolveAssemblyReferenceClient
    {
        public void Execute(ResolveAssemblyReference rarTask)
        {
            ResolveAssemblyReferenceRequest req = new()
            {
                AllowedAssemblyExtensions = rarTask.AllowedAssemblyExtensions,
                AllowedRelatedFileExtensions = rarTask.AllowedRelatedFileExtensions,
                AppConfigFile = rarTask.AppConfigFile,
                Assemblies = CreateReadOnlyTaskItems(rarTask.Assemblies),
                AssemblyFiles = CreateReadOnlyTaskItems(rarTask.AssemblyFiles),
                AutoUnify = rarTask.AutoUnify,
                CandidateAssemblyFiles = rarTask.CandidateAssemblyFiles,
                CopyLocalDependenciesWhenParentReferenceInGac = rarTask.CopyLocalDependenciesWhenParentReferenceInGac,
                DoNotCopyLocalIfInGac = rarTask.DoNotCopyLocalIfInGac,
                FindDependencies = rarTask.FindDependencies,
                FindDependenciesOfExternallyResolvedReferences = rarTask.FindDependenciesOfExternallyResolvedReferences,
                FindRelatedFiles = rarTask.FindRelatedFiles,
                FindSatellites = rarTask.FindSatellites,
                FindSerializationAssemblies = rarTask.FindSerializationAssemblies,
                FullFrameworkAssemblyTables = CreateReadOnlyTaskItems(rarTask.FullFrameworkAssemblyTables),
                FullFrameworkFolders = rarTask.FullFrameworkFolders,
                FullTargetFrameworkSubsetNames = rarTask.FullTargetFrameworkSubsetNames,
                IgnoreDefaultInstalledAssemblySubsetTables = rarTask.IgnoreDefaultInstalledAssemblySubsetTables,
                IgnoreDefaultInstalledAssemblyTables = rarTask.IgnoreDefaultInstalledAssemblyTables,
                IgnoreTargetFrameworkAttributeVersionMismatch = rarTask.IgnoreTargetFrameworkAttributeVersionMismatch,
                IgnoreVersionForFrameworkReferences = rarTask.IgnoreVersionForFrameworkReferences,
                InstalledAssemblySubsetTables = CreateReadOnlyTaskItems(rarTask.InstalledAssemblySubsetTables),
                InstalledAssemblyTables = CreateReadOnlyTaskItems(rarTask.InstalledAssemblyTables),
                LatestTargetFrameworkDirectories = rarTask.LatestTargetFrameworkDirectories,
                ProfileName = rarTask.ProfileName,
                ResolvedSDKReferences = CreateReadOnlyTaskItems(rarTask.ResolvedSDKReferences),
                SearchPaths = rarTask.SearchPaths,
                Silent = rarTask.Silent,
                StateFile = rarTask.StateFile != null ? Path.GetFullPath(rarTask.StateFile) : null,
                SupportsBindingRedirectGeneration = rarTask.SupportsBindingRedirectGeneration,
                TargetFrameworkDirectories = rarTask.TargetFrameworkDirectories,
                TargetFrameworkMoniker = rarTask.TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = rarTask.TargetFrameworkMonikerDisplayName,
                TargetFrameworkSubsets = rarTask.TargetFrameworkSubsets,
                TargetFrameworkVersion = rarTask.TargetFrameworkVersion,
                TargetProcessorArchitecture = rarTask.TargetProcessorArchitecture,
                TargetedRuntimeVersion = rarTask.TargetedRuntimeVersion,
                UnresolveFrameworkAssembliesFromHigherFrameworks = rarTask.UnresolveFrameworkAssembliesFromHigherFrameworks,
                WarnOrErrorOnTargetArchitectureMismatch = rarTask.WarnOrErrorOnTargetArchitectureMismatch
            };

            ResolveAssemblyReferenceResponse resp = ResolveAssemblyReferences(req);
            LogBuildEvents(rarTask.BuildEngine, resp.BuildEventArgsQueue);
            SetTaskOutputs(rarTask, resp);
        }

        private ResolveAssemblyReferenceResponse ResolveAssemblyReferences(ResolveAssemblyReferenceRequest req)
        {
            const string PipeName = "ResolveAssemblyReference.Pipe";

            // Temporary fix to make bootstrap build pass without process spawning implemented
            // const int FallbackTimeout = 5000;

            using NamedPipeClientStream pipe = new(".", PipeName, PipeDirection.InOut, PipeOptions.WriteThrough);

            pipe.Connect();

            XmlSerializer requestSerializer = new(typeof(ResolveAssemblyReferenceRequest));
            requestSerializer.Serialize(pipe, req);

#pragma warning disable CA2000 // Dispose objects before losing scope
            StreamWriter sw = new(pipe);
#pragma warning restore CA2000 // Dispose objects before losing scope

            sw.WriteLine();
            sw.WriteLine("DONE");
            sw.Flush();

            XmlSerializer responseSerializer = new(typeof(ResolveAssemblyReferenceResponse));
            return (ResolveAssemblyReferenceResponse)responseSerializer.Deserialize(pipe)!;
        }

        private static ReadOnlyTaskItem[] CreateReadOnlyTaskItems(ITaskItem[] taskItems)
        {
            List<ReadOnlyTaskItem> readOnlyTaskItems = new(taskItems.Length);

            foreach (ITaskItem taskItem in taskItems)
            {
                readOnlyTaskItems.Add(new ReadOnlyTaskItem(taskItem));
            }

            return [.. readOnlyTaskItems];
        }

        private static void SetTaskOutputs(ResolveAssemblyReference rarTask, ResolveAssemblyReferenceResponse resp)
        {
            rarTask.DependsOnNETStandard = resp.DependsOnNETStandard;
            rarTask.DependsOnSystemRuntime = resp.DependsOnSystemRuntime;

            List<ITaskItem> copyLocalFiles = new(resp.NumCopyLocalFiles);

            rarTask.FilesWritten = ExtractTaskItems(resp.FilesWritten);
            rarTask.RelatedFiles = ExtractTaskItems(resp.RelatedFiles);
            rarTask.ResolvedDependencyFiles = ExtractTaskItems(resp.ResolvedDependencyFiles);
            rarTask.ResolvedFiles = ExtractTaskItems(resp.ResolvedFiles);
            rarTask.SatelliteFiles = ExtractTaskItems(resp.SatelliteFiles);
            rarTask.ScatterFiles = ExtractTaskItems(resp.ScatterFiles);
            rarTask.SerializationAssemblyFiles = ExtractTaskItems(resp.SerializationAssemblyFiles);
            rarTask.SuggestedRedirects = ExtractTaskItems(resp.SuggestedRedirects);
            rarTask.UnresolvedAssemblyConflicts = ExtractTaskItems(resp.UnresolvedAssemblyConflicts);

            rarTask.CopyLocalFiles = [.. copyLocalFiles];

            ITaskItem[] ExtractTaskItems(ReadOnlyTaskItem[] readOnlyTaskItems)
            {
                List<ITaskItem> taskItems = new(readOnlyTaskItems.Length);

                foreach (ReadOnlyTaskItem readOnlyTaskItem in readOnlyTaskItems)
                {
                    // TODO: Perf improvement, constructing Utilities.TaskItems accounts for ~10% of RAR-aas overhead
                    // due to slow setting of metadata in the backing CopyOnWriteDictionary.
                    TaskItem taskItem = new(readOnlyTaskItem);
                    taskItems.Add(taskItem);

                    if (readOnlyTaskItem.IsCopyLocalFile)
                    {
                        copyLocalFiles.Add(taskItem);
                    }
                }

                return [.. taskItems];
            }
        }

        private static void LogBuildEvents(IBuildEngine buildEngine, List<ResolveAssemblyReferenceBuildEventArgs> buildEventsArgsQueue)
        {
            // TODO: Perf improvement, LogBuildEvents() accounts for ~10% of RAR-aas overhead.
            // This is a result of logging on silent verbosities, triggering garbage collection,
            // and reconstruction of thousands of BuildEventArgs objects.
            foreach (ResolveAssemblyReferenceBuildEventArgs buildEventArgs in buildEventsArgsQueue)
            {
                DateTime eventTimestamp = new(buildEventArgs.EventTimestamp, DateTimeKind.Utc);

                switch (buildEventArgs.BuildEventArgsType)
                {
                    case BuildEventArgsType.Error:
                        BuildErrorEventArgs errorEventArgs = new(
                            buildEventArgs.Subcategory,
                            buildEventArgs.Code,
                            buildEventArgs.File,
                            buildEventArgs.LineNumber,
                            buildEventArgs.ColumnNumber,
                            buildEventArgs.EndLineNumber,
                            buildEventArgs.EndColumnNumber,
                            buildEventArgs.Message,
                            buildEventArgs.HelpKeyword,
                            buildEventArgs.SenderName);

                        buildEngine.LogErrorEvent(errorEventArgs);
                        break;
                    case BuildEventArgsType.Message:
                        BuildMessageEventArgs messageEventArgs = new(
                            buildEventArgs.Subcategory,
                            buildEventArgs.Code,
                            buildEventArgs.File,
                            buildEventArgs.LineNumber,
                            buildEventArgs.ColumnNumber,
                            buildEventArgs.EndLineNumber,
                            buildEventArgs.EndColumnNumber,
                            buildEventArgs.Message,
                            buildEventArgs.HelpKeyword,
                            buildEventArgs.SenderName,
                            (MessageImportance)buildEventArgs.Importance,
                            eventTimestamp,
                            buildEventArgs.MessageArgs);

                        buildEngine.LogMessageEvent(messageEventArgs);
                        break;
                    case BuildEventArgsType.Warning:
                        BuildWarningEventArgs warningEventArgs = new(
                            buildEventArgs.Subcategory,
                            buildEventArgs.Code,
                            buildEventArgs.File,
                            buildEventArgs.LineNumber,
                            buildEventArgs.ColumnNumber,
                            buildEventArgs.EndLineNumber,
                            buildEventArgs.EndColumnNumber,
                            buildEventArgs.Message,
                            buildEventArgs.HelpKeyword,
                            buildEventArgs.SenderName,
                            eventTimestamp,
                            buildEventArgs.MessageArgs);

                        buildEngine.LogWarningEvent(warningEventArgs);
                        break;
                }
            }
        }
    }
}
