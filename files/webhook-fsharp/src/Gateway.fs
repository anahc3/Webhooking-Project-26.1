module Webhook.Gateway

open System.Net.Http
open System.Text
open Newtonsoft.Json
open Webhook.Domain

/// Cliente HTTP compartilhado (boas práticas: reutilizar instância).
let private httpClient = new HttpClient()

/// Envia uma requisição POST para o gateway com o transaction_id.
/// Função assíncrona que isola o efeito colateral (chamada HTTP).
let private postToGateway (endpoint: string) (txId: string) : Async<unit> =
    async {
        try
            let payload = JsonConvert.SerializeObject({| transaction_id = txId |})
            let content = new StringContent(payload, Encoding.UTF8, "application/json")
            let url = sprintf "%s/%s" Constants.GatewayUrl endpoint
            let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
            response.Dispose()
        with ex ->
            printfn "⚠️  Erro ao chamar gateway (%s): %s" endpoint ex.Message
    }

/// Confirma uma transação no gateway (item opcional 4).
let confirmTransaction (txId: string) : Async<unit> =
    postToGateway "confirmar" txId

/// Cancela uma transação no gateway (item opcional 3).
let cancelTransaction (txId: string) : Async<unit> =
    postToGateway "cancelar" txId
