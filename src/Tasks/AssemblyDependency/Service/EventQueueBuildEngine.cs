// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    /// <summary>
    /// Minimal build engine implementation to collect logging events.
    /// </summary>
    internal class EventQueueBuildEngine : IBuildEngine10
    {
        private class EngineServicesImpl(MessageImportance minimumImportance, bool isTaskInputLoggingEnabled) : EngineServices
        {
            public override bool IsTaskInputLoggingEnabled => isTaskInputLoggingEnabled;

            public override bool LogsMessagesOfImportance(MessageImportance importance) => importance <= minimumImportance;
        }

        internal EventQueueBuildEngine(MessageImportance minimumMessageImportance, bool isTaskInputLoggingEnabled)
        {
            EngineServices = new EngineServicesImpl(minimumMessageImportance, isTaskInputLoggingEnabled);

            UnboundedChannelOptions channelOptions = new()
            {
                SingleWriter = true,
                SingleReader = true,
            };
            _channel = Channel.CreateUnbounded<ResolveAssemblyReferenceBuildEventArgs>(channelOptions);
        }

        public bool IsRunningMultipleNodes => throw new NotImplementedException();

        public bool ContinueOnError => throw new NotImplementedException();

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public bool AllowFailureWithoutError { get; set; }

        public EngineServices EngineServices { get; }

        internal ChannelReader<ResolveAssemblyReferenceBuildEventArgs> EventQueue => _channel.Reader;

        private readonly int _batchCount;

        private readonly Channel<ResolveAssemblyReferenceBuildEventArgs> _channel;

        private readonly Queue<ResolveAssemblyReferenceBuildEventArgs> _buildEventQueue = new();

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs,
            string toolsVersion) => throw new NotImplementedException();

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) => throw new NotImplementedException();

        public BuildEngineResult BuildProjectFilesInParallel(
            string[] projectFileNames,
            string[] targetNames,
            IDictionary[] globalProperties,
            IList<string>[] removeGlobalProperties,
            string[] toolsVersion,
            bool returnTargetOutputs) => throw new NotImplementedException();

        public bool BuildProjectFilesInParallel(
            string[] projectFileNames,
            string[] targetNames,
            IDictionary[] globalProperties,
            IDictionary[] targetOutputsPerProject,
            string[] toolsVersion,
            bool useResultsCache,
            bool unloadProjectsOnCompletion) => throw new NotImplementedException();

        public object GetRegisteredTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => throw new NotImplementedException();

        public void LogCustomEvent(CustomBuildEventArgs e) => throw new NotImplementedException();

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            ResolveAssemblyReferenceBuildEventArgs buildEventArgs = new()
            {
                BuildEventArgsType = BuildEventArgsType.Error,
                Subcategory = e.Subcategory,
                Code = e.Code,
                File = e.File,
                LineNumber = e.LineNumber,
                ColumnNumber = e.ColumnNumber,
                EndLineNumber = e.EndLineNumber,
                EndColumnNumber = e.EndColumnNumber,
                Message = e.RawMessage,
                HelpKeyword = e.HelpKeyword,
                SenderName = e.SenderName,
                EventTimestamp = e.RawTimestamp.Ticks,
            };
            AddMessageArguments(buildEventArgs.MessageArgs, e.RawArguments);
            _channel.Writer.TryWrite(buildEventArgs);
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
            ResolveAssemblyReferenceBuildEventArgs buildEventArgs = new()
            {
                BuildEventArgsType = BuildEventArgsType.Message,
                Subcategory = e.Subcategory,
                Code = e.Code,
                File = e.File,
                LineNumber = e.LineNumber,
                ColumnNumber = e.ColumnNumber,
                EndLineNumber = e.EndLineNumber,
                EndColumnNumber = e.EndColumnNumber,
                Message = e.RawMessage,
                HelpKeyword = e.HelpKeyword,
                SenderName = e.SenderName,
                Importance = (int)e.Importance,
                EventTimestamp = e.RawTimestamp.Ticks,
            };
            AddMessageArguments(buildEventArgs.MessageArgs, e.RawArguments);
            _channel.Writer.TryWrite(buildEventArgs);
        }

        public void LogTelemetry(string eventName, IDictionary<string, string> properties) => throw new NotImplementedException();

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            ResolveAssemblyReferenceBuildEventArgs buildEventArgs = new()
            {
                BuildEventArgsType = BuildEventArgsType.Warning,
                Subcategory = e.Subcategory,
                Code = e.Code,
                File = e.File,
                LineNumber = e.LineNumber,
                ColumnNumber = e.ColumnNumber,
                EndLineNumber = e.EndLineNumber,
                EndColumnNumber = e.EndColumnNumber,
                Message = e.RawMessage,
                HelpKeyword = e.HelpKeyword,
                SenderName = e.SenderName,
                EventTimestamp = e.RawTimestamp.Ticks,
            };
            AddMessageArguments(buildEventArgs.MessageArgs, e.RawArguments);
            _channel.Writer.TryWrite(buildEventArgs);
        }

        private static void AddMessageArguments(RepeatedField<string> messageArgs, object[]? rawArgs)
        {
            if (rawArgs == null)
            {
                return;
            }

            messageArgs.Capacity = rawArgs.Length;

            foreach (object rawArg in rawArgs)
            {
                string parsedArg = Convert.ToString(rawArg, CultureInfo.CurrentCulture) ?? string.Empty;
                messageArgs.Add(parsedArg);
            }
        }

        public void Reacquire() => throw new NotImplementedException();

        public void RegisterTaskObject(object key, object obj, RegisteredTaskObjectLifetime lifetime, bool allowEarlyCollection) =>
            throw new NotImplementedException();

        public object UnregisterTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => throw new NotImplementedException();

        public void Yield() => throw new NotImplementedException();

        public int RequestCores(int requestedCores) => throw new NotImplementedException();

        public void ReleaseCores(int coresToRelease) => throw new NotImplementedException();

        public bool ShouldTreatWarningAsError(string warningCode) => false;

        public IReadOnlyDictionary<string, string> GetGlobalProperties() => throw new NotImplementedException();

        internal void Complete()
        {
            _channel.Writer.Complete();
        }
    }
}
