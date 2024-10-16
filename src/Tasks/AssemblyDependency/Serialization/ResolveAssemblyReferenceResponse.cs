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
    internal class ResolveAssemblyReferenceResponse : ITranslatable
    {
        private bool _isComplete;
        private bool _success;
        private string? _dependsOnNetStandard;
        private string? _dependsOnSystemRuntime;
        private int _numCopyLocalFiles;
        private TaskItemSlim[] _copyLocalFiles = [];
        private TaskItemSlim[] _filesWritten = [];
        private TaskItemSlim[] _relatedFiles = [];
        private TaskItemSlim[] _resolvedDependencyFiles = [];
        private TaskItemSlim[] _resolvedFiles = [];
        private TaskItemSlim[] _satelliteFiles = [];
        private TaskItemSlim[] _scatterFiles = [];
        private TaskItemSlim[] _serializationAssemblyFiles = [];
        private TaskItemSlim[] _suggestedRedirects = [];
        private TaskItemSlim[] _unresolvedAssemblyConflicts = [];

        public bool IsComplete { get => _isComplete; set => _isComplete = value; }

        public bool Success { get => _success; set => _success = value; }

        public string? DependsOnNetStandard { get => _dependsOnNetStandard; set => _dependsOnNetStandard = value; }

        public string? DependsOnSystemRuntime { get => _dependsOnSystemRuntime; set => _dependsOnSystemRuntime = value; }

        public int NumCopyLocalFiles { get => _numCopyLocalFiles; set => _numCopyLocalFiles = value; }

        public TaskItemSlim[] CopyLocalFiles { get => _copyLocalFiles; set => _copyLocalFiles = value; }

        public TaskItemSlim[] FilesWritten { get => _filesWritten; set => _filesWritten = value; }

        public TaskItemSlim[] RelatedFiles { get => _relatedFiles; set => _relatedFiles = value; }

        public TaskItemSlim[] ResolvedDependencyFiles { get => _resolvedDependencyFiles; set => _resolvedDependencyFiles = value; }

        public TaskItemSlim[] ResolvedFiles { get => _resolvedFiles; set => _resolvedFiles = value; }

        public TaskItemSlim[] SatelliteFiles { get => _satelliteFiles; set => _satelliteFiles = value; }

        public TaskItemSlim[] ScatterFiles { get => _scatterFiles; set => _scatterFiles = value; }

        public TaskItemSlim[] SerializationAssemblyFiles { get => _serializationAssemblyFiles; set => _serializationAssemblyFiles = value; }

        public TaskItemSlim[] SuggestedRedirects { get => _suggestedRedirects; set => _suggestedRedirects = value; }

        public TaskItemSlim[] UnresolvedAssemblyConflicts { get => _unresolvedAssemblyConflicts; set => _unresolvedAssemblyConflicts = value; }

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

        /*
        internal void WriteToStream(BinaryWriter writer)
        {
            writer.Write(IsComplete);
            writer.Write(Success);
            writer.WriteOptionalString(DependsOnNetStandard);
            writer.WriteOptionalString(DependsOnSystemRuntime);

            // CopyLocalFiles consists of references to other outputs. We save on message size by writing markers to
            // reconstruct the reference list on deserialization.
            HashSet<ITaskItem> copyLocalFiles = new(CopyLocalFiles);
            writer.Write(CopyLocalFiles.Length);

            WriteArrayToStream(writer, copyLocalFiles, FilesWritten);
            WriteArrayToStream(writer, copyLocalFiles, RelatedFiles);
            WriteArrayToStream(writer, copyLocalFiles, ResolvedDependencyFiles);
            WriteArrayToStream(writer, copyLocalFiles, ResolvedFiles);
            WriteArrayToStream(writer, copyLocalFiles, SatelliteFiles);
            WriteArrayToStream(writer, copyLocalFiles, ScatterFiles);
            WriteArrayToStream(writer, copyLocalFiles, SerializationAssemblyFiles);
            WriteArrayToStream(writer, copyLocalFiles, SuggestedRedirects);
            WriteArrayToStream(writer, copyLocalFiles, UnresolvedAssemblyConflicts);

            static void WriteArrayToStream(BinaryWriter writer, HashSet<ITaskItem> copyLocalFiles, ITaskItem[] array)
            {
                if (array.Length == 0)
                {
                    writer.Write((byte)0);
                    return;
                }

                writer.Write((byte)1);
                writer.Write(array.Length);

                foreach (ITaskItem taskItem in array)
                {
                    WriteToStream(writer, taskItem);
                    writer.Write(copyLocalFiles.Contains(taskItem));
                }
            }

            static void WriteToStream(BinaryWriter writer, ITaskItem taskItem)
            {
                KeyValuePair<string, string>[] metadata = taskItem.EnumerateMetadata().ToArray();

                if (metadata.Length == 0)
                {
                    writer.Write((byte)0);
                    return;
                }

                writer.Write((byte)1);
                writer.Write(metadata.Length);

                foreach (KeyValuePair<string, string> kvp in metadata)
                {
                    writer.Write(kvp.Key);
                    writer.Write(kvp.Value);
                }
            }
        }

        internal void CreateFromStream(BinaryReader reader)
        {
            IsComplete = reader.ReadBoolean();
            Success = reader.ReadBoolean();

            int copyLocalFileCount = reader.ReadInt32();
            List<ITaskItem> copyLocalFiles = new(copyLocalFileCount);

            DependsOnNetStandard = reader.ReadOptionalString();
            DependsOnSystemRuntime = reader.ReadOptionalString();
            FilesWritten = CreateArrayFromStream(reader, copyLocalFiles);
            RelatedFiles = CreateArrayFromStream(reader, copyLocalFiles);
            ResolvedDependencyFiles = CreateArrayFromStream(reader, copyLocalFiles);
            ResolvedFiles = CreateArrayFromStream(reader, copyLocalFiles);
            SatelliteFiles = CreateArrayFromStream(reader, copyLocalFiles);
            ScatterFiles = CreateArrayFromStream(reader, copyLocalFiles);
            SerializationAssemblyFiles = CreateArrayFromStream(reader, copyLocalFiles);
            SuggestedRedirects = CreateArrayFromStream(reader, copyLocalFiles);
            UnresolvedAssemblyConflicts = CreateArrayFromStream(reader, copyLocalFiles);
            CopyLocalFiles = [.. copyLocalFiles];

            static ITaskItem[] CreateArrayFromStream(BinaryReader reader, List<ITaskItem> copyLocalFiles)
            {
                if (reader.ReadByte() == 0)
                {
                    return [];
                }

                int length = reader.ReadInt32();
                var array = new ITaskItem[length];

                for (int i = 0; i < length; i++)
                {
                    TaskItem taskItem = new();
                    taskItem.CreateFromStream(reader);
                    array[i] = taskItem;

                    bool isCopyLocal = reader.ReadBoolean();

                    if (isCopyLocal)
                    {
                        copyLocalFiles.Add(taskItem);
                    }
                }

                return array;
            }
        }

        internal string ToByteString()
        {
            MemoryStream stream = new();
            using BinaryWriter writer = new(stream, Encoding.Default, leaveOpen: true);
            WriteToStream(writer);
            stream.Position = 0;
            using StreamReader reader = new(stream);

            return reader.ReadToEnd();
        }
        */

    }
}