// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Tasks.AssemblyDependency;

// var rarTest = new ResolveAssemblyReferencePerfTester();
// await rarTest.Execute();
var rarService = new ResolveAssemblyReferenceService();
await rarService.ExecuteAsync();
