// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.BackEnd;

namespace Microsoft.Build.BackEnd
{
    internal sealed class InterningReadTranslator : ITranslatable
    {
        private List<string> _strings = [];

        private Dictionary<PathIds, string> _pathIdsToString = [];

        private readonly ITranslator _translator;

        internal InterningReadTranslator(ITranslator translator)
        {
            _translator = translator;
        }

        internal string? ReadNullable()
        {
            if (!_translator.TranslateNullable(string.Empty))
            {
                return null;
            }

            return Read();
        }

        internal string Read()
        {
            int key = -1;
            _translator.Translate(ref key);
            return _strings[key];
        }

        internal string? ReadNullablePath()
        {
            if (!_translator.TranslateNullable(string.Empty))
            {
                return null;
            }

            return ReadPath();
        }

        internal string ReadPath()
        {
            // const string FilePrefix = @"file:///";
            // bool addPrefix = _translator.TranslateNullable(string.Empty);
            // const string DllSuffix = ".dll";
            // bool addSuffix = _translator.TranslateNullable(string.Empty);

            // if (!_translator.TranslateNullable(string.Empty))
            // {
            //     return addSuffix ? Read() + DllSuffix : Read();
            // }

            if (!_translator.TranslateNullable(string.Empty))
            {
                return Read();
            }

            int directoryKey = -1;
            int fileNameKey = -1;
            _translator.Translate(ref directoryKey);
            _translator.Translate(ref fileNameKey);

            PathIds pathIds = new(directoryKey, fileNameKey);

            if (_pathIdsToString.TryGetValue(pathIds, out string? path))
            {
                return path;
            }

            string directory = _strings[pathIds.DirectoryId];
            string fileName = _strings[pathIds.FileNameId];
            string str = string.Concat(directory, fileName);
            _pathIdsToString.Add(pathIds, str);

            // if (addPrefix && addSuffix)
            // {
            //     str = FilePrefix + str + DllSuffix;
            // }
            // else if (addPrefix)
            // {
            //     str = FilePrefix + str;
            // }
            // else if (addSuffix)
            // {
            //     str += DllSuffix;
            // }

            return str;
        }

        public void Translate(ITranslator translator)
        {
            _translator.Translate(ref _strings);

            // Console.WriteLine(_strings.Count);
            // foreach (string str in _strings)
            // {
            //     Console.WriteLine(str);
            // }

#if NET
            _pathIdsToString.EnsureCapacity(_strings.Count);
#else
            _pathIdsToString = new(_strings.Count);
#endif
        }

        private readonly record struct PathIds(int DirectoryId, int FileNameId);
    }
}
