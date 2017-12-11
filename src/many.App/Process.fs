module Process

open System
open System.Diagnostics
open System.Threading.Tasks
open System.Management

let fix (proc: Process option) =
    match proc with
    | None -> None
    | Some p when p.HasExited -> None
    | _ -> proc

let exec command =
    let comspec = Environment.GetEnvironmentVariable("COMSPEC")
    let arguments = command |> sprintf "/c %s"
    let info = ProcessStartInfo(comspec, arguments, UseShellExecute = false)
    Process.Start(info)

let wait (proc: Process) =
    match proc with
    | p when p.HasExited -> Task.FromResult(0) :> Task
    | p -> Task.Factory.StartNew((fun () -> p.WaitForExit()), TaskCreationOptions.LongRunning)

let private childrenW pid = 
    let query = pid |> sprintf "select * from Win32_Process where ParentProcessID = %d"
    use searcher = new ManagementObjectSearcher(query)
    let collection = searcher.Get() |> Seq.cast<ManagementObject>
    collection |> Seq.map (fun o -> o.["ProcessId"] |> Convert.ToInt32) |> Seq.toArray

let children (proc: Process) =
    match proc with
    | p when p.HasExited -> Seq.empty
    | p -> p.Id |> childrenW |> Seq.map Process.GetProcessById

let rec kill (proc: Process) =
    proc |> children |> Seq.iter kill
    proc.Kill()