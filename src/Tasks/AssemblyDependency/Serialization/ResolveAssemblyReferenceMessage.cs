// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal abstract class ResolveAssemblyReferenceMessage
    {
        internal string? ByteString { get; private set; }

        internal void SetByteString(byte[] buffer, int messageLength)
        {
            ByteString = Encoding.UTF8.GetString(buffer, 0, messageLength);
        }
    }
}
