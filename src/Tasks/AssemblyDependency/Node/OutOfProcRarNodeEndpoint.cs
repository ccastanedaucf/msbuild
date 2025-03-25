// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Internal;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    /// <summary>
    /// Implements a single instance of a pipe server which executes the ResolveAssemblyReference task.
    /// </summary>
    internal class OutOfProcRarNodeEndpoint : IDisposable
    {
        private readonly int _endpointId;

        private readonly NodePipeServer _pipeServer;

        private readonly ConcurrentDictionary<string, byte> _seenStateFiles;

        internal OutOfProcRarNodeEndpoint(int endpointId, ServerNodeHandshake handshake, int maxNumberOfServerInstances, ConcurrentDictionary<string, byte> seenStateFiles)
        {
            _endpointId = endpointId;
            _pipeServer = new NodePipeServer(NamedPipeUtil.GetRarNodeEndpointPipeName(handshake), handshake, maxNumberOfServerInstances);

            NodePacketFactory packetFactory = new();
            packetFactory.RegisterPacketHandler(NodePacketType.RarNodeExecuteRequest, RarNodeExecuteRequest.FactoryForDeserialization, null);
            _pipeServer.RegisterPacketFactory(packetFactory);

            _seenStateFiles = seenStateFiles;
        }

        public void Dispose() => _pipeServer.Dispose();

        internal async Task RunAsync(CancellationToken cancellationToken = default)
        {
            CommunicationsUtilities.Trace("({0}) Starting RAR endpoint.", _endpointId);

            try
            {
                await RunInternalAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellation excpetions for now. We're using this as a simple way to gracefully shutdown the
                // endpoint, instead of having to implement separate Start / Stop methods and deferring to the caller.
                // Can reevaluate if we need more granular control over cancellation vs shutdown.
            }
        }

        private async Task RunInternalAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (!_pipeServer.IsConnected)
                {
                    _ = _pipeServer.WaitForConnection();
                }

                CommunicationsUtilities.Trace("({0}) Received RAR request.", _endpointId);

                try
                {
                    INodePacket packet = await _pipeServer.ReadPacketAsync(cancellationToken);

                    if (packet.Type == NodePacketType.NodeShutdown)
                    {
                        // Although the client has already disconnected, it is still necessary to Diconnect() so the
                        // pipe can transition into PipeState.Disonnected, which is treated as an intentional pipe break.
                        // Otherwise, all future operations on the pipe will throw an exception.
                        CommunicationsUtilities.Trace("({0}) RAR client disconnected.", _endpointId);
                        _pipeServer.Disconnect();
                        continue;
                    }

                    RarNodeExecuteResponse response = ExecuteRequest((RarNodeExecuteRequest)packet);
                    await _pipeServer.WritePacketAsync(response, cancellationToken);

                    CommunicationsUtilities.Trace("({0}) Completed RAR request.", _endpointId);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    CommunicationsUtilities.Trace("({0}) Exception while executing RAR request: {1}", _endpointId, e);
                }
            }

            _pipeServer.Disconnect();
        }

        private RarNodeExecuteResponse ExecuteRequest(RarNodeExecuteRequest request)
        {
            RarNodeBuildEngine buildEngine = new(request.MinimumMessageImportance, request.IsTaskInputLoggingEnabled);
            ResolveAssemblyReference rarTask = new() { BuildEngine = buildEngine };
            request.ToTask(rarTask);

            // Only load the state file on the first run.
            if (rarTask.StateFile != null && !_seenStateFiles.TryAdd(rarTask.StateFile, 0))
            {
                rarTask.StateFile = null;
            }

            bool success = rarTask.Execute();

            return new RarNodeExecuteResponse(rarTask, success);
        }
    }
}
