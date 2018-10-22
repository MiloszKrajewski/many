namespace Tools

open System

module Set =
    let changes setA setB = Set.difference (Set.union setA setB) (Set.intersect setA setB)

module String =
    let join separator strings = String.Join(separator, strings :> seq<_>)