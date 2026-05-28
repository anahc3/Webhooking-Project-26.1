module Webhook.Handlers

open System.IO
open Microsoft.AspNetCore.Http
open Giraffe
open Webhook.Domain
open Webhook.Validation

/// Pipeline completo de validação do payload.
let validatePipeline (token: string option) (rawBody: string) : Result<WebhookPayload, ValidationError> =
    validateToken token
    |> Result.bind (fun _ -> parsePayload rawBody)
    |> Result.bind extractTransactionId
    |> Result.bind validateRequiredFields
    |> Result.bind validateAmount

type ErrorResponse = {
    status: string
    transaction_id: string option
    reason: string
}

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

let webhookHandler : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let token =
                match ctx.Request.Headers.TryGetValue("X-Webhook-Token") with
                | true, value -> Some (value.ToString())
                | _ -> None

            use reader = new StreamReader(ctx.Request.Body)
            let! rawBody = reader.ReadToEndAsync()

            let result = validatePipeline token rawBody

            match result with
            | Ok payload ->
                ctx.SetStatusCode 200
                return! json {| status = "confirmed"; transaction_id = payload.TransactionId |} next ctx
            | Error err ->
                let statusCode, response = errorToResponse err
                ctx.SetStatusCode statusCode
                let body =
                    match response.transaction_id with
                    | Some txId -> box {| status = response.status; transaction_id = txId; reason = response.reason |}
                    | None -> box {| status = response.status; reason = response.reason |}
                return! json body next ctx
        }