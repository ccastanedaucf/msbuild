using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Xml.Serialization;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Net.Client;
using Microsoft.Build.BackEnd;
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

            ResolveAssemblyReferenceRequest req = new()
            {
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
                Silent = rarTask.Silent,
                SupportsBindingRedirectGeneration = rarTask.SupportsBindingRedirectGeneration,
                UnresolveFrameworkAssembliesFromHigherFrameworks = rarTask.UnresolveFrameworkAssembliesFromHigherFrameworks,
                IsTaskLoggingEnabled = rarTask.Log.IsTaskInputLoggingEnabled,
                MinimumMessageImportance = minimumMessageImportance,
                AppConfigFile = appConfigFile,
                ProfileName = rarTask.ProfileName,
                StateFile = stateFile,
                TargetedRuntimeVersion = rarTask.TargetedRuntimeVersion,
                TargetFrameworkMoniker = rarTask.TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = rarTask.TargetFrameworkMonikerDisplayName,
                TargetFrameworkVersion = rarTask.TargetFrameworkVersion,
                TargetProcessorArchitecture = rarTask.TargetProcessorArchitecture,
                WarnOrErrorOnTargetArchitectureMismatch = rarTask.WarnOrErrorOnTargetArchitectureMismatch,
                AllowedAssemblyExtensions = rarTask.AllowedAssemblyExtensions,
                AllowedRelatedFileExtensions = rarTask.AllowedRelatedFileExtensions,
                Assemblies = CreateReadOnlyTaskItems(rarTask.Assemblies),
                AssemblyFiles = CreateReadOnlyTaskItems(rarTask.AssemblyFiles),
                CandidateAssemblyFiles = rarTask.CandidateAssemblyFiles,
                FullFrameworkAssemblyTables = CreateReadOnlyTaskItems(rarTask.FullFrameworkAssemblyTables),
                FullFrameworkFolders = rarTask.FullFrameworkFolders,
                FullTargetFrameworkSubsetNames = rarTask.FullTargetFrameworkSubsetNames,
                InstalledAssemblyTables = CreateReadOnlyTaskItems(rarTask.InstalledAssemblyTables),
                InstalledAssemblySubsetTables = CreateReadOnlyTaskItems(rarTask.InstalledAssemblySubsetTables),
                LatestTargetFrameworkDirectories = rarTask.LatestTargetFrameworkDirectories,
                ResolvedSDKReferences = CreateReadOnlyTaskItems(rarTask.ResolvedSDKReferences),
                SearchPaths = rarTask.SearchPaths,
                TargetFrameworkDirectories = rarTask.TargetFrameworkDirectories,
                TargetFrameworkSubsets = rarTask.TargetFrameworkSubsets,
            };

            ResolveAssemblyReferenceResponse resp = ResolveAssemblyReferences(req, rarTask.BuildEngine);
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

        private ResolveAssemblyReferenceResponse ResolveAssemblyReferences(ResolveAssemblyReferenceRequest request, IBuildEngine buildEngine)
        {
            using NamedPipeClientStream pipe = new(".", ResolveAssemblyReferenceService.PipeName, PipeDirection.InOut);
            pipe.Connect(FallbackTimeout);

            SendRequest(pipe, request);
            ResolveAssemblyReferenceResponse response = ReadResponse(pipe);

            // The RAR service will reply with queued build events before the task has completed.
            // Process these while waiting for completion.
            while (!response.IsComplete)
            {
                LogBuildEvents(buildEngine, response.BuildEventArgsQueue);
                response = ReadResponse(pipe);
            }

            LogBuildEvents(buildEngine, response.BuildEventArgsQueue);

            return response;
        }

        private void SendRequest(NamedPipeClientStream pipe, ResolveAssemblyReferenceRequest request)
        {
            // Serialize to temporary buffer to reduce IO calls.
            using MemoryStream memoryStream = new();
            ITranslator translator = BinaryTranslator.GetWriteTranslator(memoryStream);
            translator.Translate(ref request);

            // Delimit message with length.
            pipe.Write(Encoding.UTF8.GetBytes(memoryStream.Length.ToString()));

            // Send the serialized request.
            memoryStream.CopyTo(pipe);
        }

        private ResolveAssemblyReferenceResponse ReadResponse(NamedPipeClientStream pipe)
        {
            // Read the message length.
            using BinaryReader reader = new(pipe, Encoding.Default, leaveOpen: true);
            int messageLength = reader.ReadInt32();

            // Read raw bytes to a temporary buffer to reduce IO calls.
            using MemoryStream memoryStream = new(messageLength);
            byte[] buffer = memoryStream.GetBuffer();
            pipe.Read(buffer, 0, buffer.Length);

            // Deserialize the request.
            memoryStream.Position = 0;
            ITranslator translator = BinaryTranslator.GetReadTranslator(memoryStream, InterningBinaryReader.PoolingBuffer);
            ResolveAssemblyReferenceResponse response = new();
            translator.Translate(ref response);

            return response;
        }

        private static TaskItemSlim[] CreateReadOnlyTaskItems(ITaskItem[] taskItems)
        {
            List<TaskItemSlim> readOnlyTaskItems = new(taskItems.Length);

            foreach (ITaskItem taskItem in taskItems)
            {
                readOnlyTaskItems.Add(new TaskItemSlim(taskItem));
            }

            return readOnlyTaskItems.ToArray();
        }


        private static void SetTaskOutputs(ResolveAssemblyReference rarTask, ResolveAssemblyReferenceResponse response)
        {
            rarTask.DependsOnNETStandard = response.DependsOnNetStandard;
            rarTask.DependsOnSystemRuntime = response.DependsOnSystemRuntime;
            List<ITaskItem> copyLocalFiles = new(response.NumCopyLocalFiles);
            rarTask.FilesWritten = ExtractTaskItems(response.FilesWritten);
            rarTask.RelatedFiles = ExtractTaskItems(response.RelatedFiles);
            rarTask.ResolvedDependencyFiles = ExtractTaskItems(response.ResolvedDependencyFiles);
            rarTask.ResolvedFiles = ExtractTaskItems(response.ResolvedFiles);
            rarTask.SatelliteFiles = ExtractTaskItems(response.SatelliteFiles);
            rarTask.ScatterFiles = ExtractTaskItems(response.ScatterFiles);
            rarTask.SerializationAssemblyFiles = ExtractTaskItems(response.SerializationAssemblyFiles);
            rarTask.SuggestedRedirects = ExtractTaskItems(response.SuggestedRedirects);
            rarTask.UnresolvedAssemblyConflicts = ExtractTaskItems(response.UnresolvedAssemblyConflicts);

            ITaskItem[] ExtractTaskItems(TaskItemSlim[] readOnlyTaskItems)
            {
                ITaskItem[] taskItems = new ITaskItem[readOnlyTaskItems.Length];

                for (int i = 0; i < readOnlyTaskItems.Length; i++)
                {
                    TaskItemSlim readOnlyTaskItem = readOnlyTaskItems[i];

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

        private static void LogBuildEvents(IBuildEngine buildEngine, ResolveAssemblyReferenceBuildEventArgs[] buildEventsArgsQueue)
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
