#!/bin/bash
# Gera certificado SSL auto-assinado para HTTPS (item opcional 6)
# Senha do certificado: webhook123

set -e

cd "$(dirname "$0")"

echo "🔐 Gerando certificado SSL auto-assinado..."

openssl req -x509 -newkey rsa:4096 \
    -keyout webhook.key \
    -out webhook.crt \
    -days 365 \
    -nodes \
    -subj "/C=BR/ST=SP/L=Sao Paulo/O=Insper/CN=localhost"

openssl pkcs12 -export \
    -out webhook.pfx \
    -inkey webhook.key \
    -in webhook.crt \
    -password pass:webhook123

# Limpa os arquivos intermediários
rm webhook.key webhook.crt

echo "✅ Certificado gerado em: certs/webhook.pfx"
echo "   Senha: webhook123"
echo ""
echo "Para rodar com HTTPS: dotnet run -- --https"
