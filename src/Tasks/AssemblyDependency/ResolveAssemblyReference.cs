// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;
#if (!STANDALONEBUILD)
using Microsoft.Internal.Performance;
#endif

namespace Microsoft.Build.Tasks
{
    /// <summary>
    /// Given a list of assemblyFiles, determine the closure of all assemblyFiles that
    /// depend on those assemblyFiles including second and nth-order dependencies too.
    /// </summary>
    public class ResolveAssemblyReference : TaskExtension
    {
        #region Properties
        private ITaskItem[] _installedAssemblySubsetTables = Array.Empty<TaskItem>();
        private ITaskItem[] _fullFrameworkAssemblyTables = Array.Empty<TaskItem>();
        private string[] _targetFrameworkSubsets = Array.Empty<string>();
        private string[] _fullTargetFrameworkSubsetNames = Array.Empty<string>();

        private string _profileName = string.Empty;
        private string[] _fullFrameworkFolders = Array.Empty<string>();
        private WarnOrErrorOnTargetArchitectureMismatchBehavior _warnOrErrorOnTargetArchitectureMismatch = WarnOrErrorOnTargetArchitectureMismatchBehavior.Warning;

        /// <summary>
        /// If set to true, it forces to unresolve framework assemblies with versions higher or equal the version of the target framework, regardless of the target framework
        /// </summary>
        public bool UnresolveFrameworkAssembliesFromHigherFrameworks { get; set; } = false;

        /// <summary>
        /// If there is a mismatch between the targetprocessor architecture and the architecture of a primary reference.
        /// 
        /// When this is error,  an error will be logged. 
        /// 
        /// When this is warn, if there is a mismatch between the targetprocessor architecture and the architecture of a primary reference a warning will be logged.
        /// 
        /// When this is none, no error or warning will be logged.
        /// </summary>
        public string WarnOrErrorOnTargetArchitectureMismatch
        {
            get => _warnOrErrorOnTargetArchitectureMismatch.ToString();

            set
            {
                if (!Enum.TryParse(value, /*ignoreCase*/true, out _warnOrErrorOnTargetArchitectureMismatch))
                {
                    _warnOrErrorOnTargetArchitectureMismatch = WarnOrErrorOnTargetArchitectureMismatchBehavior.Warning;
                }
            }
        }
        /// <summary>
        /// A list of fully qualified paths-to-assemblyFiles to find dependencies for.
        ///
        /// Optional attributes are:
        ///     bool Private [default=true] -- means 'CopyLocal'
        ///     string FusionName -- the simple or strong fusion name for this item. If this 
        ///         attribute is present it can save time since the assembly file won't need
        ///         to be opened to get the fusion name.
        ///     bool ExternallyResolved [default=false] -- indicates that the reference and its
        ///        dependencies are resolved by an external system (commonly from nuget assets) and
        ///        so several steps can be skipped as an optimization: finding dependencies, 
        ///        satellite assemblies, etc.
        /// </summary>
        public ITaskItem[] AssemblyFiles { get; set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// The list of directories which contain the redist lists for the most current 
        /// framework which can be targeted on the machine. If this is not set
        /// Then we will looks for the highest framework installed on the machine 
        /// for a given target framework identifier and use that.
        /// </summary>
        public string[] LatestTargetFrameworkDirectories { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Should the framework attribute be ignored when checking to see if an assembly is compatible with the targeted framework.
        /// </summary>
        public bool IgnoreTargetFrameworkAttributeVersionMismatch { get; set; } = false;

        /// <summary>
        /// Force dependencies to be walked even when a reference is marked with ExternallyResolved=true
        /// metadata.
        /// </summary>
        /// <remarks>
        /// This is used to ensure that we suggest appropriate binding redirects for assembly version
        /// conflicts within an externally resolved graph.
        /// </remarks>
        public bool FindDependenciesOfExternallyResolvedReferences { get; set; }

        /// <summary>
        /// List of target framework subset names which will be searched for in the target framework directories
        /// </summary>
        public string[] TargetFrameworkSubsets
        {
            get => _targetFrameworkSubsets;
            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "TargetFrameworkSubsets");
                _targetFrameworkSubsets = value;
            }
        }

