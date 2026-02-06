const net = require('net');
const tls = require('tls');
const fs = require('fs');
const http = require('http');
const { Duplex } = require('stream');

const MANAGEMENT_PORT = 8080;
const START_PORT = 1433;

// Global Constants
const PACKET_TYPE_PRELOGIN = 0x12;
const OPTION_ENCRYPTION = 0x01;
const ENCRYPT_ON = 0x01;
const HEADER_SIZE = 8;
const MAX_PACKET_SIZE = 4096;

console.log(`Starting Dynamic TLS Proxy Manager...`);
console.log(`Management API on port ${MANAGEMENT_PORT}`);

const certs = {
    key: fs.readFileSync('/app/certs/server.key'),
    cert: fs.readFileSync('/app/certs/server.crt')
};

// Map: "host:port" -> localPort
const routes = new Map();
let nextPort = START_PORT;

// Start Management API
const api = http.createServer((req, res) => {
    // console.log(`[API] Received ${req.method} ${req.url}`);

    if (req.method === 'POST' && req.url === '/register') {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => {
            // console.log(`[API] Body received: ${body}`);
            try {
                const { targetHost, targetPort } = JSON.parse(body);
                if (!targetHost || !targetPort) throw new Error("Missing targetHost or targetPort");

                const key = `${targetHost}:${targetPort}`;

                if (routes.has(key)) {
                    const allocatedPort = routes.get(key);
                    res.writeHead(200, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ port: allocatedPort, status: 'existing' }));
                    return;
                }

                const port = nextPort++;
                routes.set(key, port);

                startProxyListener(port, targetHost, parseInt(targetPort, 10));

                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ port, status: 'created' }));

            } catch (e) {
                console.error("[API] Error processing request:", e);
                res.writeHead(400, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ error: e.message }));
            }
        });
    } else if (req.url === '/health') {
        res.writeHead(200);
        res.end('OK');
    } else {
        res.writeHead(404);
        res.end();
    }
});

api.on('clientError', (err, socket) => socket.end('HTTP/1.1 400 Bad Request\r\n\r\n'));
api.listen(MANAGEMENT_PORT, '0.0.0.0', () => console.log(`Management API listening on 0.0.0.0:${MANAGEMENT_PORT}`));


