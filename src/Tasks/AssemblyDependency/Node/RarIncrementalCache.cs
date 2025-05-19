// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class RarIncrementalCache
    {
        private readonly ConcurrentDictionary<CachedRequest, CachedResponse> _requestCache = new(CachedRequestEqualityComparer.Instance);

        public void Add(byte[] request, byte[] response, SystemState systemState)
        {
            int i = 0;
            FileState[] files = new FileState[systemState.instanceLocalFileStateCache.Count];
            foreach (KeyValuePair<string, SystemState.FileState> kvp in systemState.instanceLocalFileStateCache)
            {
                // Avoid caching files considered to be immutable.
                if (!ImmutableFilesTimestampCache.Shared.TryGetValue(kvp.Key, out DateTime lastModified) || lastModified == DateTime.MinValue)
                {
                    files[i] = new FileState(path: kvp.Key, lastWriteTimeUtc: kvp.Value.LastModified);
                    i++;
                }
            }

            i = 0;
            DirectoryState[] directories = new DirectoryState[systemState.instanceLocalDirectoryExists.Count];
            foreach (KeyValuePair<string, bool> kvp in systemState.instanceLocalDirectoryExists)
            {
                directories[i] = new DirectoryState(path: kvp.Key, exists: kvp.Value);
                i++;
            }

            i = 0;
            EnumerationState[] enumerations = new EnumerationState[systemState.instanceLocalDirectoryExists.Count];
            foreach (string directory in systemState.instanceLocalDirectories.Keys)
            {
                // Enumerations are the most expensive to check up-to-date status. Since RAR only enumerates to
                // check for directories matching culture names, we can just invalidate if the parent directory's
                // modified time has changed.
                _ = NativeMethodsShared.GetLastWriteDirectoryUtcTime(directory, out DateTime lastModifiedTime);
                enumerations[i] = new EnumerationState(path: directory, lastModifiedTime);
                i++;
            }

            _requestCache[new CachedRequest(request)] = new CachedResponse(response, files, directories, enumerations);
        }

        public bool TryGetValue(ReadOnlyMemory<byte> request, [NotNullWhen(true)] out byte[]? response)
        {
            response = null;
            if (!_requestCache.TryGetValue(new CachedRequest(request), out CachedResponse cachedResponse))
            {
                return false;
            }

            foreach (FileState file in cachedResponse.Files)
            {
                if (NativeMethodsShared.GetLastWriteFileUtcTime(file.Path) != file.LastWriteTimeUtc)
                {
                    return false;
                }
            }

            foreach (DirectoryState directory in cachedResponse.Directories)
            {
                if (FileUtilities.DirectoryExistsNoThrow(directory.Path) != directory.Exists)
                {
                    return false;
                }
            }

            foreach (EnumerationState enumeration in cachedResponse.Enumerations)
            {
                _ = NativeMethodsShared.GetLastWriteDirectoryUtcTime(enumeration.Path, out DateTime lastModifiedTime);
                if (lastModifiedTime != enumeration.LastModifiedTimeUtc)
                {
                    return false;
                }
            }

            response = cachedResponse.Payload;
            return true;
        }

        private readonly struct CachedRequest(ReadOnlyMemory<byte> payload)
        {
            public readonly ReadOnlyMemory<byte> Payload = payload;
        }

        private readonly struct CachedResponse(byte[] payload, FileState[] files, DirectoryState[] directories, EnumerationState[] enumerations)
        {
            public readonly byte[] Payload = payload;
            public readonly FileState[] Files = files;
            public readonly DirectoryState[] Directories = directories;
            public readonly EnumerationState[] Enumerations = enumerations;
        }

        private readonly struct FileState(string path, DateTime lastWriteTimeUtc)
        {
            public readonly string Path = path;
            public readonly DateTime LastWriteTimeUtc = lastWriteTimeUtc;
        }

        private readonly struct DirectoryState(string path, bool exists)
        {
            public readonly string Path = path;
            public readonly bool Exists = exists;
        }

        private readonly struct EnumerationState(string path, DateTime lastModifiedTime)
        {
            public readonly string Path = path;
            public readonly DateTime LastModifiedTimeUtc = lastModifiedTime;
        }

        private sealed class CachedRequestEqualityComparer : IEqualityComparer<CachedRequest>
        {
            public static readonly CachedRequestEqualityComparer Instance = new();

            public bool Equals(CachedRequest x, CachedRequest y)
            {
                if (x.Payload.Length != y.Payload.Length)
                {
                    return false;
                }

                if (!Vector.IsHardwareAccelerated)
                {
                    return x.Payload.Span.SequenceEqual(y.Payload.Span);
                }

                // SIMD accelerated comparison.
                ReadOnlySpan<byte> left = x.Payload.Span;
                ReadOnlySpan<byte> right = y.Payload.Span;
                ReadOnlySpan<Vector<byte>> leftVecArray = MemoryMarshal.Cast<byte, Vector<byte>>(left);
                ReadOnlySpan<Vector<byte>> rightVecArray = MemoryMarshal.Cast<byte, Vector<byte>>(right);

                int length = x.Payload.Length;
                int numVectors = length / Vector<byte>.Count;
                for (int i = 0; i < numVectors; i++)
                {
                    if (!leftVecArray[i].Equals(rightVecArray[i]))
                    {
                        return false;
                    }
                }

                int ceiling = numVectors * Vector<byte>.Count;
                for (int i = ceiling; i < length; i++)
                {
                    if (left[i] != right[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            // TODO: Seed with another value unique to the project to avoid collisions, which will result in mulitple
            // comparisons. This will likely require an additional packet to avoid deserializing the payload.
            public int GetHashCode(CachedRequest obj) => obj.Payload.Length;
        }
    }
}
