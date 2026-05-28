module Webhook.Database

open Microsoft.Data.Sqlite
open Webhook.Domain

let private connectionString = "Data Source=webhook.db"

/// Inicializa o banco criando a tabela se não existir.
/// Função executada apenas uma vez na inicialização.
let initDatabase () : unit =
    use conn = new SqliteConnection(connectionString)
    conn.Open()
    let sql = """
        CREATE TABLE IF NOT EXISTS transactions (
            transaction_id TEXT PRIMARY KEY,
            event TEXT NOT NULL,
            amount TEXT NOT NULL,
            currency TEXT NOT NULL,
            timestamp TEXT NOT NULL,
            status TEXT NOT NULL,
            reason TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        )
    """
    use cmd = new SqliteCommand(sql, conn)
    cmd.ExecuteNonQuery() |> ignore

/// Verifica se uma transação já existe no banco (idempotência).
/// Função pura no sentido de não modificar estado externo.
let transactionExists (txId: string) : bool =
    use conn = new SqliteConnection(connectionString)
    conn.Open()
    use cmd = new SqliteCommand("SELECT COUNT(*) FROM transactions WHERE transaction_id = @id AND status = 'confirmed'", conn)
    cmd.Parameters.AddWithValue("@id", txId) |> ignore
    let count = cmd.ExecuteScalar() :?> int64
    count > 0L

/// Persiste uma transação confirmada no banco.
let saveConfirmed (payload: WebhookPayload) : unit =
    use conn = new SqliteConnection(connectionString)
    conn.Open()
    let sql = """
        INSERT OR REPLACE INTO transactions
        (transaction_id, event, amount, currency, timestamp, status, reason)
        VALUES (@id, @event, @amount, @currency, @timestamp, 'confirmed', NULL)
    """
    use cmd = new SqliteCommand(sql, conn)
    cmd.Parameters.AddWithValue("@id", payload.TransactionId) |> ignore
    cmd.Parameters.AddWithValue("@event", payload.Event) |> ignore
    cmd.Parameters.AddWithValue("@amount", payload.Amount) |> ignore
    cmd.Parameters.AddWithValue("@currency", payload.Currency) |> ignore
    cmd.Parameters.AddWithValue("@timestamp", payload.Timestamp) |> ignore
    cmd.ExecuteNonQuery() |> ignore

/// Persiste uma transação cancelada no banco.
let saveCancelled (txId: string) (reason: string) : unit =
    use conn = new SqliteConnection(connectionString)
    conn.Open()
    let sql = """
        INSERT OR REPLACE INTO transactions
        (transaction_id, event, amount, currency, timestamp, status, reason)
        VALUES (@id, '', '', '', '', 'cancelled', @reason)
    """
    use cmd = new SqliteCommand(sql, conn)
    cmd.Parameters.AddWithValue("@id", txId) |> ignore
    cmd.Parameters.AddWithValue("@reason", reason) |> ignore
    cmd.ExecuteNonQuery() |> ignore
