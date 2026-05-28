module Webhook.Program

open Microsoft.AspNetCore.Builder
open Giraffe

let webApp : HttpHandler =
    choose [
        GET >=> route "/health" >=> json {| status = "ok" |}
        RequestErrors.NOT_FOUND "Not Found"
    ]

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    builder.Services.AddGiraffe() |> ignore
    builder.WebHost.ConfigureKestrel(fun options ->
        options.ListenLocalhost(5000)
    ) |> ignore
    let app = builder.Build()
    app.UseGiraffe webApp
    printfn "🚀 Server rodando em http://127.0.0.1:5000"
    app.Run()
    0