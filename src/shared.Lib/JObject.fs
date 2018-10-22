module JObject

open System.IO
open Newtonsoft.Json.Linq

let Load filename section = 
    JObject.Parse(File.ReadAllText(filename))
    |> (fun o -> match section with | None -> o | Some s -> o.[s] :?> JObject)