        /// <summary>
        /// These can either be simple fusion names like:
        ///
        ///      System
        ///
        /// or strong names like
        ///
        ///     System, Version=2.0.3500.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
        ///
        /// These names will be resolved into full paths and all dependencies will be found.
        ///
        /// Optional attributes are:
        ///     bool Private [default=true] -- means 'CopyLocal'
        ///     string HintPath [default=''] -- location of file name to consider as a reference, 
        ///         used when {HintPathFromItem} is one of the paths in SearchPaths.
        ///     bool SpecificVersion [default=absent] -- 
        ///         when true, the exact fusionname in the Include must be matched. 
        ///         when false, any assembly with the same simple name will be a match.
        ///         when absent, then look at the value in Include. 
        ///           If its a simple name then behave as if specific version=false.
        ///           If its a strong name name then behave as if specific version=true.
        ///     string ExecutableExtension [default=absent] -- 
        ///         when present, the resolved assembly must have this extension.
        ///         when absent, .dll is considered and then .exe for each directory looked at.
        ///     string SubType -- only items with empty SubTypes will be considered. Items 
        ///         with non-empty subtypes will be ignored.
        ///     string AssemblyFolderKey [default=absent] -- supported for legacy AssemblyFolder 
        ///         resolution. This key can have a value like 'hklm\vendor folder'. When set, only
        ///         this particular assembly folder key will be used.
        ///            This is to support the scenario in VSWhidey#357946 in which there are multiple
        ///            side-by-side libraries installed and the user wants to pick an exact version.
        ///     bool EmbedInteropTyeps [default=absent] -- 
        ///         when true, we should treat this assembly as if it has no dependencies and should 
        ///         be completely embedded into the target assembly.
        /// </summary>
        public ITaskItem[] Assemblies { get; set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// A list of assembly files that can be part of the search and resolution process.
        /// These must be absolute filesnames, or project-relative filenames.
        ///
        /// Assembly files in this list will be considered when SearchPaths contains
        /// {CandidateAssemblyFiles} as one of the paths to consider.
        /// </summary>
        public string[] CandidateAssemblyFiles { get; set; } = Array.Empty<string>();

        /// <summary>
        /// A list of resolved SDK references which contain the sdk name, sdk location and the targeted configuration.
        /// These locations will only be searched if the reference has the SDKName metadata attached to it.
        /// </summary>
        public ITaskItem[] ResolvedSDKReferences { get; set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Path to the target frameworks directory. Required to figure out CopyLocal status 
        /// for resulting items.
        /// If not present, then no resulting items will be deemed CopyLocal='true' unless they explicity 
        /// have a Private='true' attribute on their source item.
        /// </summary>
        public string[] TargetFrameworkDirectories { get; set; } = Array.Empty<string>();

        /// <summary>
        /// A list of XML files that contain assemblies that are expected to be installed on the target machine.
        /// 
        /// Format of the file is like:
        /// 
        ///     <FileList Redist="Microsoft-Windows-CLRCoreComp" >
        ///         <File AssemblyName="System" Version="2.0.0.0" PublicKeyToken="b77a5c561934e089" Culture="neutral" ProcessorArchitecture="MSIL" FileVersion="2.0.40824.0" InGAC="true" />
        ///         etc.
        ///     </FileList>
        /// 
        /// When present, assemblies from this list will be candidates to automatically "unify" from prior versions up to
        /// the version listed in the XML. Also, assemblies with InGAC='true' will be considered prerequisites and will be CopyLocal='false'
        /// unless explicitly overridden.
        /// Items in this list may optionally specify the "FrameworkDirectory" metadata to associate an InstalledAssemblyTable
        /// with a particular framework directory.  However, this setting will be ignored unless the Redist name begins with
        /// "Microsoft-Windows-CLRCoreComp".
        /// If there is only a single TargetFrameworkDirectories element, then any items in this list missing the
        /// "FrameworkDirectory" metadata will be treated as though this metadata is set to the lone (unique) value passed
        /// to TargetFrameworkDirectories.
        /// </summary>
        public ITaskItem[] InstalledAssemblyTables { get; set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// A list of XML files that contain assemblies that are expected to be in the target subset
        /// 
        /// Format of the file is like:
        /// 
        ///     <FileList Redist="ClientSubset" >
        ///         <File AssemblyName="System" Version="2.0.0.0" PublicKeyToken="b77a5c561934e089" Culture="neutral" ProcessorArchitecture="MSIL" FileVersion="2.0.40824.0" InGAC="true" />
        ///         etc.
        ///     </FileList>
        /// 
        /// Items in this list may optionally specify the "FrameworkDirectory" metadata to associate an InstalledAssemblySubsetTable
        /// with a particular framework directory. 
        /// If there is only a single TargetFrameworkDirectories element, then any items in this list missing the
        /// "FrameworkDirectory" metadata will be treated as though this metadata is set to the lone (unique) value passed
        /// to TargetFrameworkDirectories.
        /// </summary>
        public ITaskItem[] InstalledAssemblySubsetTables
        {
            get => _installedAssemblySubsetTables;
            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "InstalledAssemblySubsetTables");
                _installedAssemblySubsetTables = value;
            }
        }

        /// <summary>
        /// A list of XML files that contain the full framework for the profile.
        /// 
        /// Normally nothing is passed in here, this is for the cases where the location of the xml file for the full framework 
        /// is not under a RedistList folder.
        /// 
        /// Format of the file is like:
        /// 
        ///     <FileList Redist="MatchingRedistListName" >
        ///         <File AssemblyName="System" Version="2.0.0.0" PublicKeyToken="b77a5c561934e089" Culture="neutral" ProcessorArchitecture="MSIL" FileVersion="2.0.40824.0" InGAC="true" />
        ///         etc.
        ///     </FileList>
        /// 
        /// Items in this list must specify the "FrameworkDirectory" metadata to associate an redist list
        /// with a particular framework directory. If the association is not made an error will be logged. The reason is, 
        /// The logic in rar assumes if a FrameworkDirectory is not set it will use the target framework directory.
        /// </summary>
        public ITaskItem[] FullFrameworkAssemblyTables
        {
            get => _fullFrameworkAssemblyTables;
            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "FullFrameworkAssemblyTables");
                _fullFrameworkAssemblyTables = value;
            }
        }

