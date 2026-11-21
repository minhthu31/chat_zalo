"""Exercise the published HTTPS stack with independent clients and real cookies.

Creates three uniquely named demo accounts and messages in the selected deployment.
Use --insecure-localhost only for Caddy's local development certificate.
"""
import argparse
import http.cookiejar
import json
import ssl
import subprocess
import time
import uuid
from urllib import request, error, parse
from websockets.sync.client import connect


class Client:
    def __init__(self, base, context):
        self.base = base.rstrip('/')
        self.cookies = http.cookiejar.CookieJar()
        self.opener = request.build_opener(request.HTTPSHandler(context=context), request.HTTPCookieProcessor(self.cookies))

    def call(self, method, path, body=None, expected=200):
        headers = {}
        if method != 'GET':
            headers['X-CSRF-TOKEN'] = self.call('GET', '/api/auth/csrf')['token']
        if body is not None:
            headers['Content-Type'] = 'application/json'
        req = request.Request(self.base + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
        try:
            response = self.opener.open(req, timeout=20)
        except error.HTTPError as exc:
            response = exc
        raw = response.read()
        assert response.status == expected, (method, path, response.status, raw.decode()[:400])
        return json.loads(raw) if raw and 'json' in response.headers.get('Content-Type', '') else raw

    def cookie_header(self):
        req = request.Request(self.base + '/hubs/chat')
        self.cookies.add_cookie_header(req)
        return req.get_header('Cookie')


def run(base, context, restart_api=False):
    clients = [Client(base, context) for _ in range(3)]
    assert b'<html' in clients[0].call('GET', '/').lower()
    assert clients[0].call('GET', '/appsettings.json')['ApiBaseUrl'] == '/'
    script = clients[0].call('GET', '/_framework/blazor.webassembly.js')
    assert len(script) > 100 and not script.lstrip().startswith(b'<!DOCTYPE')
    tag = uuid.uuid4().hex[:10]
    password = 'Smoke_' + uuid.uuid4().hex + '!9aA'
    accounts = []
    for index, client in enumerate(clients):
        name = f'chat23_{tag}_{index}'
        user = client.call('POST', '/api/auth/register', {'username': name, 'email': name + '@example.test', 'password': password}, 201)
        client.call('POST', '/api/auth/login', {'usernameOrEmail': name, 'password': password})
        assert client.call('GET', '/api/auth/me')['id'] == user['id']
        accounts.append(user)
    if restart_api:
        subprocess.run(['docker', 'compose', '--env-file', 'deploy/.env', '-f', 'deploy/compose.yml', 'up', '-d', '--no-deps', '--force-recreate', 'api'], check=True)
        for attempt in range(40):
            try:
                clients[0].call('GET', '/health')
                break
            except (OSError, AssertionError):
                if attempt == 39:
                    raise
                time.sleep(0.5)
        for client, user in zip(clients, accounts):
            assert client.call('GET', '/api/auth/me')['id'] == user['id']
        print('PASS: persisted database and login cookies survive API container recreation.')
    a, b, c = clients
    aid, bid, cid = [user['id'] for user in accounts]
    results = a.call('GET', '/api/users/search?username=' + accounts[1]['username'])
    assert any(user['id'] == bid for user in results)
    paged = a.call('GET', '/api/users/search/paged?username=chat23_' + tag + '&page=1&pageSize=10')
    assert paged['totalCount'] == 3
    for client, target in [(a, bid), (b, cid)]:
        client.call('POST', '/api/friendships', {'targetUserId': target}, 201)
    b.call('POST', f'/api/friendships/{aid}/accept', expected=204)
    c.call('POST', f'/api/friendships/{bid}/accept', expected=204)
    assert any(friend['userId'] == bid for friend in a.call('GET', '/api/friendships'))
    conversation = a.call('POST', '/api/conversations/direct', {'targetUserId': bid}, 201)['id']

    # Verify WebSocket upgrade through the same HTTPS reverse proxy and actual delivery.
    negotiated = b.call('POST', '/hubs/chat/negotiate?negotiateVersion=1')
    ws_url = base.replace('https://', 'wss://', 1).rstrip('/') + '/hubs/chat?id=' + parse.quote(negotiated['connectionToken'])
    with connect(ws_url, ssl=context, additional_headers={'Cookie': b.cookie_header()}, open_timeout=20) as ws:
        ws.send('{"protocol":"json","version":1}\x1e')
        assert ws.recv(timeout=10) == '{}\x1e'
        ws.send(json.dumps({'type': 1, 'invocationId': '1', 'target': 'JoinConversation', 'arguments': [conversation]}) + '\x1e')
        while True:
            records = [json.loads(item) for item in ws.recv(timeout=10).split('\x1e') if item]
            completion = next((item for item in records if item.get('type') == 3 and item.get('invocationId') == '1'), None)
            if completion:
                assert 'error' not in completion, completion
                break
        message = a.call('POST', f'/api/conversations/{conversation}/messages', {'clientMessageId': str(uuid.uuid4()), 'body': 'CHAT-23 HTTPS smoke message'}, 201)
        while True:
            records = [json.loads(item) for item in ws.recv(timeout=10).split('\x1e') if item]
            event = next((item for item in records if item.get('target') == 'ConversationChanged'), None)
            if event:
                assert 'body' not in json.dumps(event)
                break
    history = b.call('GET', f'/api/conversations/{conversation}/messages')
    assert any(item['id'] == message['id'] for item in history)
    a.call('PUT', f'/api/blocks/{bid}', expected=204)
    b.call('GET', f'/api/conversations/{conversation}/messages', expected=403)
    a.call('DELETE', f'/api/blocks/{bid}', expected=204)
    assert b.call('GET', f'/api/conversations/{conversation}/messages')
    a.call('DELETE', f'/api/friendships/{bid}', expected=204)
    assert not any(friend['userId'] == bid for friend in a.call('GET', '/api/friendships'))

    other = Client(base, context)
    other.call('POST', '/api/auth/login', {'usernameOrEmail': accounts[0]['username'], 'password': password})
    new_password = password + 'New!'
    a.call('POST', '/api/auth/change-password', {'currentPassword': password, 'newPassword': new_password}, 204)
    a.call('GET', '/api/auth/me', expected=401)
    other.call('GET', '/api/auth/me', expected=401)
    other.call('POST', '/api/auth/login', {'usernameOrEmail': accounts[0]['username'], 'password': password}, 401)
    other.call('POST', '/api/auth/login', {'usernameOrEmail': accounts[0]['username'], 'password': new_password})
    print('PASS: 3 clients, shared users, paging, friendship chain, accept/remove, Direct messages, WebSocket notification, block/unblock, password change and session revocation.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('url')
    parser.add_argument('--insecure-localhost', action='store_true')
    parser.add_argument('--restart-api', action='store_true', help='Recreate the local Compose API and verify persisted cookies/data.')
    args = parser.parse_args()
    context = ssl.create_default_context()
    if args.insecure_localhost:
        assert parse.urlparse(args.url).hostname == 'localhost', 'TLS bypass is only for localhost.'
        context = ssl._create_unverified_context()
    assert args.url.startswith('https://'), 'Exercise HTTPS, including Secure cookies.'
    if args.restart_api:
        assert parse.urlparse(args.url).hostname == 'localhost', 'Only recreate the local deployment.'
    run(args.url, context, args.restart_api)
