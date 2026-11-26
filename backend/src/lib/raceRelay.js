import { createHash, randomBytes } from 'node:crypto';

const WS_MAGIC = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';
const ROOM_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

function encodeFrame(opcode, payloadBuffer) {
  const len = payloadBuffer.length;
  let header;
  if (len < 126) {
    header = Buffer.alloc(2);
    header[0] = 0x80 | opcode;
    header[1] = len;
  } else if (len < 65536) {
    header = Buffer.alloc(4);
    header[0] = 0x80 | opcode;
    header[1] = 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(10);
    header[0] = 0x80 | opcode;
    header[1] = 127;
    header.writeBigUInt64BE(BigInt(len), 2);
  }
  return Buffer.concat([header, payloadBuffer]);
}

function sendJson(socket, message) {
  if (socket.destroyed || !socket.writable) return;
  socket.write(encodeFrame(0x1, Buffer.from(JSON.stringify(message), 'utf8')));
}

function makeRoomCode(rooms) {
  for (let i = 0; i < 32; i++) {
    let code = '';
    const bytes = randomBytes(4);
    for (let j = 0; j < 4; j++) code += ROOM_CHARS[bytes[j] % ROOM_CHARS.length];
    if (!rooms.has(code)) return code;
  }
  return randomBytes(2).toString('hex').toUpperCase();
}

/**
 * Attaches a lightweight RFC 6455 WebSocket race relay at `/ws/race` to an HTTP server.
 * Supports room creation, joining by 4-character code, race start, and kart state broadcast.
 */