        /// <summary>
        /// [default=false]
        /// Boolean property to control whether or not the task should look for and use additional installed 
        /// assembly tables (a.k.a Redist Lists) found in the RedistList directory underneath the provided
        /// TargetFrameworkDirectories.
        /// </summary>
        public bool IgnoreDefaultInstalledAssemblyTables { get; set; } = false;

        /// <summary>
        /// [default=false]
        /// Boolean property to control whether or not the task should look for and use additional installed 
        /// assembly subset tables (a.k.a Subset Lists) found in the SubsetList directory underneath the provided
        /// TargetFrameworkDirectories.
        /// </summary>
        public bool IgnoreDefaultInstalledAssemblySubsetTables { get; set; } = false;

        /// <summary>
        /// If the primary reference is a framework assembly ignore its version information and actually resolve the framework assembly from the currently targeted framework.
        /// </summary>
        public bool IgnoreVersionForFrameworkReferences { get; set; } = false;

        /// <summary>
        /// The preferred target processor architecture. Used for resolving {GAC} references. 
        /// Should be like x86, IA64 or AMD64.
        /// 
        /// This is the order of preference:
        /// (1) Assemblies in the GAC that match the supplied ProcessorArchitecture.
        /// (2) Assemblies in the GAC that have ProcessorArchitecture=MSIL
        /// (3) Assemblies in the GAC that have no ProcessorArchitecture.
        /// 
        /// If absent, then only consider assemblies in the GAC that have ProcessorArchitecture==MSIL or
        /// no ProcessorArchitecture (these are pre-Whidbey assemblies).
        /// </summary>
        public string TargetProcessorArchitecture { get; set; } = null;

