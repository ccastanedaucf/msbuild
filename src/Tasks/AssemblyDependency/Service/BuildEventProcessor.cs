using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Google.Protobuf;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class BuildEventProcessor
    {
        private readonly string _workerId;

        private readonly int _maxBuildEvents = 100;

        private readonly Stream _outputStream;

        internal BuildEventProcessor(string workerId, int maxBuildEvents, Stream outputStream)
        {
            _workerId = workerId;
            _outputStream = outputStream;
            _maxBuildEvents = maxBuildEvents;
        }

        public Queue<ResolveAssemblyReferenceBuildEventArgs> BuildEventQueue { get; } = new();

        public async void ProcessBuildEvents(EventQueueBuildEngine buildEngine, CancellationToken cancellationToken)
        {
            try
            {
                while (cancellationToken.IsCancellationRequested)
                {
                    ResolveAssemblyReferenceBuildEventArgs buildEventArgs = await buildEngine.EventQueue.ReadAsync(cancellationToken);
                    BuildEventQueue.Enqueue(buildEventArgs);

                    if (BuildEventQueue.Count == _maxBuildEvents)
                    {
                        Console.WriteLine($"({_workerId}) Flushing build events.");
                        ResolveAssemblyReferencesReply response = new();
                        response.BuildEventArgsQueue.Add(BuildEventQueue);
                        MessageExtensions.WriteDelimitedTo(response, _outputStream);
                        BuildEventQueue.Clear();
                    }
                }
            }
            catch (ChannelClosedException)
            {
            }
        }
    }
}
