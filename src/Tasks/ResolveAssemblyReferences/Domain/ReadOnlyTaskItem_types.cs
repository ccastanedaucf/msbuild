﻿
//------------------------------------------------------------------------------
// This code was generated by a tool.
//
//   Tool : Bond Compiler 0.11.0.0
//   Input filename:  BondTaskItem.bond
//   Output filename: BondTaskItem_types.cs
//
// Changes to this file may cause incorrect behavior and will be lost when
// the code is regenerated.
// <auto-generated />
//------------------------------------------------------------------------------


// suppress "Missing XML comment for publicly visible type or member"
#pragma warning disable 1591


#region ReSharper warnings
// ReSharper disable PartialTypeWithSinglePart
// ReSharper disable RedundantNameQualifier
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace
// ReSharper disable UnusedParameter.Local
// ReSharper disable RedundantUsingDirective
#endregion

namespace Microsoft.Build.Tasks.ResolveAssemblyReferences.Domain
{
    using System.Collections.Generic;

    [global::Bond.Schema]
    [System.CodeDom.Compiler.GeneratedCode("gbc", "0.11.0.0")]
    public partial class ReadOnlyTaskItem
    {
        [global::Bond.Id(0)]
        public string ItemSpec { get; set; }

        [global::Bond.Id(1)]
        public Dictionary<string, string> MetadataNameToValue { get; set; }

        [global::Bond.Id(2)]
        public List<int> ResponseFieldIds { get; set; }

        public ReadOnlyTaskItem()
            : this("Microsoft.Build.Tasks.ResolveAssemblyReferences.BondTypes.BondTaskItem", "BondTaskItem")
        { }

        protected ReadOnlyTaskItem(string fullName, string name)
        {
            ItemSpec = "";
            MetadataNameToValue = new Dictionary<string, string>();
            ResponseFieldIds = new List<int>();
        }
    }
} // Microsoft.Build.Tasks.ResolveAssemblyReferences.BondTypes