        /// <summary>
        /// What is the runtime we are targeting, is it 2.0.57027 or anotherone, It can have a v or not prefixed onto it.
        /// </summary>
        public string TargetedRuntimeVersion { get; set; } = string.Empty;

        /// <summary>
        /// List of locations to search for assemblyFiles when resolving dependencies.
        /// The following types of things can be passed in here:
        /// (1) A plain old directory path.
        /// (2) {HintPathFromItem} -- Look at the HintPath attribute from the base item.
        ///     This attribute must be a file name *not* a directory name.
        /// (3) {CandidateAssemblyFiles} -- Look at the files passed in through the CandidateAssemblyFiles
        ///     parameter.
        /// (4) {Registry:_AssemblyFoldersBase_,_RuntimeVersion_,_AssemblyFoldersSuffix_}
        ///      Where:
        ///
        ///         _AssemblyFoldersBase_ = Software\Microsoft\[.NetFramework | .NetCompactFramework]
        ///         _RuntimeVersion_ = the runtime version property from the project file
        ///         _AssemblyFoldersSuffix_ = [ PocketPC | SmartPhone | WindowsCE]\AssemblyFoldersEx
        ///
        ///      Then look in the registry for keys with the following schema:
        ///
        ///         [HKLM | HKCU]\SOFTWARE\MICROSOFT\.NetFramework\
        ///           v1.0.3705
        ///             AssemblyFoldersEx
        ///                 ControlVendor.GridControl.1.0:
        ///                     @Default = c:\program files\ControlVendor\grid control\1.0\bin
        ///                     @Description = Grid Control for .NET version 1.0
        ///                     9466
        ///                         @Default = c:\program files\ControlVendor\grid control\1.0sp1\bin
        ///                         @Description = SP1 for Grid Control for .NET version 1.0
        ///
        ///      The based registry key is composed as:
        ///
        ///          [HKLM | HKCU]\_AssemblyFoldersBase_\_RuntimeVersion_\_AssemblyFoldersSuffix_
        ///
        /// (5) {AssemblyFolders} -- Use the VisualStudion 2003 .NET finding-assemblies-from-registry scheme.
        /// (6) {GAC} -- Look in the GAC.
        /// (7) {RawFileName} -- Consider the Include value to be an exact path and file name.
        ///
        ///
        /// </summary>
        /// <value></value>
        [Required]
        public string[] SearchPaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// [default=.exe;.dll]
        /// These are the assembly extensions that will be considered during references resolution.
        /// </summary>
        public string[] AllowedAssemblyExtensions { get; set; } = { ".winmd", ".dll", ".exe" };


        /// <summary>
        /// [default=.pdb;.xml]
        /// These are the extensions that will be considered when looking for related files.
        /// </summary>
        public string[] AllowedRelatedFileExtensions { get; set; } = { ".pdb", ".xml", ".pri" };


        /// <summary>
        /// If this file name is passed in, then we parse it as an app.config file and extract bindingRedirect mappings. These mappings are used in the dependency
        /// calculation process to remap versions of assemblies.
        ///
        /// If this parameter is passed in, then AutoUnify must be false, otherwise error.
        /// </summary>
        /// <value></value>
        public string AppConfigFile { get; set; } = null;

        /// <summary>
        /// This is true if the project type supports "AutoGenerateBindingRedirects" (currently only for EXE projects).
        /// </summary>
        /// <value></value>
        public bool SupportsBindingRedirectGeneration { get; set; }

