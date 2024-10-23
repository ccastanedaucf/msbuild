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

#nullable disable

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    // Shim for ProjectItemInstance.TaskItem
    internal class ResolveAssemblyReferenceRequestItem : ITaskItem2, ITranslatable
    {
        private Dictionary<string, string> _metadata;

        private string _evaluatedIncludeEscaped;

        private string _evaluatedIncludeUnescaped;

        public string ItemSpec
        {
            get => _evaluatedIncludeUnescaped;
            set => throw new NotImplementedException();
        }

        public ICollection MetadataNames { get => throw new NotImplementedException(); }

        public Dictionary<string, string> Metadata => _metadata;

        public int MetadataCount => _metadata.Count;

        public string EvaluatedIncludeEscaped
        {
            // Used by Utilities.TaskItem.ctor
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public ResolveAssemblyReferenceRequestItem()
        {
        }

        public ResolveAssemblyReferenceRequestItem(ITaskItem taskItem)
        {
            if (taskItem is not ITaskItem2 taskItem2 || taskItem2.CloneCustomMetadataEscaped() is not Dictionary<string, string> metadata)
            {
                throw new Exception("Unexpected ITaskItem type given to RAR request.");
            }

            // Store the unescaped value, as this is frequently used by RAR and is immutable.
            _evaluatedIncludeUnescaped = taskItem.ItemSpec;
            _evaluatedIncludeEscaped = taskItem2.EvaluatedIncludeEscaped;
            _metadata = metadata;
        }

        public string GetMetadata(string metadataName) =>
            EscapingUtilities.UnescapeAll(GetMetadataValueEscaped(metadataName));

        public void SetMetadata(string metadataName, string metadataValue) => throw new NotImplementedException();

        public void RemoveMetadata(string metadataName) => throw new NotImplementedException();

        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            if (destinationItem is not IMetadataContainer destinationAsTaskItem)
            {
                // TODO: Better message
                throw new Exception("Unexpected ITaskItem type.");
            }

            destinationAsTaskItem.ImportMetadata(_metadata);
        }

        public IDictionary CloneCustomMetadata() => throw new NotImplementedException();

        public string GetMetadataValueEscaped(string metadataName)
        {
            string metadataValue;

            if (!_metadata.TryGetValue(metadataName, out metadataValue)
                && FileUtilities.ItemSpecModifiers.IsItemSpecModifier(metadataName))
            {
                // Current directory is only required full full path evaluation
                // Because we cache the full path ahead of time, it will never be called.
                string dummy = null;
                metadataValue = FileUtilities.ItemSpecModifiers.GetItemSpecModifier(
                    null,
                    _evaluatedIncludeEscaped, // TODO: Is any defining project modifier called?
                    null,
                    metadataName,
                    ref dummy);
            }

            return metadataValue ?? string.Empty;
        }

        public void SetMetadataValueLiteral(string metadataName, string metadataValue) => throw new NotImplementedException();

        public IDictionary CloneCustomMetadataEscaped() => throw new NotImplementedException();

        public void Translate(ITranslator translator)
        {
            translator.Translate(ref _evaluatedIncludeUnescaped);
            translator.Translate(ref _evaluatedIncludeEscaped);
            translator.TranslateDictionary(ref _metadata, MSBuildNameIgnoreCaseComparer.Default);
        }
    }
}