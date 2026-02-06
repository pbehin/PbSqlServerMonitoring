FROM burningalchemist/sql_exporter:latest AS source

FROM alpine:latest

# Install OpenSSL and CA Certificates
RUN apk add --no-cache openssl ca-certificates

RUN apk add --no-cache curl

# Copy the binary from the original image
COPY --from=source /bin/sql_exporter /bin/sql_exporter

# Config directory expected by the app (volume mount point)
RUN mkdir -p /etc/sql_exporter

# Enforce TLS 1.2 and High Security in OpenSSL
# Note: Go binaries usually ignore this unless using CGO+OpenSSL, 
# but we apply it as requested.
# RUN sed -i '1s/^/openssl_conf = openssl_init\n/' /etc/ssl/openssl.cnf && \
#     printf "\n[openssl_init]\nssl_conf = ssl_sect\n\n[ssl_sect]\nsystem_default = system_default_sect\n\n[system_default_sect]\nMinProtocol = TLSv1.2\nCipherString = DEFAULT@SECLEVEL=2\n" >> /etc/ssl/openssl.cnf

RUN sed -i '/\[\s*new_oids\s*\]/i openssl_conf = openssl_init\n[openssl_init]\nssl_conf = ssl_sect\n[ssl_sect]\nsystem_default = system_default_sect\n[system_default_sect]\nMinProtocol = TLSv1.2\nCipherString = DEFAULT@SECLEVEL=2\n' /etc/ssl/openssl.cnf


USER nobody

ENTRYPOINT ["/bin/sql_exporter"]