        /// <summary>
        /// [default=false]
        /// This parameter is used for building assemblies, such as DLLs, which cannot have a normal
        /// App.Config file.
        ///
        /// When true, the resulting dependency graph is automatically treated as if there were an
        /// App.Config file passed in to the AppConfigFile parameter. This virtual
        /// App.Config file has a bindingRedirect entry for each conflicting set of assemblies such
        /// that the highest version assembly is chosen. A consequence of this is that there will never
        /// be a warning about conflicting assemblies because every conflict will have been resolved.
        ///
        /// When true, each distinct remapping will result in a high priority comment indicating the
        /// old and new versions and the fact that this was done automatically because AutoUnify was true.
        ///
        /// When true, the AppConfigFile parameter should be empty. Otherwise, it's an
        /// error.
        ///
        /// When false, no assembly version remapping will occur automatically. When two versions of an
        /// assembly are present, there will be a warning.
        ///
        /// When false, each distinct conflict between different versions of the same assembly will
        /// result in a high priority comment. After all of these comments are displayed, there will be
        /// a single warning with a unique error code and text that reads "Found conflicts between
        /// different versions of reference and dependent assemblies".
        /// </summary>
        /// <value></value>
        public bool AutoUnify { get; set; } = false;


        /// <summary>
        ///  When determining if a dependency should be copied locally one of the checks done is to see if the 
        ///  parent reference in the project file has the Private metadata set or not. If that metadata is set then 
        ///  We will use that for the dependency as well. 
        ///  
        /// However, if the metadata is not set then the dependency will go through the same checks as the parent reference. 
        /// One of these checks is to see if the reference is in the GAC. If a reference is in the GAC then we will not copy it locally
        /// as it is assumed it will be in the gac on the target machine as well. However this only applies to that specific reference and not its dependencies.
        /// 
        /// This means a reference in the project file may be copy local false due to it being in the GAC but the dependencies may still be copied locally because they are not in the GAC.
        /// This is the default behavior for RAR and causes the default value for this property to be true.
        /// 
        /// When this property is false we will still check project file references to see if they are in the GAC and set their copy local state as appropriate. 
        /// However for dependencies we will not only check to see if they are in the GAC but we will also check to see if the parent reference from the project file is in the GAC. 
        /// If the parent reference from the project file is in the GAC then we will not copy the dependency locally.
        /// 
        /// NOTE: If there are multiple parent reference and ANY of them does not come from the GAC then we will set copy local to true.
        /// </summary>
        public bool CopyLocalDependenciesWhenParentReferenceInGac { get; set; } = true;

        /// <summary>
        /// [default=false]
        /// Enables legacy mode for CopyLocal determination. If true, referenced assemblies will not be copied locally if they
        /// are found in the GAC. If false, assemblies will be copied locally unless they were found only in the GAC.
        /// </summary>
        public bool DoNotCopyLocalIfInGac { get; set; }

        /// <summary>
        /// An optional file name that indicates where to save intermediate build state
        /// for this task. If not specified, then no inter-build caching will occur.
        /// </summary>
        /// <value></value>
        public string StateFile { get; set; } = null;

        /// <summary>
        /// If set, then dependencies will be found. Otherwise, only Primary references will be
        /// resolved.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindDependencies { get; set; } = true;

        /// <summary>
        /// If set, then satellites will be found.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindSatellites { get; set; } = true;

        /// <summary>
        /// If set, then serialization assemblies will be found.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindSerializationAssemblies { get; set; } = true;

        /// <summary>
        /// If set, then related files (.pdbs and .xmls) will be found.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindRelatedFiles { get; set; } = true;

        /// <summary>
        /// If set, then don't log any messages to the screen.
        ///
        /// Default is false.
        /// </summary>
        /// <value></value>
        public bool Silent { get; set; } = false;

        /// <summary>
        /// The project target framework version.
        ///
        /// Default is empty. which means there will be no filtering for the reference based on their target framework.
        /// </summary>
        /// <value></value>
        public string TargetFrameworkVersion { get; set; } = string.Empty;

