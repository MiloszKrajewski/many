#r ".fake/FakeLib.dll"
#load "build.tools.fsx"

open Fake
open System.Threading

let build () = Proj.build "src"
let restore () = Proj.restore "src"
//let pack project = Proj.pack

Target "Clean" (fun _ -> !! "**/bin/" ++ "**/obj/" |> DeleteDirs)

Target "Restore" (fun _ -> restore ())

Target "Build" (fun _ ->
    "Common.targets" |> Proj.updateVersion Proj.productVersion Proj.assemblyVersion
    build ()
)

Target "Rebuild" ignore

Target "Release" (fun _ ->
    let libzApp = "packages/tools/LibZ.Tool/tools/libz.exe" |> FullName
    let out = "./.output"
    let libz = out @@ "libz"

    let publish appname =
        let out' = out @@ appname
        out' |> CleanDir
        Proj.publish out' (sprintf "src/%s.App" appname)
        !! (out' @@ "*.exe") ++ (out' @@ "*.dll") ++ (out' @@ "*.exe.config") |> CopyFiles libz

    libz |> CleanDir

    publish "many"
    publish "watch"

    !! (libz @@ "*.App.exe*") |> Seq.iter (fun fn -> fn |> Rename (fn.Replace(".App.", ".")))

    Shell.runAt libz libzApp "add -l many.libz -i *.dll --move"
    Shell.runAt libz libzApp "instrument -a many.exe --libz-file many.libz"
    Shell.runAt libz libzApp "instrument -a watch.exe --libz-file many.libz"
)

"Restore" ==> "Build"
"Build" ==> "Rebuild"
"Clean" ?=> "Restore"
"Clean" ==> "Rebuild"
"Rebuild" ==> "Release"

RunTargetOrDefault "Build"