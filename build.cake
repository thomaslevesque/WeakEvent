using System.Xml.Linq;

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");

// Variable definitions

var projectName = "WeakEvent";
var solutionFile = $"./{projectName}.sln";
var outDir = $"./{projectName}/bin/{configuration}";

var unitTestAssemblies = new[] { $"{projectName}.Tests/bin/{configuration}/{projectName}.Tests.dll" };

var nugetPackageId = $"ThomasLevesque.{projectName}";
var nuspecFile = $"./NuGet/{nugetPackageId}.nuspec";
var nugetDir = $"./NuGet/{configuration}";
var nupkgDir = $"{nugetDir}/nupkg";
var nugetTargets = new[] { $"dotnet", $"portable-net45+win8+wpa81+wp8" };
var nugetFiles = new[] { $"{projectName}.dll" };

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(() =>
{
    // Executed BEFORE the first task.
    Information("Running tasks...");
});

Teardown(() =>
{
    // Executed AFTER the last task.
    Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASK DEFINITIONS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(outDir);
    CleanDirectory(nugetDir);
});

Task("Build")
    .IsDependentOn("Clean")
    .Does(() =>
{
    MSBuild(solutionFile,
        settings => settings.SetConfiguration(configuration));
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    XUnit2(unitTestAssemblies);
});

Task("Pack")
    .IsDependentOn("Test")
    .Does(() =>
{
    
    CreateDirectory(nupkgDir);
    foreach (var target in nugetTargets)
    {
        string targetDir = $"{nupkgDir}/lib/{target}";
        CreateDirectory(targetDir);
        foreach (var file in nugetFiles)
        {
            CopyFileToDirectory($"{outDir}/{file}", targetDir);
        }
    }
    var packSettings = new NuGetPackSettings
    {
        BasePath = nupkgDir,
        OutputDirectory = nugetDir
    };
    NuGetPack(nuspecFile, packSettings);
});

Task("Push")
    .IsDependentOn("Pack")
    .Does(() =>
{
    var doc = XDocument.Load(nuspecFile);
    var ns = XNamespace.Get("http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd");
    string version = doc.Root.Element(ns + "metadata").Element(ns + "version").Value;
    string package = $"{nugetDir}/{nugetPackageId}.{version}.nupkg";
    NuGetPush(package, new NuGetPushSettings());
});

///////////////////////////////////////////////////////////////////////////////
// TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Test");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);