export function attachRaceRelay(server, { path = '/ws/race' } = {}) {
  const rooms = new Map();
  let nextPeerId = 1;

  function leaveRoom(peer) {
    if (!peer.roomCode) return;
    const room = rooms.get(peer.roomCode);
    peer.roomCode = null;
    if (!room) return;
    room.peers.delete(peer.id);
    for (const other of room.peers.values()) {
      sendJson(other.socket, { type: 'peer_left', peerId: peer.id });
    }
    if (room.peers.size === 0) {
      rooms.delete(room.code);
    } else if (room.hostId === peer.id) {
      const first = room.peers.values().next().value;
      room.hostId = first.id;
    }
  }

  function handleMessage(peer, raw) {
    let msg;
    try {
      msg = JSON.parse(raw);
    } catch {
      sendJson(peer.socket, { type: 'error', error: 'Invalid JSON' });
      return;
    }
    if (!msg || typeof msg.type !== 'string') return;

    if (msg.type === 'create') {
      leaveRoom(peer);
      const code = makeRoomCode(rooms);
      peer.name = String(msg.name || `Racer ${peer.id}`).slice(0, 24);
      peer.driverId = String(msg.driverId || 'cyber-shogun');
      peer.roomCode = code;
      const room = {
        code,
        circuit: String(msg.circuit || 'tokyo'),
        hostId: peer.id,
        started: false,
        peers: new Map([[peer.id, peer]])
      };
      rooms.set(code, room);
      sendJson(peer.socket, {
        type: 'created',
        code,
        peerId: peer.id,
        circuit: room.circuit,
        peers: [{ id: peer.id, name: peer.name, driverId: peer.driverId }]
      });
      return;
    }

    if (msg.type === 'join') {
      const code = String(msg.code || '').trim().toUpperCase();
      const room = rooms.get(code);
      if (!room) {
        sendJson(peer.socket, { type: 'error', error: 'Room not found' });
        return;
      }
      if (room.peers.size >= 10) {
        sendJson(peer.socket, { type: 'error', error: 'Room is full' });
        return;
      }
      leaveRoom(peer);
      peer.name = String(msg.name || `Racer ${peer.id}`).slice(0, 24);
      peer.driverId = String(msg.driverId || 'surya-prime');
      peer.roomCode = code;
      room.peers.set(peer.id, peer);
      const peerList = [...room.peers.values()].map((p) => ({
        id: p.id, name: p.name, driverId: p.driverId
      }));
      sendJson(peer.socket, {
        type: 'joined',
        code,
        peerId: peer.id,
        circuit: room.circuit,
        peers: peerList
      });
      for (const other of room.peers.values()) {
        if (other.id !== peer.id) {
          sendJson(other.socket, {
            type: 'peer_joined',
            peer: { id: peer.id, name: peer.name, driverId: peer.driverId }
          });
        }
      }
      return;
    }

    if (msg.type === 'start') {
      const room = peer.roomCode ? rooms.get(peer.roomCode) : null;
      if (!room || room.hostId !== peer.id) return;
      room.started = true;
      const peerList = [...room.peers.values()].map((p) => ({
        id: p.id, name: p.name, driverId: p.driverId
      }));
      for (const member of room.peers.values()) {
        sendJson(member.socket, {
          type: 'started',
          code: room.code,
          circuit: room.circuit,
          peers: peerList
        });
      }
      return;
    }

    if (msg.type === 'state') {
      const room = peer.roomCode ? rooms.get(peer.roomCode) : null;
      if (!room) return;
      const relay = {
        type: 'state',
        peerId: peer.id,
        driverId: typeof msg.driverId === 'string' ? msg.driverId : peer.driverId,
        x: Number(msg.x) || 0,
        y: Number(msg.y) || 0,
        z: Number(msg.z) || 0,
        ry: Number(msg.ry) || 0,
        speed: Number(msg.speed) || 0,
        lap: Number(msg.lap) || 0,
        gate: Number(msg.gate) || 0
      };
      for (const other of room.peers.values()) {
        if (other.id !== peer.id) sendJson(other.socket, relay);
      }
      return;
    }

    if (msg.type === 'leave') {
      leaveRoom(peer);
    }
  }

  server.on('upgrade', (req, socket) => {
    const reqPath = (req.url || '').split('?')[0];
    if (reqPath !== path) {
      socket.destroy();
      return;
    }
    const key = req.headers['sec-websocket-key'];
    if (!key) {
      socket.destroy();
      return;
    }
    const accept = createHash('sha1').update(`${key}${WS_MAGIC}`).digest('base64');
    socket.write(
      'HTTP/1.1 101 Switching Protocols\r\n'
      + 'Upgrade: websocket\r\n'
      + 'Connection: Upgrade\r\n'
      + `Sec-WebSocket-Accept: ${accept}\r\n\r\n`
    );

    const peer = { id: nextPeerId++, socket, roomCode: null, name: '', driverId: 'cyber-shogun' };
    let buffer = Buffer.alloc(0);

    socket.on('data', (chunk) => {
      buffer = Buffer.concat([buffer, chunk]);
      while (buffer.length >= 2) {
        const b0 = buffer[0];
        const b1 = buffer[1];
        const opcode = b0 & 0x0f;
        const masked = (b1 & 0x80) !== 0;
        let payloadLen = b1 & 0x7f;
        let offset = 2;
        if (payloadLen === 126) {
          if (buffer.length < 4) return;
          payloadLen = buffer.readUInt16BE(2);
          offset = 4;
        } else if (payloadLen === 127) {
          if (buffer.length < 10) return;
          payloadLen = Number(buffer.readBigUInt64BE(2));
          offset = 10;
        }
        const maskLen = masked ? 4 : 0;
        if (buffer.length < offset + maskLen + payloadLen) return;
        const mask = masked ? buffer.subarray(offset, offset + 4) : null;
        const payload = Buffer.from(buffer.subarray(offset + maskLen, offset + maskLen + payloadLen));
        buffer = buffer.subarray(offset + maskLen + payloadLen);

        if (mask) {
          for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i % 4];
        }
        if (opcode === 0x8) {
          leaveRoom(peer);
          socket.end(encodeFrame(0x8, Buffer.alloc(0)));
          return;
        }
        if (opcode === 0x9) {
          socket.write(encodeFrame(0xA, payload));
          continue;
        }
        if (opcode === 0x1) {
          handleMessage(peer, payload.toString('utf8'));
        }
      }
    });

    socket.on('close', () => leaveRoom(peer));
    socket.on('error', () => leaveRoom(peer));
  });

  return { rooms };
}
