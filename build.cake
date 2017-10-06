using System.Xml.Linq;

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");

var projectName = "WeakEvent";
var solution = $"{projectName}.sln";
var libraryProject = $"./{projectName}/{projectName}.csproj";
var testProject = $"./{projectName}.Tests/{projectName}.Tests.csproj";
var outDir = $"./{projectName}/bin/{configuration}";

Task("Clean")
    .Does(() =>
{
    CleanDirectory(outDir);
});

Task("Restore")
    .Does(() => RunMSBuildTarget(solution, "Restore"));

Task("Build")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .Does(() => RunMSBuildTarget(solution, "Build"));

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    DotNetCoreTest(testProject, new DotNetCoreTestSettings { Configuration = configuration });
});

Task("Pack")
    .IsDependentOn("Test")
    .Does(() => RunMSBuildTarget(libraryProject, "Pack"));

Task("Push")
    .IsDependentOn("Pack")
    .Does(() =>
{
    var doc = XDocument.Load(libraryProject);
    string version = doc.Root.Elements("PropertyGroup").Elements("Version").First().Value;
    string package = $"{projectName}/bin/{configuration}/{projectName}.{version}.nupkg";
    NuGetPush(package, new NuGetPushSettings());
});

Task("Default")
    .IsDependentOn("Pack");

void RunMSBuildTarget(string projectOrSolution, string target)
{
    MSBuild(projectOrSolution, new MSBuildSettings
    {
        Targets = { target },
        Configuration = configuration
    });
}

RunTarget(target);
