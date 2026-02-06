FROM node:18-alpine

WORKDIR /app

# Install OpenSSL and generate self-signed certificate with SANs
# Note: creating a config file for SANs
RUN apk add --no-cache openssl && \
    mkdir -p /app/certs && \
    printf "[dn]\nCN=sql_proxy\n[req]\ndistinguished_name = dn\n[EXT]\nsubjectAltName=DNS:sql_proxy" > /app/certs/openssl.cnf && \
    openssl req -x509 -newkey rsa:2048 -nodes -keyout /app/certs/server.key -out /app/certs/server.crt -days 3650 -subj "/CN=sql_proxy" -extensions EXT -config /app/certs/openssl.cnf

COPY server.js ./

EXPOSE 1433

CMD ["node", "server.js"]
