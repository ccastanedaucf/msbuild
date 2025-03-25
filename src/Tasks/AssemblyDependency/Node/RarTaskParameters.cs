// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal class RarTaskParameters
    {
        /// <summary>
        /// Exclude CopyLocalFiles since it is a list of references - otherwise we'll end up with duplicated task item instances.
        /// We reconstruct this on our own using item metadata.
        /// </summary>
        private const string CopyLocalPropertyName = nameof(ResolveAssemblyReference.CopyLocalFiles);

        private static readonly Lazy<PropertyInfo[]> s_outputProperties = new(() =>
            [.. typeof(ResolveAssemblyReference).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.GetCustomAttribute<OutputAttribute>() != null && !property.Name.Equals(CopyLocalPropertyName, StringComparison.Ordinal))]);

        private static readonly Lazy<PropertyInfo[]> s_inputProperties = new(() =>
            [.. typeof(ResolveAssemblyReference).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.GetGetMethod() != null && property.GetSetMethod() != null)]);

        internal static Dictionary<string, TaskParameter> GetTaskInputs(ResolveAssemblyReference rar) =>
            GetTaskParameters(rar, s_inputProperties.Value);

        internal static Dictionary<string, TaskParameter> GetTaskOutputs(ResolveAssemblyReference rar) =>
            GetTaskParameters(rar, s_outputProperties.Value);

        private static Dictionary<string, TaskParameter> GetTaskParameters(ResolveAssemblyReference rar, PropertyInfo[] properties)
        {
            Dictionary<string, TaskParameter> taskParameters = new(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo property in properties)
            {
                TaskParameter parameter = new(property.GetValue(rar, null));
                bool isParameterSet = parameter.ParameterType switch
                {
                    TaskParameterType.Null => false,
                    TaskParameterType.PrimitiveTypeArray or TaskParameterType.ValueTypeArray or TaskParameterType.ITaskItemArray => ((Array)parameter.WrappedParameter).Length != 0,
                    _ => true,
                };

                // Avoid serializing empty parameters.
                if (isParameterSet)
                {
                    taskParameters[property.Name] = parameter;
                }
            }

            return taskParameters;
        }

        internal static void SetTaskInputs(ResolveAssemblyReference task, Dictionary<string, TaskParameter> parameters) =>
            SetTaskParameters(task, parameters, s_inputProperties.Value);

        internal static void SetTaskOutputs(ResolveAssemblyReference task, Dictionary<string, TaskParameter> parameters) =>
            SetTaskParameters(task, parameters, s_outputProperties.Value);

        private static void SetTaskParameters(ResolveAssemblyReference task, Dictionary<string, TaskParameter> parameters, PropertyInfo[] properties)
        {
            foreach (PropertyInfo property in properties)
            {
                if (parameters.TryGetValue(property.Name, out TaskParameter? parameter))
                {
                    property.SetValue(task, parameter.WrappedParameter);
                }
            }
        }
    }
}
