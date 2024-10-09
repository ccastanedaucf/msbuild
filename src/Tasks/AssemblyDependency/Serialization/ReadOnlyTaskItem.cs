using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.Build.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    public partial class ReadOnlyTaskItem : ITaskItem2
    {
        private Lazy<Dictionary<string, string>> _metadata;

        public ICollection MetadataNames { get; } = Array.Empty<string>();

        public int MetadataCount => Metadata.Count;

        public string EvaluatedIncludeEscaped
        {
            get => EscapingUtilities.UnescapeAll(ItemSpec);

            set => throw new NotImplementedException();
        }

        partial void OnConstruction()
        {
            _metadata = new(() =>
            {
                Dictionary<string, string> metadata = new(Metadata.Count, MSBuildNameIgnoreCaseComparer.Default);

                foreach (TaskItemMetadata itemMetadata in Metadata)
                {
                    metadata[itemMetadata.Name] = itemMetadata.Value;
                }

                return metadata;
            });
        }

        public ReadOnlyTaskItem(ITaskItem taskItem)
        {
            ItemSpec = taskItem.ItemSpec;

            if (taskItem is ITaskItem2 taskItem2 && taskItem2.CloneCustomMetadataEscaped() is Dictionary<string, string> metadata)
            {
                _metadata = new(() => metadata);
            }
            else
            {
                _metadata = new(() => new(taskItem.MetadataCount, MSBuildNameIgnoreCaseComparer.Default));
                taskItem.CopyMetadataTo(this);
            }

            foreach (KeyValuePair<string, string> kvp in _metadata.Value)
            {
                Metadata.Add(new TaskItemMetadata { Name = kvp.Key, Value = kvp.Value });
            }
        }

        public ReadOnlyTaskItem(ITaskItem taskItem, bool isCopyLocalFile)
        {
            IsCopyLocalFile = isCopyLocalFile;
            ItemSpec = taskItem.ItemSpec;
            _metadata = new(() => new(taskItem.MetadataCount, MSBuildNameIgnoreCaseComparer.Default));

            // TODO: Perf improvement, copying metadata from Utilities.TaskItem accounts for ~10% of RAR-aas overhead
            // due to slow copying of metadata from the backing CopyOnWriteDictionary.
            if (taskItem is ITaskItem2 taskItem2)
            {
                foreach (DictionaryEntry metadataNameWithValue in taskItem2.CloneCustomMetadataEscaped())
                {
                    _metadata.Value[(string)metadataNameWithValue.Key!] = (string)metadataNameWithValue.Value!;
                }
            }
            else
            {
                taskItem.CopyMetadataTo(this);
            }

            foreach (KeyValuePair<string, string> kvp in _metadata.Value)
            {
                Metadata.Add(new TaskItemMetadata { Name = kvp.Key, Value = kvp.Value });
            }
        }

        public string GetMetadata(string metadataName) =>
            EscapingUtilities.UnescapeAll(GetMetadataValueEscaped(metadataName));

        public void SetMetadata(string metadataName, string metadataValue)
        {
            _metadata.Value[metadataName] = metadataValue;
        }

        public void RemoveMetadata(string metadataName)
        {
            throw new NotImplementedException();
        }

        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            if (destinationItem is IMetadataContainer destinationAsTaskItem)
            {
                destinationAsTaskItem.ImportMetadata(_metadata.Value);
            }
            else
            {
                foreach (KeyValuePair<string, string> metadataNameAndValue in _metadata.Value)
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
            _metadata.Value.TryGetValue(metadataName, out string? metadataValue) ? metadataValue : string.Empty;

        public void SetMetadataValueLiteral(string metadataName, string metadataValue)
        {
            throw new NotImplementedException();
        }

        public IDictionary CloneCustomMetadataEscaped()
        {
            throw new NotImplementedException();
        }
    }
}
