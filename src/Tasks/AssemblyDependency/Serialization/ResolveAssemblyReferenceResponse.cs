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
    internal class ResolveAssemblyReferenceResponse : ResolveAssemblyReferenceMessage, ITranslatable
    {
        private bool _isComplete;
        private bool _success;
        private string? _dependsOnNetStandard;
        private string? _dependsOnSystemRuntime;
        private int _numCopyLocalFiles;
        private ResolveAssemblyReferenceResponseItem[] _copyLocalFiles = [];
        private ResolveAssemblyReferenceResponseItem[] _filesWritten = [];
        private ResolveAssemblyReferenceResponseItem[] _relatedFiles = [];
        private ResolveAssemblyReferenceResponseItem[] _resolvedDependencyFiles = [];
        private ResolveAssemblyReferenceResponseItem[] _resolvedFiles = [];
        private ResolveAssemblyReferenceResponseItem[] _satelliteFiles = [];
        private ResolveAssemblyReferenceResponseItem[] _scatterFiles = [];
        private ResolveAssemblyReferenceResponseItem[] _serializationAssemblyFiles = [];
        private ResolveAssemblyReferenceResponseItem[] _suggestedRedirects = [];
        private ResolveAssemblyReferenceResponseItem[] _unresolvedAssemblyConflicts = [];

        public bool IsComplete { get => _isComplete; set => _isComplete = value; }

        public bool Success { get => _success; set => _success = value; }

        public string? DependsOnNetStandard { get => _dependsOnNetStandard; set => _dependsOnNetStandard = value; }

        public string? DependsOnSystemRuntime { get => _dependsOnSystemRuntime; set => _dependsOnSystemRuntime = value; }

        public int NumCopyLocalFiles { get => _numCopyLocalFiles; set => _numCopyLocalFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] CopyLocalFiles { get => _copyLocalFiles; set => _copyLocalFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] FilesWritten { get => _filesWritten; set => _filesWritten = value; }

        public ResolveAssemblyReferenceResponseItem[] RelatedFiles { get => _relatedFiles; set => _relatedFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] ResolvedDependencyFiles { get => _resolvedDependencyFiles; set => _resolvedDependencyFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] ResolvedFiles { get => _resolvedFiles; set => _resolvedFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] SatelliteFiles { get => _satelliteFiles; set => _satelliteFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] ScatterFiles { get => _scatterFiles; set => _scatterFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] SerializationAssemblyFiles { get => _serializationAssemblyFiles; set => _serializationAssemblyFiles = value; }

        public ResolveAssemblyReferenceResponseItem[] SuggestedRedirects { get => _suggestedRedirects; set => _suggestedRedirects = value; }

        public ResolveAssemblyReferenceResponseItem[] UnresolvedAssemblyConflicts { get => _unresolvedAssemblyConflicts; set => _unresolvedAssemblyConflicts = value; }

        public ResolveAssemblyReferenceBuildEventArgs[] BuildEventArgsQueue { get; set; } = [];

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
