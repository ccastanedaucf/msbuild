// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.BackEnd;

namespace Microsoft.Build.BackEnd
{
    internal sealed class InterningWriteTranslator : ITranslatable
    {
        private List<string> _strings = [];

        private Dictionary<string, int> _stringToIds = [];

        private Dictionary<string, PathIds> _stringToPathIds = [];

        private MemoryStream _packetStream = new();

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        // Recursive loop
        internal ITranslator Translator { get; private set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        internal void InitCapacity(IEqualityComparer<string> comparer, int count)
        {
            if (Translator == null)
            {
                Translator = BinaryTranslator.GetWriteTranslator(_packetStream, this);
            }

            int capacity = count * 8;
            int bufferCapacity = capacity * 128;
            _stringToIds = new Dictionary<string, int>(count * 8, comparer);
            _stringToPathIds = new Dictionary<string, PathIds>(count * 8, comparer);
            _strings.Clear();
            _strings.Capacity = capacity;
            _packetStream.Position = 0;
            _packetStream.SetLength(0);
            _packetStream.Capacity = bufferCapacity;
        }

        internal void Intern(string str) => InternString(str);

        internal void InternNullable(string str)
        {
            if (!Translator.TranslateNullable(str))
            {
                return;
            }

            InternString(str);
        }

        private int InternString(string str)
        {
            if (!_stringToIds.TryGetValue(str, out int index))
            {
                index = _strings.Count;
                _stringToIds.Add(str, index);
                _strings.Add(str);
            }

            Translator.Translate(ref index);
            return index;
        }

        internal void InternNullablePath(string str)
        {
            if (!Translator.TranslateNullable(str))
            {
                return;
            }

            InternPath(str);
        }

        internal void InternPath(string str)
        {
            // const string FilePrefix = @"file:///";
            // if (str.StartsWith(FilePrefix, StringComparison.Ordinal))
            // {
            //     _ = Translator.TranslateNullable(string.Empty);
            //     str = str.Substring(FilePrefix.Length);
            // }
            // else
            // {
            //     string? dummy = null;
            //     _ = Translator.TranslateNullable(dummy);
            // }

            // const string DllSuffix = ".dll";
            // if (str.EndsWith(DllSuffix, StringComparison.Ordinal))
            // {
            //     _ = Translator.TranslateNullable(string.Empty);
            //     str = str.Substring(0, str.Length - DllSuffix.Length);
            // }
            // else
            // {
            //     string? dummy = null;
            //     _ = Translator.TranslateNullable(dummy);
            // }

            if (_stringToPathIds.TryGetValue(str, out PathIds pathIds))
            {
                _ = Translator.TranslateNullable(string.Empty);
                int directoryId = pathIds.DirectoryId;
                int fileNameId = pathIds.FileNameId;
                Translator.Translate(ref directoryId);
                Translator.Translate(ref fileNameId);
                return;
            }

            int splitId = str.LastIndexOf(Path.DirectorySeparatorChar);

            if (splitId == -1)
            {
                splitId = str.LastIndexOf(Path.AltDirectorySeparatorChar);
            }

            bool isPathLike = splitId > -1
                && splitId < str.Length - 1
                && str.IndexOf('%') == -1
                && !PathIsInvalid(str)
                && Path.IsPathRooted(str);

            if (!isPathLike)
            {
                string? dummy = null;
                _ = Translator.TranslateNullable(dummy);
                _ = InternString(str);
                return;
            }

            // If we've seen a string already and know it's pathlike, we just need the index duo
            string directory = str.Substring(0, splitId + 1);
            string fileName = str.Substring(splitId + 1);

            _ = Translator.TranslateNullable(string.Empty);
            int directoryIndex = InternString(directory);
            int fileNameIndex = InternString(fileName);

            _stringToPathIds.Add(str, new PathIds(directoryIndex, fileNameIndex));
        }

        private static bool PathIsInvalid(string path)
        {
            if (path.IndexOfAny(InvalidPathChars) >= 0)
            {
                return true;
            }

            // Path.GetFileName does not react well to malformed filenames.
            // For example, Path.GetFileName("a/b/foo:bar") returns bar instead of foo:bar
            // It also throws exceptions on illegal path characters
            var lastDirectorySeparator = path.LastIndexOfAny(Slashes);

            return path.IndexOfAny(InvalidFileNameChars, lastDirectorySeparator >= 0 ? lastDirectorySeparator + 1 : 0) >= 0;
        }


        /// <summary>
        /// Copied from https://github.com/dotnet/corefx/blob/056715ff70e14712419d82d51c8c50c54b9ea795/src/Common/src/System/IO/PathInternal.Windows.cs#L61
        /// MSBuild should support the union of invalid path chars across the supported OSes, so builds can have the same behaviour crossplatform: https://github.com/dotnet/msbuild/issues/781#issuecomment-243942514
        /// </summary>
        private static readonly char[] InvalidPathChars =
        [
            '|', '\0',
            (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
            (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
            (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
            (char)31
        ];

        /// <summary>
        /// Copied from https://github.com/dotnet/corefx/blob/387cf98c410bdca8fd195b28cbe53af578698f94/src/System.Runtime.Extensions/src/System/IO/Path.Windows.cs#L18
        /// MSBuild should support the union of invalid path chars across the supported OSes, so builds can have the same behaviour crossplatform: https://github.com/dotnet/msbuild/issues/781#issuecomment-243942514
        /// </summary>
        private static readonly char[] InvalidFileNameChars =
        [
            '\"', '<', '>', '|', '\0',
            (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
            (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
            (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
            (char)31, ':', '*', '?', '\\', '/'
        ];

        private static readonly char[] Slashes = { '/', '\\' };

        public void Translate(ITranslator translator)
        {
            translator.Translate(ref _strings);
            byte[] buffer = _packetStream.GetBuffer();
            int bufferSize = (int)_packetStream.Length;
            translator.Writer.Write(buffer, 0, bufferSize);
        }
        private readonly record struct PathIds(int DirectoryId, int FileNameId);
    }
}
