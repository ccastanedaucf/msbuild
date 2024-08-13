using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Serialization;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    public class ResolveAssemblyReferenceService
    {
        public void Execute()
        {
            while (true)
            {
                const string PipeName = "ResolveAssemblyReference.Pipe";

                using NamedPipeServerStream pipe = new(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.WriteThrough,
                    inBufferSize: 16384,
                    outBufferSize: 16384);

                pipe.WaitForConnection();

                using StreamReader reader = new(pipe);
                string str = string.Empty;

                while (true)
                {
                    string? line = reader.ReadLine();

                    if (line == "DONE")
                    {
                        break;
                    }

                    str += line;
                }

                XmlSerializer requestSerializer = new(typeof(ResolveAssemblyReferenceRequest));
                var req = (ResolveAssemblyReferenceRequest)requestSerializer.Deserialize(new StringReader(str))!;
                ResolveAssemblyReferenceResponse resp = HandleRequest(req);

                XmlSerializer responseSerializer = new(typeof(ResolveAssemblyReferenceResponse));
                responseSerializer.Serialize(pipe, resp);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    pipe.WaitForPipeDrain();
                }
                else
                {
                    throw new InvalidOperationException("lol");
                }

                pipe.Disconnect();
            }
        }

        private ResolveAssemblyReferenceResponse HandleRequest(ResolveAssemblyReferenceRequest req)
        {
            EventQueueBuildEngine buildEngine = new();
            ResolveAssemblyReference rarTask = new()
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
                LatestTargetFrameworkDirectories = req.LatestTargetFrameworkDirectories,
                ProfileName = req.ProfileName,
                ResolvedSDKReferences = req.ResolvedSDKReferences,
                SearchPaths = req.SearchPaths,
                ShouldExecuteInProcess = true,
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

            Console.WriteLine("Executing RAR...");
            bool result = rarTask.ExecuteInProcess();
            Console.WriteLine("RAR complete.");
            Console.WriteLine(result);

            return CreateReponse(rarTask, buildEngine);
        }

        private static ResolveAssemblyReferenceResponse CreateReponse(
            ResolveAssemblyReference rarTask,
            EventQueueBuildEngine buildEngine)
        {
            HashSet<ITaskItem> copyLocalFiles = new(rarTask.CopyLocalFiles);

            return new ResolveAssemblyReferenceResponse
            {
                // TODO: Major perf improvement, simply adding the recorded BuildEventArgs to the response
                // accounts for anywhere from 40-50% of RAR-aas overhead. This is a combination of triggering
                // ResolveAssemblyReferenceServiceGateway.LogBuildEvents() and blowing up the response
                // payload size, resulting in slower serialization. RAR will likely need some method of knowing
                // the current verbosity.
                BuildEventArgsQueue = [.. buildEngine.EventQueue],
                NumCopyLocalFiles = rarTask.CopyLocalFiles.Length,
                DependsOnNETStandard = rarTask.DependsOnNETStandard,
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
            };

            ReadOnlyTaskItem[] CreateReadOnlyTaskItems(ITaskItem[] taskItems)
            {
                List<ReadOnlyTaskItem> readOnlyTaskItems = new(taskItems.Length);

                foreach (ITaskItem taskItem in taskItems)
                {
                    readOnlyTaskItems.Add(new ReadOnlyTaskItem(
                        taskItem,
                        isCopyLocalFile: copyLocalFiles.Contains(taskItem)));
                }

                return [.. readOnlyTaskItems];
            }
        }
    }
}
