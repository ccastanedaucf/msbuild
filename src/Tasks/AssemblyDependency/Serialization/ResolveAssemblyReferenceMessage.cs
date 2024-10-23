// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal abstract class ResolveAssemblyReferenceMessage
    {
        internal string? ByteString { get; private set; }

        internal byte[]? ByteHash { get; private set; }

        internal void SetByteString(byte[] buffer, int sourceIndex, int messageLength)
        {
            ByteString = Encoding.UTF8.GetString(buffer, sourceIndex, messageLength);
            ByteHash = new byte[messageLength];
            Array.Copy(buffer, sourceIndex, ByteHash, 0, messageLength);
        }
    }
}
