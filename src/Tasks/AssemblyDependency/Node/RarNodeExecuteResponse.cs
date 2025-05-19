// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal sealed class RarNodeExecuteResponse : INodePacket
    {
        private Dictionary<string, TaskParameter> _taskOutputs = [];

        private bool _success;

        private int _numCopyLocalFiles;

        internal RarNodeExecuteResponse(ResolveAssemblyReference rarTask, bool success)
        {
            _taskOutputs = RarTaskParameters.GetTaskOutputs(rarTask);
            _success = success;
            _numCopyLocalFiles = rarTask.CopyLocalFiles.Length;
        }

        private RarNodeExecuteResponse()
        {
        }

        public NodePacketType Type => NodePacketType.RarNodeExecuteResponse;

        public Dictionary<string, TaskParameter> TaskOutputs => _taskOutputs;

        public bool Success => _success;

        internal void SetTask(ResolveAssemblyReference rarTask)
        {
            RarTaskParameters.SetTaskOutputs(rarTask, _taskOutputs);

            if (_numCopyLocalFiles == 0)
            {
                return;
            }

            // CopyLocalFiles consists of a list of references that are lost during serialization.
            ITaskItem[] copyLocalFiles = new ITaskItem[_numCopyLocalFiles];
            int i = 0;

            foreach (TaskParameter taskOutput in _taskOutputs.Values)
            {
                if (taskOutput.ParameterType == TaskParameterType.ITaskItemArray)
                {
                    ITaskItem[] taskItems = (ITaskItem[])taskOutput.WrappedParameter;

                    foreach (ITaskItem taskItem in taskItems)
                    {
                        if (taskItem.GetMetadata(ItemMetadataNames.copyLocal).Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            copyLocalFiles[i] = taskItem;
                            i++;
                        }
                    }
                }
            }

            rarTask.CopyLocalFiles = copyLocalFiles;
        }

        public void Translate(ITranslator translator)
        {
            translator.WithInterning(StringComparer.OrdinalIgnoreCase, 100, translator =>
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
