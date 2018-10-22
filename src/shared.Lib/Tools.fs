namespace Tools

open System
open System.Text.RegularExpressions
open System.Threading.Tasks

module Set =
    let changes setA setB = Set.difference (Set.union setA setB) (Set.intersect setA setB)

module String =
    let join separator strings = String.Join(separator, strings :> seq<_>)
    
module Regex = 
    let wildcard w =
        Regex(Regex.Escape(w).Replace(@"\*", ".*").Replace(@"\?", ".") |> sprintf "^%s$").IsMatch

    
type Response<'a> () =
    let response = TaskCompletionSource<'a> ()
    member x.Return value = response.SetResult value
    member x.Await () = response.Task.Result
