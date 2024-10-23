// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class ResolveAssemblyReferenceClient : ResolveAssemblyReferenceNodeBase
    {
        private const int FallbackTimeout = 5000;

        private static readonly byte[] ReusableBuffer = new byte[DefaultBufferSizeInBytes];

        private readonly MemoryStream _memoryStream = new(DefaultBufferSizeInBytes);

        public bool Execute(ResolveAssemblyReference rarTask)
        {
            // RAR service may have a different working directory, so convert potential relative paths to absolute.
            string? appConfigFile = rarTask.AppConfigFile != null ? Path.GetFullPath(rarTask.AppConfigFile) : null;
            string? stateFile = rarTask.StateFile != null ? Path.GetFullPath(rarTask.StateFile) : null;

            // Allow the service to avoid processing messages which would never be logged by the client.
            MessageImportance minimumMessageImportance = GetMinimumMessageImportance(rarTask.Log);

            RarExecutionRequest req = new()
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
                Assemblies = ConvertTaskItems(rarTask.Assemblies),
                AssemblyFiles = ConvertTaskItems(rarTask.AssemblyFiles),
                CandidateAssemblyFiles = rarTask.CandidateAssemblyFiles,
                FullFrameworkAssemblyTables = ConvertTaskItems(rarTask.FullFrameworkAssemblyTables),
                FullFrameworkFolders = rarTask.FullFrameworkFolders,
                FullTargetFrameworkSubsetNames = rarTask.FullTargetFrameworkSubsetNames,
                InstalledAssemblyTables = ConvertTaskItems(rarTask.InstalledAssemblyTables),
                InstalledAssemblySubsetTables = ConvertTaskItems(rarTask.InstalledAssemblySubsetTables),
                LatestTargetFrameworkDirectories = rarTask.LatestTargetFrameworkDirectories,
                ResolvedSDKReferences = ConvertTaskItems(rarTask.ResolvedSDKReferences),
                SearchPaths = rarTask.SearchPaths,
                TargetFrameworkDirectories = rarTask.TargetFrameworkDirectories,
                TargetFrameworkSubsets = rarTask.TargetFrameworkSubsets,
            };

            RarExecutionResponse resp = ResolveAssemblyReferences(req, rarTask.BuildEngine);
            SetTaskOutputs(rarTask, resp);

            return resp.Success;

            static RarTaskItemInput[] ConvertTaskItems(ITaskItem[] taskItems)
            {
                List<RarTaskItemInput> requestItems = new(taskItems.Length);

                foreach (ITaskItem taskItem in taskItems)
                {
                    requestItems.Add(new RarTaskItemInput(taskItem));
                }

                return [.. requestItems];
            }
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

        private RarExecutionResponse ResolveAssemblyReferences(RarExecutionRequest request, IBuildEngine buildEngine)
        {
            using NamedPipeClientStream pipe = new(".", ResolveAssemblyReferenceService.PipeName, PipeDirection.InOut);
            pipe.Connect(FallbackTimeout);

            SendRequest(pipe, request);
            RarExecutionResponse response = ReadResponse(pipe);

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

        private void SendRequest(NamedPipeClientStream pipe, RarExecutionRequest request)
        {
            // Serialize to temporary buffer to reduce IO calls.
            Serialize(request, _memoryStream);
            SetMessageLength(_memoryStream);
            WritePipe(pipe, _memoryStream);
        }

        private RarExecutionResponse ReadResponse(NamedPipeClientStream pipe)
        {
            // Read raw bytes to a temporary buffer to reduce IO calls.
            int bytesRead = ReadPipe(pipe, ReusableBuffer, 0, MessageOffsetInBytes);
            int messageLength = ParseMessageLength(ReusableBuffer);

            // Additional reads for the remaining message.
            byte[] buffer = EnsureBufferSize(ReusableBuffer, messageLength);
            bytesRead = ReadPipe(pipe, buffer, bytesRead, messageLength);

            if (bytesRead > messageLength)
            {
                throw new Exception("Should not be reading into next message!");
            }

            return Deserialize<RarExecutionResponse>(buffer, messageLength);
        }


        private static void SetTaskOutputs(ResolveAssemblyReference rarTask, RarExecutionResponse response)
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

            ITaskItem[] ExtractTaskItems(RarTaskItemOutput[] responseItems)
            {
                ITaskItem[] taskItems = new ITaskItem[responseItems.Length];

                for (int i = 0; i < responseItems.Length; i++)
                {
                    RarTaskItemOutput responseItem = responseItems[i];

                    TaskItem taskItem = new(responseItem.EvaluatedIncludeEscaped);
                    taskItems[i] = taskItem;

                    if (responseItem.IsCopyLocalFile)
                    {
                        copyLocalFiles.Add(taskItem);
                    }
                }

                return [.. taskItems];
            }
        }

        private static void LogBuildEvents(IBuildEngine buildEngine, RarBuildEventArgs[] buildEventsArgsQueue)
        {
            foreach (RarBuildEventArgs buildEventArgs in buildEventsArgsQueue)
            {
                DateTime eventTimestamp = new(buildEventArgs.EventTimestamp, DateTimeKind.Utc);

                switch (buildEventArgs.EventType)
                {
                    case RarBuildEventArgsType.Error:
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
                            buildEventArgs.MessageArgs);

                        buildEngine.LogErrorEvent(errorEventArgs);
                        break;
                    case RarBuildEventArgsType.Message:
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
                    case RarBuildEventArgsType.Warning:
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
