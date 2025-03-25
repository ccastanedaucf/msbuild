// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Internal;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class RarNodeExecuteRequest : INodePacket
    {
        private Dictionary<string, TaskParameter> _taskInputs = [];

        private MessageImportance _minimumMessageImportance;

        private bool _isTaskInputLoggingEnabled;

        private RarNodeExecuteRequest()
        {
        }

        internal RarNodeExecuteRequest(ResolveAssemblyReference rarTask)
        {
            // RAR service may have a different working directory, so convert potential relative paths to absolute.
            if (rarTask.AppConfigFile != null)
            {
                rarTask.AppConfigFile = Path.GetFullPath(rarTask.AppConfigFile);
            }

            if (rarTask.StateFile != null)
            {
                rarTask.StateFile = Path.GetFullPath(rarTask.StateFile);
            }

            _taskInputs = RarTaskParameters.GetTaskInputs(rarTask);

            // Allow the service to avoid processing messages which would never be logged by the client.
            _minimumMessageImportance = rarTask.Log.LogsMessagesOfImportance(MessageImportance.Low) ? MessageImportance.Low
                    : rarTask.Log.LogsMessagesOfImportance(MessageImportance.Normal) ? MessageImportance.Normal
                    : MessageImportance.High;
        }

        public NodePacketType Type => NodePacketType.RarNodeExecuteRequest;

        internal Dictionary<string, TaskParameter> TaskInputs => _taskInputs;

        internal MessageImportance MinimumMessageImportance => _minimumMessageImportance;

        internal bool IsTaskInputLoggingEnabled => _isTaskInputLoggingEnabled;

        internal void ToTask(ResolveAssemblyReference rarTask) => RarTaskParameters.SetTaskInputs(rarTask, _taskInputs);

        public void Translate(ITranslator translator)
        {
            // translator.TranslateDictionary(ref _taskInputs, StringComparer.Ordinal, translator => TaskParameter.FactoryForDeserialization(translator, interner));
            int count = _taskInputs.Count;
            translator.Translate(ref count);
            translator.WithInterning(StringComparer.Ordinal, initialCapacity: count, translator =>
            {
                translator.TranslateDictionary(ref _taskInputs, StringComparer.Ordinal, TaskParameter.FactoryForDeserialization);
            });
            translator.TranslateEnum(ref _minimumMessageImportance, (int)_minimumMessageImportance);
            translator.Translate(ref _isTaskInputLoggingEnabled);
        }

        internal static INodePacket FactoryForDeserialization(ITranslator translator)
        {
            RarNodeExecuteRequest request = new();
            request.Translate(translator);
            return request;
        }
    }
}
