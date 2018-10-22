module Main

open System
open System.Diagnostics
open System.IO
open Newtonsoft.Json.Linq
open System.Text.RegularExpressions
open System.Threading.Tasks
open Argu

type Arguments =
    | [<AltCommandLine("-c")>] Config of string
    | [<AltCommandLine("-s")>] Section of string
    | [<AltCommandLine("-i")>] Include of string list
    | [<AltCommandLine("-e")>] Exclude of string list
    with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Config _ -> "config filename (default: many.config.json)"
            | Section _ -> "section name inside config (no section, if not provided)"
            | Include _ -> "include tasks on startup (default: *)"
            | Exclude _ -> "exclude tasks on startup (default: none)"

type Response<'a> () =
    let response = TaskCompletionSource<'a> ()
    member x.Return value = response.SetResult value
    member x.Await () = response.Task.Result

type ActualState =
    | Running of Process
    | Stopped

type ExpectedState =
    | Running
    | Stopped

type Command =
    | Run
    | Kill
    | Inspect
    | Pulse
    | Sync of Response<unit>
    | Query of Response<(string * ActualState)>

let locked action = lock typeof<obj> action

let supervisor name command =
    let wait proc feed =
        async {
            do! Process.wait proc |> Async.AwaitTask
            feed Inspect
            printfn ">>> %s: Stopped" name
            do! Async.Sleep 5000
            feed Pulse
        } |> Async.Start
    let exec feed =
        locked (fun () -> 
            printfn ">>> %s: Starting" name
            let proc = Process.exec command
            wait proc feed
            ActualState.Running proc
        )
    let kill proc =
        printfn ">>> %s: Stopping" name
        Process.kill proc
        ActualState.Stopped
    let inspect proc =
        match (proc :> Process).HasExited with | true -> ActualState.Stopped | _ -> ActualState.Running proc

    let mutable actual = ActualState.Stopped
    let mutable expected = ExpectedState.Stopped

    let rec handle command feed =
        let handle command = handle command feed
        match command, expected, actual with
        | Run, _, _ -> expected <- ExpectedState.Running; handle Pulse
        | Kill, _, _ -> expected <- ExpectedState.Stopped; handle Pulse
        | Pulse, ExpectedState.Stopped, ActualState.Running proc -> actual <- kill proc
        | Pulse, ExpectedState.Running, ActualState.Stopped -> actual <- exec feed
        | Inspect, _, ActualState.Running proc -> actual <- inspect proc
        | Sync s, _, _ -> s.Return ()
        | Query r, _, s -> r.Return (name, s)
        | _ -> ()

    fun command feed -> async { handle command feed }

let (|Regex|_|) p s =
    match Regex.Match(s, p) with | m when m.Success -> Some (fun (g: string) -> m.Groups.[g].Value) | _ -> None

let wildcard w =
    Regex(Regex.Escape(w).Replace(@"\*", ".*").Replace(@"\?", ".") |> sprintf "^%s$").IsMatch

let loadTasks section filename =
    JObject.Load filename section
    |> (fun o -> o.Properties())
    |> Seq.map (fun p -> (p.Name, p.Value.ToString()))
    |> Seq.toList

let parseTasks filename arguments =
    let section = 
        arguments 
        |> Seq.choose (function | Section s -> Some s | _ -> None) 
        |> Seq.tryHead

    let configs = 
        arguments 
        |> Seq.choose (function | Config c -> Some c | _ -> None) 
        |> List.ofSeq 
        |> function | [] when File.Exists(filename) -> [filename] | l -> l  
    
    configs 
    |> Seq.collect (loadTasks section)
    |> Seq.rev
    |> Seq.distinctBy fst

[<EntryPoint>]
let main argv =
    let hr = "-" |> String.replicate 40
    let menu () =
        printfn "%s\n" hr
        printfn "  (Q)uit"
        printfn "  (L)ist"
        printfn ""
        printfn "  (K)ill <pattern>"
        printfn "  (R)un <pattern>"
        printfn "\n%s" hr

    try
        let parser = ArgumentParser.Create<Arguments>(programName = "many.exe")
        let arguments = parser.Parse(argv).GetAllResults()

        menu ()

        let tasks =
            arguments
            |> parseTasks "many.config.json"
            |> Seq.map (fun (name, command) -> name, Actor.create (supervisor name command))
            |> Map.ofSeq

        let enumerate pattern =
            let test = wildcard pattern
            tasks |> Map.toSeq |> Seq.filter (fun (name, _) -> test name) |> Seq.toArray

        let sync feed = let s = Response () in feed (Sync s); s.Await ()
        let query feed = let r = Response () in feed (Query r); r.Await ()
        let syncAll () = tasks |> Map.iter (fun _ feed -> sync feed)
        let run pattern = pattern |> enumerate |> Seq.iter (fun (_, feed) -> feed Run; sync feed)
        let kill pattern = pattern |> enumerate |> Seq.iter (fun (_, feed) -> feed Kill)
        let list () =
            let mkLine (n, s) = sprintf "  %s%s" n (match s with | ActualState.Stopped -> " (stopped)" | ActualState.Running p -> sprintf " (pid: %d)" p.Id)
            let content = tasks |> Map.toSeq |> Seq.map (fun (_, a) -> query a |> mkLine) |> Seq.reduce (sprintf "%s\n%s")
            printfn "%s\n" hr
            printfn "%s" content
            printfn "\n%s" hr

        let matchAny argf def =
            let includes =
                arguments
                |> Seq.choose argf
                |> Seq.collect id
                |> Seq.toList
                |> List.map wildcard
            fun name -> match includes with | [] -> def | _ -> includes |> List.exists (fun test -> test name)

        let includes = matchAny (fun arg -> match arg with | Include x -> Some x | _ -> None) true
        let excludes = matchAny (fun arg -> match arg with | Exclude x -> Some x | _ -> None) false

        let initializeAll () =
            tasks
            |> Map.toSeq
            |> Seq.choose (fun (name, feed) ->
                match includes name && not (excludes name) with | true -> Some feed | _ -> None)
            |> Seq.iter (fun feed -> feed Run)

        initializeAll ()

        let readline () = 
            let result = Console.ReadLine().Trim()
            if not (String.IsNullOrWhiteSpace(result)) then
                printfn "\n>>> %s" result
            result

        let rec loop () =
            match readline () with
            | "" | "h" -> menu (); true
            | "q" -> kill "*"; false
            | "l" -> list (); true
            | Regex "^k\s*(?<name>.*)$" group -> kill (group "name"); true
            | Regex "^r\s*(?<name>.*)$" group -> run (group "name"); true
            | _ -> (); true

        while loop () do ()

        syncAll ()

        0
    with
    | :? ArguParseException as a -> printfn "\n%s\n" a.Message; -1
    | e -> e.ToString() |> printfn "\n\n\n%s"; -2
