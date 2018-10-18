module Main

open System
open System.IO
open System.Threading.Tasks

let watchFolder folder handler = 
    let watcher = new FileSystemWatcher(folder)
    let handler (e: FileSystemEventArgs) = handler e
    let disposables = [
        watcher :> IDisposable
        watcher.Changed.Subscribe handler
        watcher.Created.Subscribe handler
        watcher.Deleted.Subscribe handler
        watcher.Renamed.Subscribe handler
    ]
    watcher.EnableRaisingEvents <- true
    fun () -> disposables |> Seq.iter (fun d -> d.Dispose ())

let delay secs action arg = Task.Delay(secs*1000.0 |> int).ContinueWith(fun _ -> action arg) |> ignore

type Message = 
    | Change
    | Trigger of int64
    | Started
    | Stopped

[<EntryPoint>]
let main argv =
    let mutable lastTrigger = DateTime.MinValue
    let mutable lastChange = DateTime.MinValue

    let post = Actor.create (fun message post -> async {
        let now = DateTime.Now
        match message with
        | Change when now.Subtract(lastChange).TotalSeconds > 0.1 -> 
            lastChange <- now
            delay 3.0 post (Trigger now.Ticks)
        | Trigger timestamp when timestamp = lastChange.Ticks ->
            ()
    })

    let handler (e: FileSystemEventArgs) = printfn "%A %s" e.ChangeType e.FullPath
    let watcher = watchFolder "C:\\Temp" handler
    Console.ReadLine () |> ignore
    watcher ()
    0
