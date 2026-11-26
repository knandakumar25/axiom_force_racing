import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { attachRaceRelay } from '../src/lib/raceRelay.js';

function nextMessage(ws) {
  return new Promise((resolve, reject) => {
    const onMessage = (ev) => {
      cleanup();
      resolve(JSON.parse(ev.data));
    };
    const onError = (err) => {
      cleanup();
      reject(err);
    };
    const cleanup = () => {
      ws.removeEventListener('message', onMessage);
      ws.removeEventListener('error', onError);
    };
    ws.addEventListener('message', onMessage);
    ws.addEventListener('error', onError);
  });
}

function openSocket(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    ws.addEventListener('open', () => resolve(ws), { once: true });
    ws.addEventListener('error', reject, { once: true });
  });
}

test('raceRelay creates rooms, joins peers, starts race, and relays kart state', async () => {
  const server = http.createServer();
  attachRaceRelay(server);
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address();
  const url = `ws://127.0.0.1:${port}/ws/race`;

  const host = await openSocket(url);
  const guest = await openSocket(url);
  try {
    const hostMsgPromise = nextMessage(host);
    host.send(JSON.stringify({ type: 'create', circuit: 'tokyo', driverId: 'cyber-shogun', name: 'Kaito' }));
    const created = await hostMsgPromise;
    assert.equal(created.type, 'created');
    assert.equal(created.code.length, 4);

    const hostPeerJoinedPromise = nextMessage(host);
    const guestJoinedPromise = nextMessage(guest);
    guest.send(JSON.stringify({ type: 'join', code: created.code, driverId: 'surya-prime', name: 'Arjun' }));
    const [peerJoined, joined] = await Promise.all([hostPeerJoinedPromise, guestJoinedPromise]);
    assert.equal(peerJoined.type, 'peer_joined');
    assert.equal(joined.type, 'joined');
    assert.equal(joined.peers.length, 2);

    const hostStarted = nextMessage(host);
    const guestStarted = nextMessage(guest);
    host.send(JSON.stringify({ type: 'start' }));
    const [s1, s2] = await Promise.all([hostStarted, guestStarted]);
    assert.equal(s1.type, 'started');
    assert.equal(s2.type, 'started');

    const guestStatePromise = nextMessage(guest);
    host.send(JSON.stringify({ type: 'state', x: 12.5, y: 1.0, z: -4.2, ry: 90, speed: 24.5, lap: 1, gate: 3 }));
    const relayed = await guestStatePromise;
    assert.equal(relayed.type, 'state');
    assert.equal(relayed.x, 12.5);
    assert.equal(relayed.speed, 24.5);
  } finally {
    host.close();
    guest.close();
    await new Promise((resolve) => server.close(resolve));
  }
});
