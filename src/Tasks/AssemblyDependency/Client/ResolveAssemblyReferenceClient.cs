using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Xml.Serialization;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Net.Client;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class ResolveAssemblyReferenceClient
    {
        private const int FallbackTimeout = 5000;

        public bool Execute(ResolveAssemblyReference rarTask)
        {
            // RAR service may have a different working directory, so convert potential relative paths to absolute.
            string? appConfigFile = rarTask.AppConfigFile != null ? Path.GetFullPath(rarTask.AppConfigFile) : null;
            string? stateFile = rarTask.StateFile != null ? Path.GetFullPath(rarTask.StateFile) : null;

            // Allow the service to avoid processing messages which would never be logged by the client.
            MessageImportance minimumMessageImportance = GetMinimumMessageImportance(rarTask.Log);

            ResolveAssemblyReferencesRequest req = new()
            {
                AppConfigFile = appConfigFile,
                AutoUnify = rarTask.AutoUnify,
                CopyLocalDependenciesWhenParentReferenceInGac = rarTask.CopyLocalDependenciesWhenParentReferenceInGac,
                DoNotCopyLocalIfInGac = rarTask.DoNotCopyLocalIfInGac,
                FindDependencies = rarTask.FindDependencies,
                FindDependenciesOfExternallyResolvedReferences = rarTask.FindDependenciesOfExternallyResolvedReferences,
                FindRelatedFiles = rarTask.FindRelatedFiles,
                FindSatellites = rarTask.FindSatellites,
                FindSerializationAssemblies = rarTask.FindSerializationAssemblies,
                IgnoreDefaultInstalledAssemblySubsetTables = rarTask.IgnoreDefaultInstalledAssemblySubsetTables,
                IgnoreDefaultInstalledAssemblyTables = rarTask.IgnoreDefaultInstalledAssemblyTables,
                IgnoreTargetFrameworkAttributeVersionMismatch = rarTask.IgnoreTargetFrameworkAttributeVersionMismatch,
                IgnoreVersionForFrameworkReferences = rarTask.IgnoreVersionForFrameworkReferences,
                ProfileName = rarTask.ProfileName,
                Silent = rarTask.Silent,
                StateFile = stateFile,
                SupportsBindingRedirectGeneration = rarTask.SupportsBindingRedirectGeneration,
                TargetFrameworkMoniker = rarTask.TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = rarTask.TargetFrameworkMonikerDisplayName,
                TargetFrameworkVersion = rarTask.TargetFrameworkVersion,
                TargetProcessorArchitecture = rarTask.TargetProcessorArchitecture,
                TargetedRuntimeVersion = rarTask.TargetedRuntimeVersion,
                UnresolveFrameworkAssembliesFromHigherFrameworks = rarTask.UnresolveFrameworkAssembliesFromHigherFrameworks,
                WarnOrErrorOnTargetArchitectureMismatch = rarTask.WarnOrErrorOnTargetArchitectureMismatch,
                MinimumMessageImportance = (int)minimumMessageImportance,
                IsTaskLoggingEnabled = rarTask.Log.IsTaskInputLoggingEnabled,
            };
            req.AllowedAssemblyExtensions.Add(rarTask.AllowedAssemblyExtensions);
            req.AllowedRelatedFileExtensions.Add(rarTask.AllowedRelatedFileExtensions);
            req.Assemblies.Add(CreateReadOnlyTaskItems(rarTask.Assemblies));
            req.AssemblyFiles.Add(CreateReadOnlyTaskItems(rarTask.AssemblyFiles));
            req.CandidateAssemblyFiles.Add(rarTask.CandidateAssemblyFiles);
            req.FullFrameworkAssemblyTables.Add(CreateReadOnlyTaskItems(rarTask.FullFrameworkAssemblyTables));
            req.FullFrameworkFolders.Add(rarTask.FullFrameworkFolders);
            req.FullTargetFrameworkSubsetNames.Add(rarTask.FullTargetFrameworkSubsetNames);
            req.InstalledAssemblySubsetTables.Add(CreateReadOnlyTaskItems(rarTask.InstalledAssemblySubsetTables));
            req.InstalledAssemblyTables.Add(CreateReadOnlyTaskItems(rarTask.InstalledAssemblyTables));
            req.LatestTargetFrameworkDirectories.Add(rarTask.LatestTargetFrameworkDirectories);
            req.ResolvedSdkReferences.Add(CreateReadOnlyTaskItems(rarTask.ResolvedSDKReferences));
            req.SearchPaths.Add(rarTask.SearchPaths);
            req.TargetFrameworkDirectories.Add(rarTask.TargetFrameworkDirectories);
            req.TargetFrameworkSubsets.Add(rarTask.TargetFrameworkSubsets);

            ResolveAssemblyReferencesReply resp = ResolveAssemblyReferences(req, rarTask.BuildEngine);
            SetTaskOutputs(rarTask, resp);

            return resp.Success;
        }

        private MessageImportance GetMinimumMessageImportance(TaskLoggingHelper log)
        {
            if (log.LogsMessagesOfImportance(MessageImportance.Low))
            {
                return MessageImportance.Low;
            }

            if (log.LogsMessagesOfImportance(MessageImportance.Normal))
            {
                return MessageImportance.Normal;
            }

            return MessageImportance.High;
        }

        private ResolveAssemblyReferencesReply ResolveAssemblyReferences(ResolveAssemblyReferencesRequest req, IBuildEngine buildEngine)
        {
            using NamedPipeClientStream pipe = new(".", ResolveAssemblyReferenceService.PipeName, PipeDirection.InOut);
            pipe.Connect(FallbackTimeout);

            MessageExtensions.WriteDelimitedTo(req, pipe);
            ResolveAssemblyReferencesReply reply = ResolveAssemblyReferencesReply.Parser.ParseDelimitedFrom(pipe);

            // The RAR service will reply with queued build events before the task has completed.
            // Process these while waiting for completion.
            while (!reply.IsCompleted)
            {
                LogBuildEvents(buildEngine, reply.BuildEventArgsQueue);
                reply = ResolveAssemblyReferencesReply.Parser.ParseDelimitedFrom(pipe);
            }

            LogBuildEvents(buildEngine, reply.BuildEventArgsQueue);

            return reply;
        }

        private static List<ReadOnlyTaskItem> CreateReadOnlyTaskItems(ITaskItem[] taskItems)
        {
            List<ReadOnlyTaskItem> readOnlyTaskItems = new(taskItems.Length);

            foreach (ITaskItem taskItem in taskItems)
            {
                readOnlyTaskItems.Add(new ReadOnlyTaskItem(taskItem));
            }

            return readOnlyTaskItems;
        }

        private static void SetTaskOutputs(ResolveAssemblyReference rarTask, ResolveAssemblyReferencesReply resp)
        {
            rarTask.DependsOnNETStandard = resp.DependsOnNetStandard;
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

            ITaskItem[] ExtractTaskItems(RepeatedField<ReadOnlyTaskItem> readOnlyTaskItems)
            {
                ITaskItem[] taskItems = new ITaskItem[readOnlyTaskItems.Count];

                for (int i = 0; i < readOnlyTaskItems.Count; i++)
                {
                    ReadOnlyTaskItem readOnlyTaskItem = readOnlyTaskItems[i];

                    TaskItem taskItem = new(readOnlyTaskItem);
                    taskItems[i] = taskItem;

                    if (readOnlyTaskItem.IsCopyLocalFile)
                    {
                        copyLocalFiles.Add(taskItem);
                    }
                }

                return [.. taskItems];
            }
        }

        private static void LogBuildEvents(IBuildEngine buildEngine, ICollection<ResolveAssemblyReferenceBuildEventArgs> buildEventsArgsQueue)
        {
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
                            buildEventArgs.SenderName,
                            eventTimestamp,
                            buildEventArgs.MessageArgs.Count > 0 ? [.. buildEventArgs.MessageArgs] : null);

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
                            buildEventArgs.MessageArgs.Count > 0 ? [.. buildEventArgs.MessageArgs] : null);

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
                            buildEventArgs.MessageArgs.Count > 0 ? [.. buildEventArgs.MessageArgs] : null);

                        buildEngine.LogWarningEvent(warningEventArgs);
                        break;
                }
            }
        }
    }
}
