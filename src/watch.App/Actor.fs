module Actor

let create f = 
    (MailboxProcessor.Start (fun inbox -> 
        let rec loop () = async { 
            let! m = inbox.Receive()
            do! f m inbox.Post
            do! loop ()
        }
        loop ()
    )).Post
