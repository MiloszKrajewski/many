module App

open Argu

let run<'Args when 'Args :> IArgParserTemplate> name argv code = 
    try
        let parser = ArgumentParser.Create<'Args>(programName = sprintf "%s.exe" name)
        let arguments = parser.Parse(argv).GetAllResults()
        
        code arguments

        0
    with
    | :? ArguParseException as a -> printfn "\n%s\n" a.Message; -1
    | e -> e.ToString() |> printfn "\n\n\n%s"; -2
