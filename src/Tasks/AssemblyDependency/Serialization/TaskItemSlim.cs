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

        private string _evaluatedIncludeEscaped;

        private string _evaluatedIncludeUnescaped;

        private string _definingProject;

        private string _fullPath;

        public string ItemSpec
        {
            get => _evaluatedIncludeUnescaped;
            set => throw new NotImplementedException();
        }

        public ICollection MetadataNames { get => throw new NotImplementedException(); }

        public bool IsCopyLocalFile => _isCopyLocalFile;

        public Dictionary<string, string> Metadata => _metadata;

        public int MetadataCount => _metadata.Count;

        public string EvaluatedIncludeEscaped
        {
            // Used by Utilities.TaskItem.ctor
            get => _evaluatedIncludeEscaped;
            set => throw new NotImplementedException();
        }

        public TaskItemSlim()
        {
        }

        public TaskItemSlim(ITaskItem taskItem, bool isCopyLocalFile = false)
        {
            // Store the unescaped value, as this is frequently used by RAR and is immutable.
            _evaluatedIncludeUnescaped = taskItem.ItemSpec;

            if (taskItem is ITaskItem2 taskItem2)
            {
                _evaluatedIncludeEscaped = taskItem2.EvaluatedIncludeEscaped;
                _definingProject = taskItem2.GetMetadataValueEscaped(FileUtilities.ItemSpecModifiers.DefiningProjectFullPath);

                IDictionary metadata = taskItem2.CloneCustomMetadataEscaped();

                if (metadata is Dictionary<string, string> metadataDict)
                {
                    _metadata = metadataDict;
                }
                else
                {
                    _metadata = new(taskItem.MetadataCount, MSBuildNameIgnoreCaseComparer.Default);

                    // Utilities.TaskItem returns CopyOnWriteDictionary
                    foreach (DictionaryEntry metadataNameWithValue in metadata)
                    {
                        _metadata[(string)metadataNameWithValue.Key!] = (string)metadataNameWithValue.Value!;
                    }
                }
            }
            else
            {
                _evaluatedIncludeEscaped = EscapingUtilities.Escape(_evaluatedIncludeUnescaped);
                _definingProject = EscapingUtilities.EscapeWithCaching(taskItem.GetMetadata(FileUtilities.ItemSpecModifiers.DefiningProjectFullPath));
                _metadata = new(taskItem.MetadataCount, MSBuildNameIgnoreCaseComparer.Default);
                taskItem.CopyMetadataTo(this);
            }

            _isCopyLocalFile = isCopyLocalFile;
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

        public string GetMetadataValueEscaped(string metadataName)
        {
            if (FileUtilities.ItemSpecModifiers.IsDerivableItemSpecModifier(metadataName))
            {
                return FileUtilities.ItemSpecModifiers.GetItemSpecModifier(null, _evaluatedIncludeEscaped, _definingProject, metadataName, ref _fullPath)
                    ?? string.Empty;
            }

            return _metadata.TryGetValue(metadataName, out string metadataValue) ? metadataValue : string.Empty;
        }

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
            translator.Translate(ref _evaluatedIncludeUnescaped);
            translator.Translate(ref _evaluatedIncludeEscaped);
            translator.Translate(ref _definingProject);
            translator.Translate(ref _isCopyLocalFile);
            translator.TranslateDictionary(ref _metadata, MSBuildNameIgnoreCaseComparer.Default);
        }
    }
}