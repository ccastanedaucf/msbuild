// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class RarExecutionResponse : RarSerializableMessageBase, ITranslatable
    {
        private bool _isComplete;
        private bool _success;
        private string? _dependsOnNetStandard;
        private string? _dependsOnSystemRuntime;
        private int _numCopyLocalFiles;
        private RarTaskItemOutput[] _copyLocalFiles = [];
        private RarTaskItemOutput[] _filesWritten = [];
        private RarTaskItemOutput[] _relatedFiles = [];
        private RarTaskItemOutput[] _resolvedDependencyFiles = [];
        private RarTaskItemOutput[] _resolvedFiles = [];
        private RarTaskItemOutput[] _satelliteFiles = [];
        private RarTaskItemOutput[] _scatterFiles = [];
        private RarTaskItemOutput[] _serializationAssemblyFiles = [];
        private RarTaskItemOutput[] _suggestedRedirects = [];
        private RarTaskItemOutput[] _unresolvedAssemblyConflicts = [];

        public bool IsComplete { get => _isComplete; set => _isComplete = value; }

        public bool Success { get => _success; set => _success = value; }

        public string? DependsOnNetStandard { get => _dependsOnNetStandard; set => _dependsOnNetStandard = value; }

        public string? DependsOnSystemRuntime { get => _dependsOnSystemRuntime; set => _dependsOnSystemRuntime = value; }

        public int NumCopyLocalFiles { get => _numCopyLocalFiles; set => _numCopyLocalFiles = value; }

        public RarTaskItemOutput[] CopyLocalFiles { get => _copyLocalFiles; set => _copyLocalFiles = value; }

        public RarTaskItemOutput[] FilesWritten { get => _filesWritten; set => _filesWritten = value; }

        public RarTaskItemOutput[] RelatedFiles { get => _relatedFiles; set => _relatedFiles = value; }

        public RarTaskItemOutput[] ResolvedDependencyFiles { get => _resolvedDependencyFiles; set => _resolvedDependencyFiles = value; }

        public RarTaskItemOutput[] ResolvedFiles { get => _resolvedFiles; set => _resolvedFiles = value; }

        public RarTaskItemOutput[] SatelliteFiles { get => _satelliteFiles; set => _satelliteFiles = value; }

        public RarTaskItemOutput[] ScatterFiles { get => _scatterFiles; set => _scatterFiles = value; }

        public RarTaskItemOutput[] SerializationAssemblyFiles { get => _serializationAssemblyFiles; set => _serializationAssemblyFiles = value; }

        public RarTaskItemOutput[] SuggestedRedirects { get => _suggestedRedirects; set => _suggestedRedirects = value; }

        public RarTaskItemOutput[] UnresolvedAssemblyConflicts { get => _unresolvedAssemblyConflicts; set => _unresolvedAssemblyConflicts = value; }

        public RarBuildEventArgs[] BuildEventArgsQueue { get; set; } = [];

        public List<string> TrackedDirectories { get; set; } = [];

        public List<string> TrackedFiles { get; set; } = [];

        internal SystemState? Cache { get; set; }

        public void Translate(ITranslator translator)
        {
            translator.Translate(ref _isComplete);
            translator.Translate(ref _success);
            translator.Translate(ref _dependsOnNetStandard);
            translator.Translate(ref _dependsOnSystemRuntime);
            translator.Translate(ref _numCopyLocalFiles);
            translator.TranslateArray(ref _filesWritten);
            translator.TranslateArray(ref _relatedFiles);
            translator.TranslateArray(ref _resolvedDependencyFiles);
            translator.TranslateArray(ref _resolvedFiles);
            translator.TranslateArray(ref _satelliteFiles);
            translator.TranslateArray(ref _scatterFiles);
            translator.TranslateArray(ref _serializationAssemblyFiles);
            translator.TranslateArray(ref _suggestedRedirects);
            translator.TranslateArray(ref _unresolvedAssemblyConflicts);
        }
    }
}
