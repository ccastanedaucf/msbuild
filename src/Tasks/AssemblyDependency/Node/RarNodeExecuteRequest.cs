// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal sealed class RarNodeExecuteRequest : INodePacket
    {
        private Dictionary<string, TaskParameter> _taskInputs = [];

        private MessageImportance _minimumMessageImportance;

        private bool _isTaskInputLoggingEnabled;

        internal RarNodeExecuteRequest(ResolveAssemblyReference rarTask)
        {
            // RAR service may have a different working directory than the target, so convert potential relative paths to absolute.
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
            _isTaskInputLoggingEnabled = rarTask.Log.IsTaskInputLoggingEnabled;
            _minimumMessageImportance = rarTask.Log.LogsMessagesOfImportance(MessageImportance.Low) ? MessageImportance.Low
                    : rarTask.Log.LogsMessagesOfImportance(MessageImportance.Normal) ? MessageImportance.Normal
                    : MessageImportance.High;
        }

        private RarNodeExecuteRequest()
        {
        }

        public NodePacketType Type => NodePacketType.RarNodeExecuteRequest;

        internal MessageImportance MinimumMessageImportance => _minimumMessageImportance;

        internal bool IsTaskInputLoggingEnabled => _isTaskInputLoggingEnabled;

        internal void SetTask(ResolveAssemblyReference rarTask) => RarTaskParameters.SetTaskInputs(rarTask, _taskInputs);

        public void Translate(ITranslator translator)
        {
            translator.WithInterning(StringComparer.OrdinalIgnoreCase, 100, translator =>
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
