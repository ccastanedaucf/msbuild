// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Internal;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    /// <summary>
    /// Implements a single instance of a pipe server which executes the ResolveAssemblyReference task.
    /// </summary>
    internal sealed class OutOfProcRarNodeEndpoint : IDisposable
    {
        private const int MaxBuildEventsBeforeFlush = 100;

        private readonly int _endpointId;

        private readonly NodePipeServer _pipeServer;

        private readonly ConcurrentDictionary<string, byte> _seenStateFiles;

        private readonly RarIncrementalCache _incrementalCache;

        private readonly Queue<RarNodeBuildEventArgs> _buildEventQueue = new(MaxBuildEventsBeforeFlush);

        internal OutOfProcRarNodeEndpoint(
                int endpointId,
                NodePipeServer pipeServer,
                NodePacketFactory packetFactory,
                ConcurrentDictionary<string, byte> seenStateFiles,
                RarIncrementalCache incrementalCache)
        {
            _endpointId = endpointId;
            _pipeServer = pipeServer;
            _pipeServer.RegisterPacketFactory(packetFactory);
            _seenStateFiles = seenStateFiles;
            _incrementalCache = incrementalCache;
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
                    await _pipeServer.StageReadAsync(cancellationToken);
                    NodePacketType packetType = _pipeServer.DeserializePacketType();

                    switch (packetType)
                    {
                        case NodePacketType.RarNodeExecuteRequest:
                            await ExecuteRequest(cancellationToken);
                            break;
                        case NodePacketType.RarNodeConnectionSetup:
                            RarNodeConnectionSetup setup = (RarNodeConnectionSetup)_pipeServer.DeserializePacket();

                            foreach (string immutableDirectory in setup.ImmutableDirectories)
                            {
                                // Set as custom logic locations as we don't verify the directories ahead of time.
                                FileClassifier.Shared.RegisterImmutableDirectory(immutableDirectory, isCustomLogicLocation: true);
                            }

                            break;
                        case NodePacketType.NodeShutdown:
                            // Although the client has already disconnected, it is still necessary to Diconnect() so the
                            // pipe can transition into PipeState.Disonnected, which is treated as an intentional pipe break.
                            // Otherwise, all future operations on the pipe will throw an exception.
                            CommunicationsUtilities.Trace("({0}) RAR client disconnected.", _endpointId);
                            _pipeServer.Disconnect();
                            break;
                        default:
                            ErrorUtilities.ThrowInternalError($"Received unexpected packet type {packetType}");
                            break;
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    CommunicationsUtilities.Trace("({0}) Exception while executing RAR request: {1}", _endpointId, e);
                }
            }

            _pipeServer.Disconnect();
        }

        private async Task ExecuteRequest(CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> requestBuffer = _pipeServer.GetReadBuffer();

            if (_incrementalCache.TryGetValue(requestBuffer, out byte[]? cachedResponse))
            {
                await _pipeServer.WritePacketAsync(cachedResponse, cancellationToken);
                CommunicationsUtilities.Trace("({0}) Completed RAR request from cache. Skipping execution.", _endpointId);
                return;
            }

            CommunicationsUtilities.Trace("({0}) Executing RAR...", _endpointId);

            RarNodeExecuteRequest request = (RarNodeExecuteRequest)_pipeServer.DeserializePacket();

            // TODO: Should be able to reuse the Channel across requests.
            RarNodeBuildEngine buildEngine = new(request.MinimumMessageImportance, request.IsTaskInputLoggingEnabled);
            ResolveAssemblyReference rarTask = new() { BuildEngine = buildEngine };
            request.SetTask(rarTask);

            // Only load the state file on the first run.
            if (rarTask.StateFile != null && !_seenStateFiles.TryAdd(rarTask.StateFile, 0))
            {
                rarTask.StateFile = null;
            }

            // Send log events asynchronously to avoid sending back a large response packet.
            Task buildEventTask = Task.Run(
                () => ProcessLogEvents(buildEngine, cancellationToken),
                cancellationToken);

            bool success = rarTask.Execute();
            RarNodeExecuteResponse response = new(rarTask, success);

            // Ensure we've flushed out any remaining build events.
            // Ideally we'd send them with the packet, but that slightly complicates response caching.
            buildEngine.Complete();
            await buildEventTask;
            await FlushBuildEventsAsync(cancellationToken);

            await _pipeServer.WritePacketAsync(response, cancellationToken);

            CommunicationsUtilities.Trace("({0}) Completed RAR request.", _endpointId);

            ReadOnlyMemory<byte> responseBuffer = _pipeServer.GetWriteBuffer();
            _incrementalCache.Add(requestBuffer.ToArray(), responseBuffer.ToArray(), rarTask._cache);
        }

        private async Task ProcessLogEvents(RarNodeBuildEngine buildEngine, CancellationToken cancellationToken)
        {
            try
            {
                while (cancellationToken.IsCancellationRequested)
                {
                    RarNodeBuildEventArgs buildEventArgs = await buildEngine.EventQueue.ReadAsync(cancellationToken);
                    _buildEventQueue.Enqueue(buildEventArgs);

                    if (_buildEventQueue.Count == MaxBuildEventsBeforeFlush)
                    {
                        CommunicationsUtilities.Trace($"({_endpointId}) Flushing build events.");
                        await FlushBuildEventsAsync(cancellationToken);
                    }
                }
            }
            catch (ChannelClosedException)
            {
                // This is expected when we shut down the channel.
            }
        }

        private async Task FlushBuildEventsAsync(CancellationToken cancellationToken)
        {
            RarNodeLogEvents logEvents = new([.. _buildEventQueue]);
            await _pipeServer.WritePacketAsync(logEvents, cancellationToken);
            _buildEventQueue.Clear();
        }
    }
}
