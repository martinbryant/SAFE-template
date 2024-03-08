open System

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.Tools

let execContext = Context.FakeExecutionContext.Create false "build.fsx" [ ]
Context.setExecutionContext (Context.RuntimeContext.Fake execContext)

let skipTests = Environment.hasEnvironVar "yolo"
let release = ReleaseNotes.load "RELEASE_NOTES.md"

let templatePath = "./Content/.template.config/template.json"
let templateProj = "SAFE.Template.proj"
let templateName = "SAFE-Stack Web App"
let version = Environment.environVarOrDefault "VERSION" ""
let nupkgDir = Path.getFullName "./nupkg"
let nupkgPath = System.IO.Path.Combine(nupkgDir, $"SAFE.Template.%s{version}.nupkg")

let formattedRN =
    release.Notes
    |> List.map (sprintf "* %s")
    |> String.concat "\n"

Target.create "Clean" (fun _ ->
    Shell.cleanDirs [ nupkgDir ]
)

let msBuildParams msBuildParameter: MSBuild.CliArguments = { msBuildParameter with DisableInternalBinLog = true }

Target.create "Pack" (fun _ ->
    Shell.regexReplaceInFileWithEncoding
        "  \"name\": .+,"
        ("  \"name\": \"" + templateName + " v" + version + "\",")
        Text.Encoding.UTF8
        templatePath
    let releaseNotesUrl = Environment.environVarOrDefault "RELEASE_NOTES_URL" ""

    DotNet.pack
        (fun args ->
            { args with
                    Configuration = DotNet.BuildConfiguration.Release
                    OutputPath = Some nupkgDir
                    MSBuildParams = msBuildParams args.MSBuildParams
                    Common =
                        { args.Common with
                            CustomParams =
                                Some (sprintf "/p:PackageVersion=%s /p:PackageReleaseNotes=\"%s\""
                                        version
                                        releaseNotesUrl) }
            })
        templateProj
)

Target.create "Install" (fun _ ->
    let unInstallArgs = $"uninstall SAFE.Template"
    DotNet.exec (fun x -> { x with DotNetCliPath = "dotnet" }) "new" unInstallArgs
    |> ignore // Allow this to fail as the template might not be installed

    let installArgs = $"install \"%s{nupkgPath}\""
    DotNet.exec (fun x -> { x with DotNetCliPath = "dotnet" }) "new" installArgs
    |> fun result -> if not result.OK then failwith $"`dotnet new %s{installArgs}` failed with %O{result}"
)

let psi exe arg dir (x: ProcStartInfo) : ProcStartInfo =
    { x with
        FileName = exe
        Arguments = arg
        WorkingDirectory = dir }

let run exe arg dir =
    let result = Process.execWithResult (psi exe arg dir) TimeSpan.MaxValue
    if not result.OK then (failwithf "`%s %s` failed: %A" exe arg result.Errors)

Target.create "Tests" (fun _ ->
    let cmd = "run"
    let args = "--project tests/Tests.fsproj"
    let result = DotNet.exec (fun x -> { x with DotNetCliPath = "dotnet" }) cmd args
    if not result.OK then failwithf "`dotnet %s %s` failed" cmd args
)

Target.create "PreRelease" ignore

open Fake.Core.TargetOperators

"Clean"
    =?> ("Tests", not skipTests)
    ==> "Pack"
    ==> "Install"
    ==> "PreRelease"
|> ignore

[<EntryPoint>]
let main args =
    try
        match args with
        | [| target |] -> Target.runOrDefault target
        | _ -> Target.runOrDefault "Install"
        0
    with e ->
        printfn "%A" e
        1
