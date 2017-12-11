#r ".fake/FakeLib.dll"
#load "build.tools.fsx"

open Fake

let build () = Proj.build "src"
let restore () = Proj.restore "src"
let pack project = Proj.pack

Target "Clean" (fun _ -> !! "**/bin/" ++ "**/obj/" |> DeleteDirs)

Target "Restore" (fun _ -> restore ())

Target "Build" (fun _ ->
    "Common.targets" |> Proj.updateVersion Proj.productVersion Proj.assemblyVersion
    build ()
)

Target "Rebuild" ignore

Target "Release" (fun _ ->
    let libz = "packages/tools/LibZ.Tool/tools/libz.exe" |> FullName
    let out = "./.output/many"

    out |> CleanDir
    Proj.releaseNupkg ()
    Proj.publish out "src/many.App"

    Shell.runAt out libz "inject-dll -a many.App.exe -i *.dll --move"
    !! (out @@ "many.App.exe*") |> Seq.iter (fun fn -> fn |> Rename (fn.Replace("many.App", "many")))
)

"Restore" ==> "Build"
"Build" ==> "Rebuild"
"Clean" ?=> "Restore"
"Clean" ==> "Rebuild"
"Rebuild" ==> "Release"

RunTargetOrDefault "Build"