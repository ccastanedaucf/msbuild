// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Collections;
using ElementLocation = Microsoft.Build.Construction.ElementLocation;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Shared;

namespace Microsoft.Build.BackEnd
{
    /// <summary>
    /// This class is used by objects in the build engine that have the ability to execute themselves in batches, to partition the
    /// items they consume into "buckets", based on the values of select item metadata.
    /// </summary>
    /// <remarks>
    /// What batching does
    /// 
    /// Batching partitions the items consumed by the batchable object into buckets, where each bucket 
    /// contains a set of items that have the same value set on all item metadata consumed by the object. 
    /// Metadata consumed may be unqualified, for example %(m), or qualified by the item list to which it 
    /// refers, for example %(a.m).
    /// 
    /// If metadata is qualified, for example %(a.m), then this is considered distinct to metadata with the 
    /// same name on a different item type. For example, %(a.m) is distinct to %(b.m), and items of type ‘b’ 
    /// are considered to always have a blank value for %(a.m). This means items of type ‘b’ will only be 
    /// placed in buckets where %(a.m) is blank. However %(a.m) is equivalent to %(m) on items of type ‘a’.
    /// 
    /// There is an extra ambiguity rule: every items consumed by the object must have an explicit value for 
    /// every piece of unqualified metadata. For example, if @(a), %(m), and %(a.n) are consumed, every item 
    /// of type ‘a’ must have a value for the metadata ‘m’ but need not all necessarily have a value for the 
    /// metadata ‘n’. This rule eliminates ambiguity about whether items that do not define values for an 
    /// unqualified metadata should go in all buckets, or just into buckets with a blank value for 
    /// that metadata.
    /// 
    /// For example 
    /// 
    /// <ItemGroup>
    /// <a Include='a1;a2'>
    ///   <n>m0</n>
    /// </a>
    /// <a Include='a3'>
    ///   <n>m1</n>
    /// </a>
    /// <b Include='b1'>
    ///   <n>n0</n>
    /// </b>
    /// <b Include='b2;b3'>
    ///   <n>n1</n>
    /// </b>
    /// <b Include='b4'/>
    /// </ItemGroup>
    /// 
    /// <Target Name="t" >
    ///   <Message Text="a={@(a).%(a.n)} b={@(b).%(b.n)}" />
    /// </Target>
    /// 
    /// Will produce 5 buckets: 
    /// 
    /// a={a1;a2.m0} b={.}
    /// a={a3.m1} b={.}
    /// a={.} b={b1.n0}
    /// a={.} b={b2;b3.n1}
    /// a={.} b={b4.}
    /// 
    /// </remarks>
    internal static class BatchingEngine
    {
        private static readonly List<ProjectItemInstance> EmptyGroup = new List<ProjectItemInstance>();

        #region Methods

        /// <summary>
        /// Determines how many times the batchable object needs to be executed (each execution is termed a "batch"), and prepares
        /// buckets of items to pass to the object in each batch.
        /// </summary>
        /// <returns>List containing ItemBucket objects, each one representing an execution batch.</returns>
        internal static List<ItemBucket> PrepareBatchingBuckets
        (
            List<string> batchableObjectParameters,
            Lookup lookup,
            ElementLocation elementLocation,
            string explicitItemName = ""
        )
        {
            if (batchableObjectParameters == null)
            {
                ErrorUtilities.ThrowInternalError("Need the parameters of the batchable object to determine if it can be batched.");
            }
            if (lookup == null)
            {
                ErrorUtilities.ThrowInternalError("Need to specify the lookup.");
            }

            // All the @(itemname) item list references in the tag, including transforms, etc.        
            // and all the %(itemname.metadataname) references in the tag (not counting those embedded 
            // inside item transforms), and note that the itemname portion is optional.
            // The keys in the returned hash table are the qualified metadata names (e.g. "EmbeddedResource.Culture"
            // or just "Culture").  The values are MetadataReference structs, which simply split out the item 
            // name (possibly null) and the actual metadata name.       
            ItemsAndMetadataPair itemNamesAndMetadata = ExpressionShredder.GetReferencedItemNamesAndMetadata(batchableObjectParameters);

            // if the batchable object does not consume any item metadata or items, or if the item lists it consumes are all
            // empty, then the object does not need to be batched
            bool hasMetadata = itemNamesAndMetadata.Metadata?.Count > 0;
            return hasMetadata
                ? CreateBatchingBuckets(lookup, itemNamesAndMetadata, elementLocation, explicitItemName)
                : CreateSingleBucket(lookup);
        }

        private static List<ItemBucket> CreateSingleBucket(Lookup lookup)
        {
            // create a default bucket that references the project items and properties -- this way we always have a bucket
            var buckets = new List<ItemBucket>(1);
            var singleBucket = new ItemBucket(null, null, lookup);
            buckets.Add(singleBucket);

            return buckets;
        }

        private static List<ItemBucket> CreateBatchingBuckets
        (
            Lookup lookup,
            ItemsAndMetadataPair itemNamesAndMetadata,
            ElementLocation elementLocation,
            string explicitItemName
        )
        {
            if (itemNamesAndMetadata.Items == null)
            {
                itemNamesAndMetadata.Items = new HashSet<string>();
            }

            AddExplicitAndQualifiedItemNames(itemNamesAndMetadata, explicitItemName);
            VerifyThrowNoItemNamesToBatch(itemNamesAndMetadata, elementLocation);

            HashSet<string> itemNames = itemNamesAndMetadata.Items;
            List<MetadataName> metadataNames = GetMetadataNames(itemNamesAndMetadata.Metadata);
            Dictionary<MetadataValues, List<ProjectItemInstance>> metadataValuesToItems =
                MapMetadataValuesToItems(lookup, itemNames, metadataNames);
            

            return CreateItemBuckets(metadataNames, metadataValuesToItems);
        }

