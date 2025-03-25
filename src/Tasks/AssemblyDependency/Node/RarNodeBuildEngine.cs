// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    /// <summary>
    /// Minimal build engine implementation to collect logging events.
    /// </summary>
    internal class RarNodeBuildEngine : IBuildEngine10
    {
        internal RarNodeBuildEngine(MessageImportance minimumMessageImportance, bool isTaskInputLoggingEnabled) =>
            EngineServices = new EngineServicesImpl(minimumMessageImportance, isTaskInputLoggingEnabled);

        public bool IsRunningMultipleNodes => throw new NotImplementedException();

        public bool ContinueOnError => throw new NotImplementedException();

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public bool AllowFailureWithoutError { get; set; }

        public EngineServices EngineServices { get; }

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
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogTelemetry(string eventName, IDictionary<string, string> properties) => throw new NotImplementedException();

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
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
        }

        private class EngineServicesImpl(MessageImportance minimumImportance, bool isTaskInputLoggingEnabled) : EngineServices
        {
            public override bool IsTaskInputLoggingEnabled => isTaskInputLoggingEnabled;

            public override bool IsOutOfProcRarNodeEnabled => false;

            public override bool LogsMessagesOfImportance(MessageImportance importance) => importance <= minimumImportance;
        }
    }
}
