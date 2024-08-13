using Microsoft.Build.Tasks.AssemblyDependency;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
var rarService = new ResolveAssemblyReferenceService();
rarService.Execute();