        private static void AddExplicitAndQualifiedItemNames(ItemsAndMetadataPair itemNamesAndMetadata, string explicitItemName)
        {
            // Add any item types that we were explicitly told to assume.
            AddItemNameIfNotNullOrEmpty(itemNamesAndMetadata.Items, explicitItemName);

            // Loop through all the metadata references and find the ones that are qualified
            // with an item name.
            foreach (KeyValuePair<string, MetadataReference> metadataName in itemNamesAndMetadata.Metadata)
            {
                // Also add this qualified item to the consumed item references list, because
                // %(EmbeddedResource.Culture) effectively means that @(EmbeddedResource) is
                // being consumed, even though we may not see literally "@(EmbeddedResource)"
                // in the tag anywhere.  Adding it to this list allows us (down below in this
                // method) to check that every item in this list has a value for each 
                // unqualified metadata reference.
                AddItemNameIfNotNullOrEmpty(itemNamesAndMetadata.Items, metadataName.Value.ItemName);
            }
        }

        private static void AddItemNameIfNotNullOrEmpty(HashSet<string> itemNames, string itemName)
        {
            if (!string.IsNullOrEmpty(itemName))
            {
                itemNames.Add(itemName);
            }
        }

        private static void VerifyThrowNoItemNamesToBatch(ItemsAndMetadataPair itemNamesAndMetadata, ElementLocation elementLocation)
        {
            // At this point, if there were any metadata references in the tag, but no item 
            // references to batch on, we've got a problem because we can't figure out which 
            // item lists the user wants us to batch.
            if (itemNamesAndMetadata.Items.Count > 0)
            {
                string unqualifiedMetadataName = itemNamesAndMetadata.Metadata.Keys.First();
                ProjectErrorUtilities.VerifyThrowInvalidProject
                (
                    false,
                    elementLocation,
                    "CannotReferenceItemMetadataWithoutItemName",
                    unqualifiedMetadataName
                );
            }
        }

        private static List<MetadataName> GetMetadataNames(Dictionary<string, MetadataReference> metadataNames)
        {
            var flattenedMetadataNames = new List<MetadataName>();

            foreach (KeyValuePair<string, MetadataReference> metadataName in metadataNames)
            {
                string fullName = metadataName.Key;
                string itemName = metadataName.Value.ItemName;
                string unqualifiedName = metadataName.Value.MetadataName;
                flattenedMetadataNames.Add(new MetadataName(fullName, itemName, unqualifiedName));
            }

            return flattenedMetadataNames;
        }

        private static Dictionary<MetadataValues, List<ProjectItemInstance>> MapMetadataValuesToItems
        (
            Lookup lookup,
            HashSet<string> itemNames,
            List<MetadataName> metadataNames
        )
        {
            var metadataValuesToItems = new Dictionary<MetadataValues, List<ProjectItemInstance>>();

            foreach (string itemName in itemNames)
            {
                foreach (ProjectItemInstance item in lookup.GetItems(itemName))
                {
                    MetadataValues metadataValues = GetMetadataValues(metadataNames, item);

                    if (!metadataValuesToItems.TryGetValue(metadataValues, out List<ProjectItemInstance> items))
                    {
                        items = new List<ProjectItemInstance>();
                        metadataValuesToItems[metadataValues] = items;
                    }
                    items.Add(item);
                }
            }

            return metadataValuesToItems;
        }

        private static MetadataValues GetMetadataValues
        (
            List<MetadataName> metadataNames,
            ProjectItemInstance item
        )
        {
            var metadataValues = new string[metadataNames.Count];

            for (int i = 0; i < metadataNames.Count; i++)
            {
                MetadataName metadataName = metadataNames[i];
                string metadataValueEscaped = ((IItem)item).GetMetadataValueEscaped(metadataName.UnqualifiedName);
                metadataValues[i] = metadataValueEscaped;
            }

            return new MetadataValues(metadataValues);
        }

        private static List<ItemBucket> CreateItemBuckets
        (
            HashSet<string> itemNames,
            List<MetadataName> metadataNames,
            Dictionary<MetadataValues, List<ProjectItemInstance>> metadataValuesToItems
        )
        {
            var buckets = new List<ItemBucket>(metadataValuesToItems.Count);

            foreach (KeyValuePair<MetadataValues, List<ProjectItemInstance>> metadataValuesWithItems in metadataValuesToItems)
            {
                MetadataValues metadataValues = metadataValuesWithItems.Key;
                List<ProjectItemInstance> items = metadataValuesWithItems.Value;
                var metadataNameToValue = new Dictionary<string, string>(metadataNames.Count, MSBuildNameIgnoreCaseComparer.Default);

                for (int i = 0; i < metadataNames.Count; i++)
                {
                    metadataNameToValue.Add(metadataNames[i].FullName, metadataValues.Values[i]);
                }

                buckets.Add(new ItemBucket());
            }

            // list of item name -> collection

            return buckets;
        }

        #endregion

        private struct MetadataName
        {
            internal readonly string FullName;

            internal readonly string ItemName;

            internal readonly string UnqualifiedName;

            internal MetadataName
            (
                string fullName,
                string itemName,
                string unqualifiedName
            )
            {
                FullName = fullName;
                ItemName = itemName;
                UnqualifiedName = unqualifiedName;
            }
        }

        private struct MetadataValues
        {
            internal readonly string[] Values;

            internal MetadataValues(string[] values)
            {
                Values = values;
            }
        }
    }
}
