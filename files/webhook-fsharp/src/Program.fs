module Webhook.Program

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Giraffe
open Webhook.Handlers
open Webhook.Database

// ============================================================================
// ROUTING - definição funcional das rotas via Giraffe
// ============================================================================

let webApp : HttpHandler =
    choose [
        POST >=> route "/webhook" >=> webhookHandler
        GET >=> route "/health" >=> json {| status = "ok" |}
        RequestErrors.NOT_FOUND "Not Found"
    ]

let configureApp (app: IApplicationBuilder) =
    app.UseGiraffe webApp

let configureServices (services: IServiceCollection) =
    services.AddGiraffe() |> ignore

// ============================================================================
// MAIN
// ============================================================================

[<EntryPoint>]
let main args =
    // Inicializa o banco SQLite
    initDatabase ()
    printfn "📦 Banco de dados SQLite inicializado (webhook.db)"

    // Determina se usar HTTPS (item opcional 6)
    let useHttps = args |> Array.contains "--https"
    let certPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "certs", "webhook.pfx")
    let httpsAvailable = useHttps && File.Exists(certPath)

    let builder = WebApplication.CreateBuilder(args)

    builder.WebHost.ConfigureKestrel(fun options ->
        // HTTP padrão na porta 5000
        options.ListenLocalhost(5000)

        // HTTPS opcional na porta 5443 (se certificado existir)
        if httpsAvailable then
            options.ListenLocalhost(5443, fun listenOptions ->
                listenOptions.UseHttps(certPath, "webhook123") |> ignore
            )
    ) |> ignore

    configureServices builder.Services
    let app = builder.Build()
    configureApp app

    printfn "🚀 Webhook server rodando em http://127.0.0.1:5000"
    if httpsAvailable then
        printfn "🔐 HTTPS rodando em https://127.0.0.1:5443"
    elif useHttps then
        printfn "⚠️  Flag --https passada, mas certificado não encontrado em %s" certPath
        printfn "    Veja o README para gerar o certificado."

    app.Run()
    0
