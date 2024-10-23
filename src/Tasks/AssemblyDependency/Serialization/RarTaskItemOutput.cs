// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    // Minimal shim for Utilities.TaskItem, which is used for RAR outputs.
    // Code paths not hit by RAR are unimplmeneted and will throw an exception.
    // This allows us to emit a smaller serialization payload and optimize hot paths.
    internal class RarTaskItemOutput : ITaskItem2, ITranslatable
    {
        private bool _isCopyLocalFile;

        private Dictionary<string, string> _metadata;

        private string _evaluatedIncludeEscaped;

        public RarTaskItemOutput()
        {
            _metadata = [];
            _evaluatedIncludeEscaped = string.Empty;
        }

        public RarTaskItemOutput(ITaskItem taskItem, bool isCopyLocalFile)
        {
            // This should only be called with a Utilities.TaskItem.
            if (taskItem is not ITaskItem2 taskItem2)
            {
                throw new ArgumentException("Type does not implement 'ITaskItem2'.", nameof(taskItem));
            }

            _isCopyLocalFile = isCopyLocalFile;
            _evaluatedIncludeEscaped = taskItem2.EvaluatedIncludeEscaped;
            _metadata = new Dictionary<string, string>(taskItem2.MetadataCount);

            foreach (DictionaryEntry metadataNameWithValue in taskItem2.CloneCustomMetadataEscaped())
            {
                _metadata[(string)metadataNameWithValue.Key!] = (string)metadataNameWithValue.Value!;
            }
        }

        public string ItemSpec
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public ICollection MetadataNames { get => throw new NotImplementedException(); }

        public Dictionary<string, string> Metadata => throw new NotImplementedException();

        public int MetadataCount => throw new NotImplementedException();

        public string EvaluatedIncludeEscaped
        {
            get => _evaluatedIncludeEscaped;
            set => throw new NotImplementedException();
        }

        public bool IsCopyLocalFile => _isCopyLocalFile;

        public string GetMetadata(string metadataName) => throw new NotImplementedException();

        public void SetMetadata(string metadataName, string metadataValue) => throw new NotImplementedException();

        public void RemoveMetadata(string metadataName) => throw new NotImplementedException();

        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            // This should only be called with a Utilities.TaskItem.
            if (destinationItem is not IMetadataContainer metadataContainer)
            {
                throw new ArgumentException("Type does not implement 'IMetadataContainer'.", nameof(destinationItem));
            }

            // TODO: Figure out better perf as this still starts from an empty CopyOnWriteDictionary.
            metadataContainer.ImportMetadata(_metadata);
        }

        public IDictionary CloneCustomMetadata() => throw new NotImplementedException();

        public string GetMetadataValueEscaped(string metadataName)
        {
            if (!metadataName.Equals(FileUtilities.ItemSpecModifiers.DefiningProjectFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotImplementedException();
            }

            // TaskItems created by RAR do not have a defining project.
            return string.Empty;
        }

        public void SetMetadataValueLiteral(string metadataName, string metadataValue) => throw new NotImplementedException();

        public IDictionary CloneCustomMetadataEscaped() => throw new NotImplementedException();

        public void Translate(ITranslator translator)
        {
            translator.Translate(ref _isCopyLocalFile);
            translator.Translate(ref _evaluatedIncludeEscaped);
            translator.TranslateDictionary(ref _metadata, MSBuildNameIgnoreCaseComparer.Default);
        }
    }
}
