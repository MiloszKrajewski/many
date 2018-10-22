module Watch

open System
open System.IO
open System.Threading.Tasks
open System.Text.RegularExpressions
open Argu
open Tools

let delay millis action arg = 
    Task.Delay(int millis).ContinueWith(fun _ -> action arg) |> ignore
    
type Arguments =
    | [<AltCommandLine("-f"); Unique>] Folder of string
    | [<AltCommandLine("-i")>] Include of string
    | [<AltCommandLine("-e")>] Exclude of string
    | [<MainCommand; ExactlyOnce; Last>] Command of command: string list
    with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Folder _ -> "root folder (default: .)" 
            | Include _ -> "include tasks on startup (default: *)"
            | Exclude _ -> "exclude tasks on startup (default: none)"
            | Command _ -> "command executed when changes are detected"
            
type Configuraion = {
    Folder: string
    Includes: string list
    Excludes: string list
    Command: string
}

let parseArguments arguments =
    let fix s = if s |> String.exists (fun c -> c = ' ') then sprintf "\"%s\"" s else s  
    {
        Folder = arguments |> Seq.tryPick (function | Folder f -> Some f | _ -> None) |> Option.defaultValue "."
        Includes = arguments |> Seq.choose (function | Include i -> Some i | _ -> None) |> List.ofSeq 
        Excludes = arguments |> Seq.choose (function | Exclude e -> Some e | _ -> None) |> List.ofSeq
        Command = 
            arguments 
            |> Seq.choose (function | Command c -> Some c | _ -> None) 
            |> Seq.collect id 
            |> Seq.map fix 
            |> String.join " "
    }
    
type Message = 
    | Change
    | Trigger of DateTime
    | Failed
    | Stopped

type ObjectType =
    | File
    | Directory
   
type ObjectInfo = {
    Type: ObjectType
    Name: string
    Timestamp: DateTime
}

type DirectoryInfo with member i.AsTuple () = { Type = Directory; Name = i.FullName; Timestamp = i.LastWriteTime }
type FileInfo with member i.AsTuple () = { Type = File; Name = i.FullName; Timestamp = i.LastWriteTime }

let rec scan (root: DirectoryInfo) = 
    seq {
        let root = if root.Exists then [root] else []
        let directories = root |> Seq.collect (fun r -> r.GetDirectories()) 
        let files = root |> Seq.collect (fun r -> r.GetFiles())
        yield! root |> Seq.map (fun i -> i.AsTuple())
        yield! files |> Seq.map (fun i -> i.AsTuple())
        yield! directories |> Seq.collect (fun d -> scan d)
    }

let createWatcher folder handler = 
    let watcher = new FileSystemWatcher(folder)
    let disposables = [
        watcher :> IDisposable
        watcher.Changed.Subscribe (fun e -> handler (e.ChangeType, None, e.FullPath))
        watcher.Created.Subscribe (fun e -> handler (e.ChangeType, None, e.FullPath))
        watcher.Deleted.Subscribe (fun e -> handler (e.ChangeType, None, e.FullPath))
        watcher.Renamed.Subscribe (fun e -> handler (e.ChangeType, Some e.OldFullPath, e.FullPath))
    ]
    watcher.EnableRaisingEvents <- true
    fun () -> disposables |> Seq.iter (fun d -> d.Dispose ())

let createTrigger folder matcher execute =
    let differences setA setB = Set.difference (Set.union setA setB) (Set.intersect setA setB)
    let scan () =
        printfn "Scanning..." 
        DirectoryInfo(folder) |> scan |> Seq.filter (fun i -> i.Type = File && matcher i.Name) |> Set.ofSeq

    let longPause = 3000
    let mutable changed = DateTime.MinValue
    let mutable started = None
    let mutable scanned = Set.empty
    
    let execute post = 
        async { 
            try 
                do! execute ()
                post Stopped 
            with _ -> 
                post Failed 
        } |> Async.StartAsTask |> ignore   
    
    let post = Actor.create (fun message post -> async {
        let now = DateTime.Now
        match message, started with
        | Change, _ ->
            changed <- now
            delay longPause post (Trigger now)
        | Trigger timestamp, None when timestamp = changed ->
            let scanned' = scan ()
            let trigger = differences scanned scanned' |> Seq.isEmpty |> not
            if trigger then
                printfn "Started..."
                scanned <- scanned'
                started <- Some now
                execute post
        | Failed, _ ->
            printfn "Failed. Scheduling retry..."
            started <- None
            post Change
        | Stopped, Some timestamp when timestamp < changed ->
            printfn "Finished, but changes detected since started. Scheduling rerun..."
            started <- None
            post Change
        | Stopped, _ ->
            printfn "Finished."
            started <- None
        | _ -> ()
    })
    
    fun () -> post Change
    
let isIncluded includes excludes =
    let includes = includes |> Seq.map Regex.wildcard |> Seq.toArray 
    let excludes = excludes |> Seq.map Regex.wildcard |> Seq.toArray
    fun filename -> 
        (includes |> Array.isEmpty || includes |> Seq.exists (fun m -> m filename))
        && (excludes |> Seq.exists (fun m -> m filename) |> not)
        
let run arguments =
    let parser = ArgumentParser.Create<Arguments>(programName = "watch.exe")
    let config = arguments |> parseArguments
    let execute () = async { 
        let proc = Process.exec config.Command
        do! proc |> Process.wait |> Async.AwaitTask
        match proc.ExitCode with | 0 -> () | c -> failwithf "Process returned error code %d" c 
    }
    
    let folder = config.Folder
    let matcher = isIncluded config.Includes config.Excludes
    let trigger = createTrigger folder matcher (fun () -> execute ()) 
    let watcher = createWatcher folder (fun _ -> trigger ())
    trigger ()
    while true do Console.ReadLine () |> ignore
