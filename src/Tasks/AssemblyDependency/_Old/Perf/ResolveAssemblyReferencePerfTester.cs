
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Net.Client;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    public class ResolveAssemblyReferencePerfTester
    {
        private const int FallbackTimeout = 5000;

        public async Task<bool> Execute()
        {
            Task serverTask = Task.Run(() => RunServerAsync());
            ResolveAssemblyReferenceResponseSlim resp = RunClient();
            await serverTask;

            return resp.Success;
        }

        private ResolveAssemblyReferenceResponseSlim RunClient()
        {
            using NamedPipeClientStream pipeClient = new(".", ResolveAssemblyReferenceService.PipeName, PipeDirection.InOut);
            pipeClient.Connect(FallbackTimeout);

            ResolveAssemblyReferenceRequestSlim request = new()
            {
                AutoUnify = true,
                // CopyLocalDependenciesWhenParentReferenceInGac = rarTask.CopyLocalDependenciesWhenParentReferenceInGac,
                // DoNotCopyLocalIfInGac = rarTask.DoNotCopyLocalIfInGac,
                // FindDependencies = rarTask.FindDependencies,
                // FindDependenciesOfExternallyResolvedReferences = rarTask.FindDependenciesOfExternallyResolvedReferences,
                // FindRelatedFiles = rarTask.FindRelatedFiles,
                // FindSatellites = rarTask.FindSatellites,
                // FindSerializationAssemblies = rarTask.FindSerializationAssemblies,
                // IgnoreDefaultInstalledAssemblySubsetTables = rarTask.IgnoreDefaultInstalledAssemblySubsetTables,
                // IgnoreDefaultInstalledAssemblyTables = rarTask.IgnoreDefaultInstalledAssemblyTables,
                // IgnoreTargetFrameworkAttributeVersionMismatch = rarTask.IgnoreTargetFrameworkAttributeVersionMismatch,
                // IgnoreVersionForFrameworkReferences = rarTask.IgnoreVersionForFrameworkReferences,
                // Silent = rarTask.Silent,
                // SupportsBindingRedirectGeneration = rarTask.SupportsBindingRedirectGeneration,
                // UnresolveFrameworkAssembliesFromHigherFrameworks = rarTask.UnresolveFrameworkAssembliesFromHigherFrameworks,
                // IsTaskLoggingEnabled = rarTask.Log.IsTaskInputLoggingEnabled,
                // MinimumMessageImportance = minimumMessageImportance,
                // AppConfigFile = appConfigFile,
                // ProfileName = rarTask.ProfileName,
                // StateFile = stateFile,
                // TargetedRuntimeVersion = rarTask.TargetedRuntimeVersion,
                // TargetFrameworkMoniker = rarTask.TargetFrameworkMoniker,
                // TargetFrameworkMonikerDisplayName = rarTask.TargetFrameworkMonikerDisplayName,
                // TargetFrameworkVersion = rarTask.TargetFrameworkVersion,
                // TargetProcessorArchitecture = rarTask.TargetProcessorArchitecture,
                // WarnOrErrorOnTargetArchitectureMismatch = rarTask.WarnOrErrorOnTargetArchitectureMismatch,
                // AllowedAssemblyExtensions = rarTask.AllowedAssemblyExtensions,
                // AllowedRelatedFileExtensions = rarTask.AllowedRelatedFileExtensions,
                // Assemblies = [],
                // AssemblyFiles = [],
                // CandidateAssemblyFiles = rarTask.CandidateAssemblyFiles,
                // FullFrameworkAssemblyTables = [],
                // FullFrameworkFolders = rarTask.FullFrameworkFolders,
                // FullTargetFrameworkSubsetNames = rarTask.FullTargetFrameworkSubsetNames,
                // InstalledAssemblyTables = [],
                // InstalledAssemblySubsetTables = [],
                // LatestTargetFrameworkDirectories = rarTask.LatestTargetFrameworkDirectories,
                // ResolvedSDKReferences = CreateReadOnlyTaskItems(rarTask.ResolvedSDKReferences),
                // SearchPaths = rarTask.SearchPaths,
                // TargetFrameworkDirectories = rarTask.TargetFrameworkDirectories,
                // TargetFrameworkSubsets = rarTask.TargetFrameworkSubsets,
            };

            SendRequest(pipeClient, request);
            ResolveAssemblyReferenceResponseSlim response = ReadResponse(pipeClient);

            // The RAR service will reply with queued build events before the task has completed.
            // Process these while waiting for completion.
            while (!response.IsComplete)
            {
                response = ReadResponse(pipeClient);
            }

            return response;
        }

        internal async Task RunServerAsync(CancellationToken cancellationToken = default)
        {
            using NamedPipeServerStream pipeServer = new(
                ResolveAssemblyReferenceService.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                inBufferSize: 16384,
                outBufferSize: 16384);

            await pipeServer.WaitForConnectionAsync(cancellationToken);

            Stopwatch e2eTime = new();
            e2eTime.Start();
            Console.WriteLine($"Connected to client.");

            try
            {

                ResolveAssemblyReferenceRequestSlim request = ReadRequest(pipeServer);
                ResolveAssemblyReferenceResponseSlim response = new ResolveAssemblyReferenceResponseSlim()
                {
                    IsComplete = true,
                    Success = true,
                };

                Console.WriteLine($"Writing response...");
                SendResponse(pipeServer, response);

                // Avoid replaying build events on future runs.
                response.BuildEventArgsQueue = [];

                Console.WriteLine($"Waiting for pipe drain...");
                pipeServer.WaitForPipeDrain();

                e2eTime.Stop();
                Console.WriteLine($"Request completed in {e2eTime.ElapsedMilliseconds} ms.");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }

            pipeServer.Disconnect();
        }

        private void SendRequest(NamedPipeClientStream pipe, ResolveAssemblyReferenceRequestSlim request)
        {
            // Serialize to temporary buffer to reduce IO calls.
            using MemoryStream memoryStream = new();
            ITranslator translator = BinaryTranslator.GetWriteTranslator(memoryStream);
            translator.Translate(ref request);
            Console.WriteLine($"Client: Serialized response of length '{memoryStream.Length}'.");

            // Delimit message with length.
            using BinaryWriter lengthWriter = new(pipe, Encoding.Default, leaveOpen: true);
            lengthWriter.Write((int)memoryStream.Length);

            // Send the serialized request.
            memoryStream.Position = 0;
            memoryStream.CopyTo(pipe);

            Console.WriteLine("Client: Sent full message.");
        }

        private ResolveAssemblyReferenceRequestSlim ReadRequest(NamedPipeServerStream pipe)
        {
            // Read the message length.
            using BinaryReader lengthReader = new(pipe, Encoding.Default, leaveOpen: true);
            int messageLength = lengthReader.ReadInt32();
            Console.WriteLine($"Server: Read message length: '{messageLength}'");

            // Read raw bytes to a temporary buffer to reduce IO calls.
            byte[] buffer = new byte[messageLength];
            _ = pipe.Read(buffer, 0, buffer.Length);
            using MemoryStream memoryStream = new(buffer);

            ITranslator translator = BinaryTranslator.GetReadTranslator(memoryStream, InterningBinaryReader.PoolingBuffer);
            ResolveAssemblyReferenceRequestSlim request = new();
            translator.Translate(ref request);
            Console.WriteLine($"Server: Deserialized request.");

            return request;
        }

        private void SendResponse(NamedPipeServerStream pipe, ResolveAssemblyReferenceResponseSlim response)
        {
            // Serialize to temporary buffer to reduce IO calls.
            using MemoryStream memoryStream = new();
            ITranslator translator = BinaryTranslator.GetWriteTranslator(memoryStream);
            translator.Translate(ref response);
            Console.WriteLine("Sever: Serialized response.");

            // Delimit message with length.
            using BinaryWriter lengthWriter = new(pipe, Encoding.Default, leaveOpen: true);
            lengthWriter.Write((int)memoryStream.Length);
            Console.WriteLine($"Sever: Sent message length: '{memoryStream.Length}'.");

            // Send the serialized response.
            memoryStream.CopyTo(pipe);
            Console.WriteLine("Sever: Sent full message.");
        }

        private ResolveAssemblyReferenceResponseSlim ReadResponse(NamedPipeClientStream pipe)
        {
            // Read the message length.
            using BinaryReader lengthReader = new(pipe, Encoding.Default, leaveOpen: true);
            int messageLength = lengthReader.ReadInt32();
            Console.WriteLine($"Client: Read message length: '{messageLength}'");

            // Read raw bytes to a temporary buffer to reduce IO calls.
            byte[] buffer = new byte[messageLength];
            _ = pipe.Read(buffer, 0, buffer.Length);
            using MemoryStream memoryStream = new(buffer);
            Console.WriteLine($"Client: Read message in temporary buffer.");

            // Deserialize the request.
            memoryStream.Position = 0;
            ITranslator translator = BinaryTranslator.GetReadTranslator(memoryStream, InterningBinaryReader.PoolingBuffer);
            ResolveAssemblyReferenceResponseSlim response = new();
            translator.Translate(ref response);
            Console.WriteLine($"Client: Deserialized request.");

            return response;
        }
    }
}
