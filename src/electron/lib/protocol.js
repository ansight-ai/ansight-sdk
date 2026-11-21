"use strict";
const dgram = require("node:dgram");
const { randomUUID } = require("node:crypto");

function enroll({
  appId,
  appName,
  deviceId,
  accessToken,
  processSessionId,
  port = 45123,
  timeoutMs = 1500,
}) {
  return new Promise((resolve, reject) => {
    const socket = dgram.createSocket("udp4");
    const requestId = randomUUID();
    const timer = setTimeout(
      () => finish(new Error("Local Ansight enrollment timed out")),
      timeoutMs,
    );
    let done = false;
    function finish(error, value) {
      if (done) return;
      done = true;
      clearTimeout(timer);
      socket.close();
      error ? reject(error) : resolve(value);
    }
    socket.on("error", (error) => finish(error));
    socket.on("message", (bytes, remote) => {
      if (remote.address !== "127.0.0.1" || remote.port !== port) return;
      let result;
      try {
        result = JSON.parse(bytes.toString());
      } catch {
        return;
      }
      if (
        result.type !== "ENROLLMENT_RESULT" ||
        result.ver !== 2 ||
        result.requestId !== requestId
      )
        return;
      if (!result.accepted)
        return finish(
          new Error(
            result.reasonMessage || result.reason || "Enrollment rejected",
          ),
        );
      if (
        !Number.isInteger(result.webSocketPort) ||
        result.webSocketPort < 1 ||
        result.webSocketPort > 65535 ||
        typeof result.webSocketPath !== "string" ||
        !result.webSocketPath.startsWith("/") ||
        !result.webSocketToken
      ) {
        return finish(new Error("Invalid WebSocket handoff"));
      }
      const url = new URL(`ws://127.0.0.1:${result.webSocketPort}`);
      url.pathname = result.webSocketPath;
      url.searchParams.set("token", result.webSocketToken);
      finish(null, url.toString());
    });
    socket.send(
      JSON.stringify({
        type: "ENROLLMENT_CONNECT",
        ver: 2,
        enrollmentMode: "local",
        requestId,
        inviteId: `local:${appId}`,
        appId,
        deviceId,
        deviceName: appName,
        accessToken,
        processSessionId,
      }),
      port,
      "127.0.0.1",
    );
  });
}
function jpegFrame(bytes, width, height, quality = 70, timestamp = Date.now()) {
  const header = Buffer.alloc(28);
  header.write("ASJP");
  header[4] = 1;
  header[5] = 1;
  header[6] = quality;
  header.writeBigInt64LE(BigInt(timestamp), 8);
  header.writeInt32LE(width, 16);
  header.writeInt32LE(height, 20);
  header.writeInt32LE(bytes.length, 24);
  return Buffer.concat([header, bytes]);
}
function fileFrame(transferId, kind, sequence, offset, bytes) {
  const header = Buffer.alloc(56);
  header.write("ASFT");
  header[4] = 1;
  header[5] = kind;
  header.write(transferId.replaceAll("-", ""), 8, 32, "ascii");
  header.writeInt32LE(sequence, 40);
  header.writeBigInt64LE(BigInt(offset), 44);
  header.writeInt32LE(bytes.length, 52);
  return Buffer.concat([header, bytes]);
}
module.exports = { enroll, jpegFrame, fileFrame };
