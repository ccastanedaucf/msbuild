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
    internal class TaskItemSlim : ITaskItem2, ITranslatable
    {
        private Dictionary<string, string> _metadata;

        private bool _isCopyLocalFile;

        private string _itemSpec;

        public string ItemSpec { get => _itemSpec; set => _itemSpec = value; }

        public ICollection MetadataNames { get; } = Array.Empty<string>();

        public bool IsCopyLocalFile => _isCopyLocalFile;

        public Dictionary<string, string> Metadata => _metadata;

        public int MetadataCount => _metadata.Count;

        public string EvaluatedIncludeEscaped
        {
            get => EscapingUtilities.UnescapeAll(ItemSpec);

            set => throw new NotImplementedException();
        }

        public TaskItemSlim()
        {
        }

        public TaskItemSlim(ITaskItem taskItem)
        {
            _itemSpec = taskItem.ItemSpec;

            if (taskItem is ITaskItem2 taskItem2 && taskItem2.CloneCustomMetadataEscaped() is Dictionary<string, string> metadata)
            {
                _metadata = metadata;
            }
            else
            {
                _metadata = new(taskItem.MetadataCount, MSBuildNameIgnoreCaseComparer.Default);
                taskItem.CopyMetadataTo(this);
            }
        }

        public TaskItemSlim(ITaskItem taskItem, bool isCopyLocalFile)
        {
            _isCopyLocalFile = isCopyLocalFile;
            _itemSpec = taskItem.ItemSpec;
            _metadata = new(taskItem.MetadataCount, MSBuildNameIgnoreCaseComparer.Default);

            if (taskItem is ITaskItem2 taskItem2)
            {
                foreach (DictionaryEntry metadataNameWithValue in taskItem2.CloneCustomMetadataEscaped())
                {
                    _metadata[(string)metadataNameWithValue.Key!] = (string)metadataNameWithValue.Value!;
                }
            }
            else
            {
                taskItem.CopyMetadataTo(this);
            }
        }

        public string GetMetadata(string metadataName) =>
            EscapingUtilities.UnescapeAll(GetMetadataValueEscaped(metadataName));

        public void SetMetadata(string metadataName, string metadataValue)
        {
            _metadata[metadataName] = metadataValue;
        }

        public void RemoveMetadata(string metadataName)
        {
            throw new NotImplementedException();
        }

        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            if (destinationItem is IMetadataContainer destinationAsTaskItem)
            {
                destinationAsTaskItem.ImportMetadata(_metadata);
            }
            else
            {
                foreach (KeyValuePair<string, string> metadataNameAndValue in _metadata)
                {
                    destinationItem.SetMetadata(metadataNameAndValue.Key, metadataNameAndValue.Value);
                }
            }
        }

        public IDictionary CloneCustomMetadata()
        {
            throw new NotImplementedException();
        }

        public string GetMetadataValueEscaped(string metadataName) =>
            _metadata.TryGetValue(metadataName, out string metadataValue) ? metadataValue : string.Empty;

        public void SetMetadataValueLiteral(string metadataName, string metadataValue)
        {
            throw new NotImplementedException();
        }

        public IDictionary CloneCustomMetadataEscaped()
        {
            throw new NotImplementedException();
        }

        public void Translate(ITranslator translator)
        {
            translator.Translate(ref _itemSpec);
            translator.Translate(ref _isCopyLocalFile);
            translator.TranslateDictionary(ref _metadata, MSBuildNameIgnoreCaseComparer.Default);
        }
    }
}