        /// <summary>
        /// The target framework moniker we are targeting if any. This is used for logging purposes.
        ///
        /// Default is empty.
        /// </summary>
        /// <value></value>
        public string TargetFrameworkMoniker { get; set; } = string.Empty;

        /// <summary>
        /// The display name of the target framework moniker, if any. This is only for logging.
        /// </summary>
        public string TargetFrameworkMonikerDisplayName { get; set; }

        /// <summary>
        /// Provide a set of names which if seen in the TargetFrameworkSubset list will cause the ignoring 
        /// of TargetFrameworkSubsets.
        /// 
        /// Full, Complete
        /// </summary>
        public string[] FullTargetFrameworkSubsetNames
        {
            get => _fullTargetFrameworkSubsetNames;

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "FullTargetFrameworkSubsetNames");
                _fullTargetFrameworkSubsetNames = value;
            }
        }

        /// <summary>
        /// Name of the target framework profile we are targeting.
        /// Eg. Client, Web, or Network
        /// </summary>
        public string ProfileName
        {
            get => _profileName;

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "profileName");
                _profileName = value;
            }
        }

        /// <summary>
        /// Set of folders which containd a RedistList directory which represent the full framework for a given client profile.
        /// An example would be 
        /// %programfiles%\reference assemblies\microsoft\framework\v4.0
        /// </summary>
        public string[] FullFrameworkFolders
        {
            get => _fullFrameworkFolders;

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "FullFrameworkFolders");
                _fullFrameworkFolders = value;
            }
        }

        /// <summary>
        /// This is a list of all primary references resolved to full paths.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        ///     string FusionName - the fusion name for this dependency.
        ///     string ResolvedFrom - the literal search path that this file was resolved from.
        ///     bool IsRedistRoot - Whether or not this assembly is the representative for an entire redist.
        ///         'true' means the assembly is representative of an entire redist and should be indicated as
        ///         an application dependency in an application manifest.
        ///         'false' means the assembly is internal to a redist and should not be part of the
        ///         application manifest.
        ///     string Redist - The name (if any) of the redist that contains this assembly.
        /// </summary>
        [Output]
        public ITaskItem[] ResolvedFiles { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// A list of all n-th order paths-to-dependencies with the following attributes:
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        ///     string FusionName - the fusion name for this dependency.
        ///     string ResolvedFrom - the literal search path that this file was resolved from.
        ///     bool IsRedistRoot - Whether or not this assembly is the representative for an entire redist.
        ///         'true' means the assembly is representative of an entire redist and should be indicated as 
        ///         an application dependency in an application manifest.
        ///         'false' means the assembly is internal to a redist and should not be part of the 
        ///         application manifest.
        ///     string Redist - The name (if any) of the redist that contains this assembly.
        /// Does not include first order primary references--this list is in ResolvedFiles.
        /// </summary>
        [Output]
        public ITaskItem[] ResolvedDependencyFiles { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Related files are files like intellidoc (.XML) and symbols (.PDB) that have the same base
        /// name as a reference.
        ///     bool Primary [always false] - true if this assembly was passed in with Assemblies.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        /// </summary>
        [Output]
        public ITaskItem[] RelatedFiles { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Any satellite files found. These will be CopyLocal=true iff the reference or dependency 
        /// that caused this item to exist is CopyLocal=true.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        ///     string DestinationSubDirectory - the relative destination directory that this file 
        ///       should be copied to. This is mainly for satellites.
        /// </summary>
        [Output]
        public ITaskItem[] SatelliteFiles { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Any XML serialization assemblies found. These will be CopyLocal=true iff the reference or dependency 
        /// that caused this item to exist is CopyLocal=true.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        /// </summary>
        [Output]
        public ITaskItem[] SerializationAssemblyFiles { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Scatter files associated with one of the given assemblies.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        /// </summary>
        [Output]
        public ITaskItem[] ScatterFiles { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Returns every file in ResolvedFiles+ResolvedDependencyFiles+RelatedFiles+SatelliteFiles+ScatterFiles+SatelliteAssemblyFiles
        /// that have CopyLocal flags set to 'true'.
        /// </summary>
        /// <value></value>
        [Output]
        public ITaskItem[] CopyLocalFiles { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Regardless of the value of AutoUnify, returns one item for every distinct conflicting assembly 
        /// identity--including culture and PKT--that was found that did not have a suitable bindingRedirect 
        /// entry in the ApplicationConfigurationFile. 
        ///
        /// Each returned ITaskItem will have the following values:
        ///  ItemSpec - the full fusion name of the assembly family with empty version=0.0.0.0
        ///  MaxVersion - the maximum version number.
        /// </summary>
        [Output]
        public ITaskItem[] SuggestedRedirects { get; private set; } = Array.Empty<TaskItem>();

        /// <summary>
        /// Storage for names of all files writen to disk.
        /// </summary>
        private ArrayList _filesWritten = new ArrayList();

        /// <summary>
        /// The names of all files written to disk.
        /// </summary>
        [Output]
        public ITaskItem[] FilesWritten => (ITaskItem[])_filesWritten.ToArray(typeof(ITaskItem));

        /// <summary>
        /// Whether the assembly or any of its primary references depends on system.runtime. (Aka needs Facade references to resolve duplicate types)
        /// </summary>
        [Output]
        public string DependsOnSystemRuntime { get; private set; }

        /// <summary>
        /// Whether the assembly or any of its primary references depends on netstandard
        /// </summary>
        [Output]
        public string DependsOnNETStandard { get; private set; }


        #endregion

        #region ITask Members


        private static void SendRequest(Stream stream, ResolveAssemblyReferenceRequest req)
        {
            var binaryFormatter = new BinaryFormatter();
            binaryFormatter.Serialize(stream, req);
        }

        private static ResolveAssemblyReferenceResponse GetResponse(Stream stream)
        {
            var binaryFormatter = new BinaryFormatter();
            return (ResolveAssemblyReferenceResponse)binaryFormatter.Deserialize(stream);
        }

        /// <summary>
        /// Execute the task.
        /// </summary>
        /// <param name="fileExists">Delegate used for checking for the existence of a file.</param>
        /// <param name="directoryExists">Delegate used for checking for the existence of a directory.</param>
        /// <param name="getDirectories">Delegate used for finding directories.</param>
        /// <param name="getAssemblyName">Delegate used for finding fusion names of assemblyFiles.</param>
        /// <param name="getAssemblyMetadata">Delegate used for finding dependencies of a file.</param>
        /// <param name="getRegistrySubKeyNames">Used to get registry subkey names.</param>
        /// <param name="getRegistrySubKeyDefaultValue">Used to get registry default values.</param>
        /// <param name="getLastWriteTime">Delegate used to get the last write time.</param>
        /// <returns>True if there was success.</returns>
        public override bool Execute()
        {
            var pipe = new NamedPipeClientStream(".", "ResolveAssemblyReference.Pipe", PipeDirection.InOut, PipeOptions.None);
            pipe.Connect();
            var req = ProduceRequest();

            SendRequest(pipe, req);

            ResolveAssemblyReferenceResponse resp = GetResponse(pipe);
            LogBuildEvents(resp.BuildEvents);
            ConsumeResponse(resp);

            return true;
        }

        private void LogBuildEvents(IList<LazyFormattedBuildEventArgs> buildEvents)
        {
            foreach (LazyFormattedBuildEventArgs buildEvent in buildEvents)
            {
                switch (buildEvent)
                {
                    case CustomBuildEventArgs customEventArgs:
                        BuildEngine.LogCustomEvent(customEventArgs);
                        break;
                    case BuildErrorEventArgs errorEventArgs:
                        BuildEngine.LogErrorEvent(errorEventArgs);
                        break;
                    case BuildMessageEventArgs messageEventArgs:
                        BuildEngine.LogMessageEvent(messageEventArgs);
                        break;
                    case BuildWarningEventArgs warningEventArgs:
                        BuildEngine.LogWarningEvent(warningEventArgs);
                        break;
                }
            }
        }

        private ResolveAssemblyReferenceRequest ProduceRequest()
        {
            return new ResolveAssemblyReferenceRequest
            {
                AllowedAssemblyExtensions = AllowedAssemblyExtensions,
                AllowedRelatedFileExtensions = AllowedRelatedFileExtensions,
                AppConfigFile = AppConfigFile,
                Assemblies = Assemblies,
                AssemblyFiles = AssemblyFiles,
                AutoUnify = AutoUnify,
                CandidateAssemblyFiles = CandidateAssemblyFiles,
                CopyLocalDependenciesWhenParentReferenceInGac = CopyLocalDependenciesWhenParentReferenceInGac,
                DoNotCopyLocalIfInGac = DoNotCopyLocalIfInGac,
                FindDependencies = FindDependencies,
                FindDependenciesOfExternallyResolvedReferences = FindDependenciesOfExternallyResolvedReferences,
                FindRelatedFiles = FindRelatedFiles,
                FindSatellites = FindSatellites,
                FindSerializationAssemblies = FindSerializationAssemblies,
                FullFrameworkAssemblyTables = FullFrameworkAssemblyTables,
                FullFrameworkFolders = FullFrameworkFolders,
                FullTargetFrameworkSubsetNames = FullTargetFrameworkSubsetNames,
                IgnoreDefaultInstalledAssemblySubsetTables = IgnoreDefaultInstalledAssemblySubsetTables,
                IgnoreDefaultInstalledAssemblyTables = IgnoreDefaultInstalledAssemblyTables,
                IgnoreTargetFrameworkAttributeVersionMismatch = IgnoreTargetFrameworkAttributeVersionMismatch,
                IgnoreVersionForFrameworkReferences = IgnoreVersionForFrameworkReferences,
                InstalledAssemblySubsetTables = InstalledAssemblySubsetTables,
                InstalledAssemblyTables = InstalledAssemblyTables,
                LatestTargetFrameworkDirectories = LatestTargetFrameworkDirectories,
                ProfileName = ProfileName,
                ResolvedSDKReferences = ResolvedSDKReferences,
                SearchPaths = SearchPaths,
                Silent = Silent,
                StateFile = StateFile,
                SupportsBindingRedirectGeneration = SupportsBindingRedirectGeneration,
                TargetFrameworkDirectories = TargetFrameworkDirectories,
                TargetFrameworkMoniker = TargetFrameworkMoniker,
                TargetFrameworkMonikerDisplayName = TargetFrameworkMonikerDisplayName,
                TargetFrameworkSubsets = TargetFrameworkSubsets,
                TargetFrameworkVersion = TargetFrameworkVersion,
                TargetProcessorArchitecture = TargetProcessorArchitecture,
                TargetedRuntimeVersion = TargetedRuntimeVersion,
                UnresolveFrameworkAssembliesFromHigherFrameworks = UnresolveFrameworkAssembliesFromHigherFrameworks,
                WarnOrErrorOnTargetArchitectureMismatch = WarnOrErrorOnTargetArchitectureMismatch
            };
        }

        private void ConsumeResponse(ResolveAssemblyReferenceResponse resp)
        {
            CopyLocalFiles = resp.CopyLocalFiles;
            _filesWritten = new ArrayList(resp.FilesWritten);
            DependsOnNETStandard = resp.DependsOnNETStandard;
            DependsOnSystemRuntime = resp.DependsOnSystemRuntime;
            RelatedFiles = resp.RelatedFiles;
            ResolvedDependencyFiles = resp.ResolvedDependencyFiles;
            ResolvedFiles = resp.ResolvedFiles;
            SatelliteFiles = resp.SatelliteFiles;
            ScatterFiles = resp.ScatterFiles;
            SerializationAssemblyFiles = resp.SerializationAssemblyFiles;
            SuggestedRedirects = resp.SuggestedRedirects;
        }
        #endregion
    }
}
