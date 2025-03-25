// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class RarNodeExecuteResponse : INodePacket
    {
        private Dictionary<string, TaskParameter> _taskOutputs = [];

        private bool _success;

        private int _numCopyLocalFiles;

        private RarNodeExecuteResponse()
        {
        }

        internal RarNodeExecuteResponse(ResolveAssemblyReference rarTask, bool success)
        {
            _taskOutputs = RarTaskParameters.GetTaskOutputs(rarTask);
            _success = success;
            _numCopyLocalFiles = rarTask.CopyLocalFiles.Length;
        }

        public NodePacketType Type => NodePacketType.RarNodeExecuteResponse;

        public Dictionary<string, TaskParameter> TaskOutputs => _taskOutputs;

        public bool Success => _success;

        internal void ToTask(ResolveAssemblyReference rarTask)
        {
            RarTaskParameters.SetTaskOutputs(rarTask, _taskOutputs);

            if (_numCopyLocalFiles > 0)
            {
                SetCopyLocalFiles(rarTask);
            }
        }

        private void SetCopyLocalFiles(ResolveAssemblyReference rarTask)
        {
            List<ITaskItem> copyLocalFiles = new(_numCopyLocalFiles);

            foreach (TaskParameter taskOutput in _taskOutputs.Values)
            {
                if (taskOutput.ParameterType != TaskParameterType.ITaskItemArray)
                {
                    continue;
                }

                foreach (ITaskItem taskItem in (ITaskItem[])taskOutput.WrappedParameter)
                {
                    if (taskItem.GetMetadata(ItemMetadataNames.copyLocal).Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        copyLocalFiles.Add(taskItem);
                    }
                }
            }

            rarTask.CopyLocalFiles = [.. copyLocalFiles];
        }

        public void Translate(ITranslator translator)
        {
            int count = _taskOutputs.Count;
            translator.Translate(ref count);
            translator.WithInterning(StringComparer.Ordinal, initialCapacity: count, translator =>
            {
                translator.TranslateDictionary(ref _taskOutputs, StringComparer.Ordinal, TaskParameter.FactoryForDeserialization);
            });
            translator.Translate(ref _success);
            translator.Translate(ref _numCopyLocalFiles);
        }

        internal static INodePacket FactoryForDeserialization(ITranslator translator)
        {
            RarNodeExecuteResponse response = new();
            response.Translate(translator);
            return response;
        }
    }
}
