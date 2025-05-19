// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.BackEnd;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    /// <summary>
    /// Represents a queue of log events emitted from the out-of-proc RAR task, for replay by the client.
    /// </summary>
    internal class RarNodeLogEvents : INodePacket
    {
        private RarNodeBuildEventArgs[] _buildEventArgsQueue = [];

        internal RarNodeBuildEventArgs[] BuildEventArgsQueue => _buildEventArgsQueue;

        public NodePacketType Type => NodePacketType.RarNodeLogEvents;

        internal RarNodeLogEvents(RarNodeBuildEventArgs[] buildEventArgsQueue) => _buildEventArgsQueue = buildEventArgsQueue;

        private RarNodeLogEvents()
        {
        }

        public void Translate(ITranslator translator)
            => translator.TranslateArray(ref _buildEventArgsQueue, RarNodeBuildEventArgs.FactoryForDeserialization);

        internal static INodePacket FactoryForDeserialization(ITranslator translator)
        {
            RarNodeLogEvents logEvents = new();
            logEvents.Translate(translator);
            return logEvents;
        }
    }
}
