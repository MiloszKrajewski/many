module Main

open System
open System.IO
open System.Threading.Tasks
open System.Text.RegularExpressions

let delay secs action arg = 
    Task.Delay(secs*1000.0 |> int).ContinueWith(fun _ -> action arg) |> ignore
    
type Message = 
    | Change
    | Trigger of DateTime
    | Failed
    | Stopped

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
    watcher.NotifyFilter <- 
        NotifyFilters.FileName ||| NotifyFilters.DirectoryName ||| NotifyFilters.Size ||| NotifyFilters.LastWrite ||| NotifyFilters.LastAccess ||| CreationTime ||| Security
    fun () -> disposables |> Seq.iter (fun d -> d.Dispose ())

let createTrigger execute =
    let longPause = 3.0
    let mutable changed = DateTime.MinValue
    let mutable started = None
    
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
            started <- Some now
            execute post
        | Failed, None ->
            started <- None
            post Change
        | Stopped, Some timestamp when timestamp < changed ->
            started <- None
            post Change
        | Stopped, _ ->
            started <- None
        | _ -> ()
    })
    
    fun () -> post Change
    
let wildcardToRegex pattern = Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$")
let isMatch (regex: Regex) text = regex.IsMatch(text)    

let isIncluded includes excludes =
    let includes = includes |> Seq.map (wildcardToRegex >> isMatch) |> Seq.toArray 
    let excludes = excludes |> Seq.map (wildcardToRegex >> isMatch) |> Seq.toArray
    fun filename -> 
        (includes |> Array.isEmpty || includes |> Seq.exists (fun m -> m filename))
        && (excludes |> Seq.exists (fun m -> m filename) |> not)
 
[<EntryPoint>]
let main argv =
    let matcher = isIncluded ["*.cs"] []
    let trigger = createTrigger (fun () -> async { printfn "Triggered..." }) 
    let watcher = createWatcher "C:\\Temp" (fun (e, oldPath, newPath) -> 
        let newName = newPath |> Path.GetFileName
        let oldName = oldPath |> Option.map Path.GetFileName
        let applies = 
            match e with
            | WatcherChangeTypes.Changed | WatcherChangeTypes.Created -> matcher newName
            | WatcherChangeTypes.Renamed when oldName.IsSome -> matcher newName || matcher oldName.Value
            | WatcherChangeTypes.Renamed -> matcher newName
            | WatcherChangeTypes.Deleted -> true
            | _ -> false
        if applies then trigger () 
    )
    Console.ReadLine () |> ignore
    watcher ()
    0
