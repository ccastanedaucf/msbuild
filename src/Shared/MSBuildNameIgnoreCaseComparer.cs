// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Collections
{
    /// <summary>
    /// This is a custom string comparer that has three advantages over the regular
    /// string comparer:
    /// 1) It can generate hash codes and perform equivalence operations on parts of a string rather than a whole
    /// 2) It uses "unsafe" pointers to maximize performance of those operations
    /// 3) It takes advantage of limitations on MSBuild Property/Item names to cheaply do case insensitive comparison.
    /// </summary>
    [Serializable]
    internal class MSBuildNameIgnoreCaseComparer : IConstrainedEqualityComparer<string>
    {
        /// <summary>
        /// The processor architecture on which we are running, but default it will be x86
        /// </summary>
        private static readonly NativeMethodsShared.ProcessorArchitectures s_runningProcessorArchitecture;

        /// <summary>
        /// We need a static constructor to retrieve the running ProcessorArchitecture that way we can
        /// avoid using optimized code that will not run correctly on IA64 due to alignment issues
        /// </summary>
        static MSBuildNameIgnoreCaseComparer()
        {
            s_runningProcessorArchitecture = NativeMethodsShared.ProcessorArchitecture;
        }

        /// <summary>
        /// The default immutable comparer instance.
        /// </summary>
        internal static MSBuildNameIgnoreCaseComparer Default { get; } = new MSBuildNameIgnoreCaseComparer();

        public bool Equals(string x, string y)
        {
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase) == 0;
        }

        public int GetHashCode(string obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
        }

        /// <summary>
        /// Performs the "Equals" operation on two MSBuild property, item or metadata names
        /// </summary>
        public bool Equals(string compareToString, string constrainedString, int start, int lengthToCompare)
        {
            return String.Compare(compareToString, 0, constrainedString, start, lengthToCompare, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// Getting a case insensitive hash code for the msbuild property, item or metadata name
        /// </summary>
        public int GetHashCode(string obj, int start, int length)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Substring(start, length));
        }
    }
}