// Proxy Logic Factory
function startProxyListener(localPort, targetHost, targetPort) {
    console.log(`Checking/Starting Proxy on :${localPort} -> ${targetHost}:${targetPort}`);

    const server = net.createServer((clientSocket) => {
        const clientAddr = `${clientSocket.remoteAddress}:${clientSocket.remotePort}`;
        console.log(`[${localPort}][${clientAddr}] New Connection -> ${targetHost}:${targetPort}`);

        let targetSocket = new net.Socket();
        let tlsServer = null;
        let isPreLoginPhase = true;
        let clientBuffer = Buffer.alloc(0);
        let targetBuffer = Buffer.alloc(0);
        let encryptedStream = null;
        let outgoingPacketId = 1;
        let resetNextPacketId = false; // Flag for Double Reset Strategy

        targetSocket.connect(targetPort, targetHost, () => {
            // Connected
        });

        targetSocket.on('error', (err) => {
            console.error(`[${localPort}][${clientAddr}] Target Socket Error:`, err.message);
            clientSocket.end();
        });

        targetSocket.on('close', () => clientSocket.end());
        clientSocket.on('close', () => {
            targetSocket.destroy();
            if (tlsServer) tlsServer.destroy();
        });
        clientSocket.on('error', (e) => console.error(`[${localPort}][${clientAddr}] Client Error:`, e.message));

        clientSocket.on('data', (data) => {
            clientBuffer = Buffer.concat([clientBuffer, data]);
            processClientBuffer();
        });

        targetSocket.on('data', (data) => {
            if (tlsServer) {
                targetBuffer = Buffer.concat([targetBuffer, data]);
                processTargetBuffer();
            } else {
                if (isPreLoginPhase) {
                    if (data.length > 8 && (data[0] === 0x12 || data[0] === 0x04)) {
                        const mod = forceEncryption(data);
                        clientSocket.write(mod.buffer);
                        if (mod.wasForced) {
                            console.log(`[${localPort}][${clientAddr}] Forced Encryption.`);
                            setupTlsTermination();
                        }
                    } else {
                        clientSocket.write(data);
                    }
                } else {
                    clientSocket.write(data);
                }
            }
        });

        function processClientBuffer() {
            while (clientBuffer.length >= HEADER_SIZE) {
                const length = clientBuffer.readUInt16BE(2);
                if (clientBuffer.length < length) break;
                const packet = clientBuffer.subarray(0, length);
                clientBuffer = clientBuffer.subarray(length);
                handleClientPacket(packet);
            }
        }

        function processTargetBuffer() {
            while (targetBuffer.length >= HEADER_SIZE) {
                const length = targetBuffer.readUInt16BE(2);
                if (targetBuffer.length < length) break;

                const packet = targetBuffer.subarray(0, length);
                targetBuffer = targetBuffer.subarray(length);

                const payload = packet.subarray(HEADER_SIZE);
                try {
                    tlsServer.write(payload);
                } catch (e) {
                    console.error("TLS Write Error:", e);
                }
            }
        }

        function handleClientPacket(packet) {
            if (tlsServer && encryptedStream) {
                const payload = packet.subarray(HEADER_SIZE);
                encryptedStream.push(payload);
            } else {
                targetSocket.write(packet);
            }
        }

        function setupTlsTermination() {
            encryptedStream = new Duplex({
                read(size) { },
                write(chunk, encoding, cb) {
                    // Logic: Use isPreLoginPhase flag.
                    // Initially True -> 0x12 (Handshake).
                    // On 'secure' -> False -> 0x04 (Response).
                    const type = isPreLoginPhase ? 0x12 : 0x04;
                    sendTdsPacket(chunk, type);
                    cb();
                }
            });

            tlsServer = new tls.TLSSocket(encryptedStream, {
                isServer: true,
                key: certs.key,
                cert: certs.cert,
                minVersion: 'TLSv1.2',
                maxVersion: 'TLSv1.2'
            });

            tlsServer.on('data', (cleartext) => {
                console.log(`[${localPort}] Decrypted Client Data: ${cleartext.length} bytes`);
                // if (cleartext.length > 0) console.log(`[HEX] ${cleartext.toString('hex', 0, 16)}...`);
                sendTdsPacketToTarget(cleartext);
            });

            tlsServer.on('error', e => console.error(`[${localPort}][${clientAddr}] TLS Error:`, e.message));

            tlsServer.on('secure', () => {
                console.log(`[${localPort}][${clientAddr}] TLS Established.`);
                isPreLoginPhase = false; // Next packet (Finished) will be 0x04
                outgoingPacketId = 1; // Reset #1: For the Finished Message
                resetNextPacketId = true; // Queue Reset #2: For the Login Ack
            });
        }

        function sendTdsPacket(payload, type) {
            console.log(`[${localPort}] Sending TDS Packet Type 0x${type.toString(16)} to Client. Payload: ${payload.length}`);

            // Debug corrupted 1st byte (Version 101)
            if (payload.length > 0 && payload[0] === 0x65) {
                console.error(`[CRITICAL] Sending 'e' (0x65) as first byte of TLS Record!`);
                if (payload.length > 5) console.error(`[HEX] ${payload.toString('hex', 0, 16)}`);
            }

            let offset = 0;
            while (offset < payload.length) {
                const chunk = payload.subarray(offset, Math.min(payload.length, offset + MAX_PACKET_SIZE - HEADER_SIZE));
                const header = Buffer.alloc(HEADER_SIZE);
                header.writeUInt8(type, 0);
                header.writeUInt8(0x01, 1);
                header.writeUInt16BE(chunk.length + HEADER_SIZE, 2);
                header.writeUInt16BE(0x0000, 4);
                header.writeUInt8(outgoingPacketId++ % 256, 6);
                header.writeUInt8(0x00, 7);
                clientSocket.write(Buffer.concat([header, chunk]));
                offset += chunk.length;
            }

            // Double Reset Strategy:
            // If we just sent the Finished Message (triggered by Secure), reset again for the next one.
            if (resetNextPacketId) {
                console.log(`[${localPort}] Double Reset triggered. Next PacketID will be 1.`);
                outgoingPacketId = 1;
                resetNextPacketId = false;
            }
        }

        let targetPacketId = 1;

        function sendTdsPacketToTarget(payload) {
            console.log(`[${localPort}] Sending TDS Packet to Target. Payload: ${payload.length}`);
            const type = (targetPacketId === 1) ? 0x10 : 0x01;

            let offset = 0;
            while (offset < payload.length) {
                const chunk = payload.subarray(offset, Math.min(payload.length, offset + MAX_PACKET_SIZE - HEADER_SIZE));
                const header = Buffer.alloc(HEADER_SIZE);
                header.writeUInt8(type, 0);
                header.writeUInt8(0x01, 1);
                header.writeUInt16BE(chunk.length + HEADER_SIZE, 2);
                header.writeUInt16BE(0x0000, 4);
                header.writeUInt8(targetPacketId++ % 256, 6);
                header.writeUInt8(0x00, 7);
                targetSocket.write(Buffer.concat([header, chunk]));
                offset += chunk.length;
            }
        }
    });

    server.listen(localPort, '0.0.0.0', () => {
        console.log(`[${localPort}] Proxy Listener started.`);
    });
}

function forceEncryption(buffer) {
    const buf = Buffer.from(buffer);
    let requestOffset = 8;
    let wasForced = true;
    if (buf.length < 8) return { buffer: buf, wasForced: false };
    while (requestOffset + 5 <= buf.length) {
        const token = buf[requestOffset];
        if (token === 0xFF) break;
        const offsetOffset = requestOffset + 1;
        const lenOffset = requestOffset + 3;
        const dataOffset = buf.readUInt16BE(offsetOffset);
        const dataLen = buf.readUInt16BE(lenOffset);
        if (token === OPTION_ENCRYPTION && dataLen >= 1) {
            const absPos = dataOffset;
            if (absPos < buf.length) {
                buf[absPos] = ENCRYPT_ON;
                wasForced = true;
            }
        }
        requestOffset += 5;
    }
    return { buffer: buf, wasForced };
}
