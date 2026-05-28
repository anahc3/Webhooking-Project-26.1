module Webhook.Domain

/// Payload recebido do gateway de pagamento
type WebhookPayload = {
    Event: string
    TransactionId: string
    Amount: string
    Currency: string
    Timestamp: string
}

/// Resultado da validação - usando Discriminated Union (estilo funcional)
/// Cada caso representa um motivo específico de cancelamento.
type ValidationError =
    | InvalidToken
    | InvalidPayload
    | MissingTransactionId
    | MissingField of fieldName: string
    | DuplicateTransaction of txId: string
    | AmountMismatch of txId: string
    | InvalidSignature of txId: string

/// Resultado do processamento do webhook
type WebhookResult =
    | Confirmed of txId: string
    | Cancelled of txId: string * reason: string
    | Rejected of reason: string  // Para erros que não geram cancelamento (ex: token inválido)

/// Valores constantes do sistema (para validação de veracidade)
module Constants =
    let SecretToken = "meu-token-secreto"
    let HmacSecret = "hmac-shared-secret-key"  // Para validação de integridade
    let ExpectedAmount = "49.90"
    let ExpectedCurrency = "BRL"
    let GatewayUrl = "http://127.0.0.1:5001"
