// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.BackEnd;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    /// <summary>
    /// Provides any state specific to the connected client that can be used to pre-compute caches.
    /// </summary>
    internal class RarNodeConnectionSetup : INodePacket
    {
        private string[] _immutableDirectories = [];

        internal RarNodeConnectionSetup(string[] immutableDirectories) => _immutableDirectories = immutableDirectories;

        private RarNodeConnectionSetup()
        {
        }

        internal string[] ImmutableDirectories => _immutableDirectories;

        public NodePacketType Type => NodePacketType.RarNodeConnectionSetup;

        public void Translate(ITranslator translator) => translator.Translate(ref _immutableDirectories);

        internal static INodePacket FactoryForDeserialization(ITranslator translator)
        {
            RarNodeConnectionSetup setup = new();
            setup.Translate(translator);
            return setup;
        }
    }
}
