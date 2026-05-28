module Webhook.Handlers

open System.IO
open Microsoft.AspNetCore.Http
open Giraffe
open Webhook.Domain
open Webhook.Validation
open Webhook.Gateway
open Webhook.Database

// ============================================================================
// PIPELINE FUNCIONAL DE VALIDAÇÃO
// Compõe todas as validações usando Result.bind (>>=)
// Se qualquer etapa falhar, o pipeline curto-circuita com o erro.
// ============================================================================

/// Pipeline completo de validação do payload.
/// Retorna Ok payload (tudo válido) ou Error com o motivo da falha.
let validatePipeline (token: string option) (signature: string option) (rawBody: string) : Result<WebhookPayload, ValidationError> =
    validateToken token
    |> Result.bind (fun _ -> parsePayload rawBody)
    |> Result.bind extractTransactionId
    |> Result.bind validateRequiredFields
    |> Result.bind (validateNotDuplicate transactionExists)
    |> Result.bind validateAmount
    |> Result.bind (validateSignature signature rawBody)

// ============================================================================
// MAPEAMENTO DE ERROS PARA RESPOSTAS HTTP
// Cada tipo de erro gera uma resposta HTTP específica
// ============================================================================

type ErrorResponse = {
    status: string
    transaction_id: string option
    reason: string
}

/// Converte ValidationError em (statusCode, response).
/// Função pura que mapeia o erro para o formato HTTP esperado.
let errorToResponse (error: ValidationError) : int * ErrorResponse =
    match error with
    | InvalidToken ->
        403, { status = "cancelled"; transaction_id = None; reason = "invalid token" }
    | InvalidPayload ->
        400, { status = "cancelled"; transaction_id = None; reason = "invalid payload" }
    | MissingTransactionId ->
        400, { status = "cancelled"; transaction_id = None; reason = "missing field: transaction_id" }
    | MissingField field ->
        400, { status = "cancelled"; transaction_id = None; reason = sprintf "missing field: %s" field }
    | DuplicateTransaction txId ->
        400, { status = "cancelled"; transaction_id = Some txId; reason = "transaction duplicated" }
    | AmountMismatch txId ->
        400, { status = "cancelled"; transaction_id = Some txId; reason = "mismatch" }
    | InvalidSignature txId ->
        400, { status = "cancelled"; transaction_id = Some txId; reason = "invalid signature" }

/// Determina se um erro deve disparar uma chamada de cancelamento ao gateway.
/// InvalidToken e InvalidPayload NÃO geram cancelamento (não há tx_id confiável).
/// MissingTransactionId também não, pois não sabemos qual tx cancelar.
let shouldCancelOnError (error: ValidationError) : string option =
    match error with
    | InvalidToken | InvalidPayload | MissingTransactionId -> None
    | MissingField _ ->
        // Aqui precisamos extrair o txId em outro lugar (handler abaixo)
        None  // Tratado separadamente no handler
    | DuplicateTransaction txId
    | AmountMismatch txId
    | InvalidSignature txId -> Some txId

// ============================================================================
// HANDLER PRINCIPAL DO ENDPOINT /webhook
// ============================================================================

let webhookHandler : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            // Lê headers
            let token =
                match ctx.Request.Headers.TryGetValue("X-Webhook-Token") with
                | true, value -> Some (value.ToString())
                | _ -> None

            let signature =
                match ctx.Request.Headers.TryGetValue("X-Signature") with
                | true, value -> Some (value.ToString())
                | _ -> None

            // Lê o body bruto (necessário para HMAC)
            use reader = new StreamReader(ctx.Request.Body)
            let! rawBody = reader.ReadToEndAsync()

            // Executa o pipeline funcional de validação
            let result = validatePipeline token signature rawBody

            // Processa o resultado
            match result with
            | Ok payload ->
                // SUCESSO: confirma a transação
                do! confirmTransaction payload.TransactionId |> Async.StartAsTask :> System.Threading.Tasks.Task
                saveConfirmed payload
                ctx.SetStatusCode 200
                return! json {| status = "confirmed"; transaction_id = payload.TransactionId |} next ctx

            | Error err ->
                let statusCode, response = errorToResponse err

                // Tenta extrair tx_id para cancelamento em caso de MissingField
                // (já temos o body parseado nessa altura)
                let txIdForCancel =
                    match err with
                    | MissingField _ ->
                        try
                            let json = Newtonsoft.Json.Linq.JObject.Parse(rawBody)
                            match json.["transaction_id"] with
                            | null -> None
                            | t ->
                                let id = t.ToString()
                                if System.String.IsNullOrWhiteSpace(id) then None
                                else Some id
                        with _ -> None
                    | _ -> shouldCancelOnError err

                // Dispara cancelamento se aplicável
                match txIdForCancel with
                | Some txId ->
                    do! cancelTransaction txId |> Async.StartAsTask :> System.Threading.Tasks.Task
                    saveCancelled txId response.reason
                | None -> ()

                // Atualiza response com tx_id se disponível
                let finalResponse =
                    match txIdForCancel, response.transaction_id with
                    | Some txId, None -> { response with transaction_id = Some txId }
                    | _ -> response

                ctx.SetStatusCode statusCode
                let body =
                    match finalResponse.transaction_id with
                    | Some txId ->
                        box {| status = finalResponse.status; transaction_id = txId; reason = finalResponse.reason |}
                    | None ->
                        box {| status = finalResponse.status; reason = finalResponse.reason |}
                return! json body next ctx
        }
