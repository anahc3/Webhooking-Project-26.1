module Webhook.Validation

open System
open System.Security.Cryptography
open System.Text
open Newtonsoft.Json.Linq
open Webhook.Domain

// ============================================================================
// FUNÇÕES PURAS DE VALIDAÇÃO
// Cada função recebe um input e retorna Result<Success, ValidationError>
// Isso permite encadear validações via Result.bind (pipeline funcional)
// ============================================================================

/// Valida o token de autenticação (veracidade da requisição).
let validateToken (token: string option) : Result<unit, ValidationError> =
    match token with
    | Some t when t = Constants.SecretToken -> Ok ()
    | _ -> Error InvalidToken

/// Valida se o body é um JSON válido e converte para JObject.
let parsePayload (body: string) : Result<JObject, ValidationError> =
    try
        if String.IsNullOrWhiteSpace(body) then
            Error InvalidPayload
        else
            let parsed = JObject.Parse(body)
            Ok parsed
    with _ ->
        Error InvalidPayload

/// Extrai e valida o transaction_id do JSON.
let extractTransactionId (json: JObject) : Result<string * JObject, ValidationError> =
    match json.["transaction_id"] with
    | null -> Error MissingTransactionId
    | token ->
        let txId = token.ToString()
        if String.IsNullOrWhiteSpace(txId) then
            Error MissingTransactionId
        else
            Ok (txId, json)

/// Valida que os campos obrigatórios estão presentes.
/// Retorna o payload completo se tudo estiver ok.
let validateRequiredFields (txId: string, json: JObject) : Result<WebhookPayload, ValidationError> =
    let requiredFields = ["event"; "amount"; "currency"; "timestamp"]

    let missingField =
        requiredFields
        |> List.tryFind (fun field -> json.[field] = null)

    match missingField with
    | Some field -> Error (MissingField field)
    | None ->
        Ok {
            Event = json.["event"].ToString()
            TransactionId = txId
            Amount = json.["amount"].ToString()
            Currency = json.["currency"].ToString()
            Timestamp = json.["timestamp"].ToString()
        }

/// Valida que a transação não foi confirmada anteriormente (idempotência).
/// Usa o banco como fonte da verdade.
let validateNotDuplicate (existsFn: string -> bool) (payload: WebhookPayload) : Result<WebhookPayload, ValidationError> =
    if existsFn payload.TransactionId then
        Error (DuplicateTransaction payload.TransactionId)
    else
        Ok payload

/// Valida que o valor e moeda batem com o esperado (veracidade da transação).
let validateAmount (payload: WebhookPayload) : Result<WebhookPayload, ValidationError> =
    if payload.Amount = Constants.ExpectedAmount && payload.Currency = Constants.ExpectedCurrency then
        Ok payload
    else
        Error (AmountMismatch payload.TransactionId)
