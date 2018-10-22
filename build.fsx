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
    let out = "./.output"
    
    Proj.releaseNupkg ()
    
    let publish appname = 
        let out = out @@ appname
        out |> CleanDir
        Proj.publish out "src/many.App"
        Shell.runAt out libz (sprintf "inject-dll -a %s.App.exe -i *.dll --move" appname)
        !! (out @@ (sprintf "%s.App.exe*" appname)) 
        |> Seq.iter (fun fn -> fn |> Rename (fn.Replace(sprintf "%s.App" appname, appname)))
        
    publish "many"
    publish "watch"
)

"Restore" ==> "Build"
"Build" ==> "Rebuild"
"Clean" ?=> "Restore"
"Clean" ==> "Rebuild"
"Rebuild" ==> "Release"

RunTargetOrDefault "Build"