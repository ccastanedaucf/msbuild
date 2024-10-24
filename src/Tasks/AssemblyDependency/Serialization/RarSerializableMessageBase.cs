// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using System.IO.Hashing;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal abstract class RarSerializableMessageBase
    {
        internal ulong ByteHash { get; private set; }

        internal byte[]? ByteArray { get; private set; }

        internal void SetByteString(byte[] buffer, int sourceIndex, int messageLength)
        {
            ByteArray = new byte[messageLength];
            Array.Copy(buffer, sourceIndex, ByteArray, 0, messageLength);

            // TODO: Properly implement IEquatable
            ByteHash = XxHash64.HashToUInt64(ByteArray);
        }
    }
}
