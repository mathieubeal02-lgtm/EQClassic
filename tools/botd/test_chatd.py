#!/usr/bin/env python3
"""Checks chatd.py (milestone 4) against a local stub of the LLM API: no network, no real key.

- llm off: a player's tell gets a template.
- llm on: a normal reply passes; a reply with a URL and one with a blocked word are dropped
  (logged chat=filtered); the calls-per-hour cap holds over a burst; bots get no LLM answer.
- LFG lines: at most one per bot per interval; OOC: one bot line per N seconds.
Exit 1 on failure.
"""
import json
import os
import sys
import tempfile
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import chatd  # noqa: E402

REPLIES = ['hey, just camping gnolls here', 'check out www.example.com for plat', 'you are a retard', 'k']
stub_calls = []


class Stub(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers['content-length'])))
        stub_calls.append(body)
        text = REPLIES[(len(stub_calls) - 1) % len(REPLIES)]
        out = json.dumps({'content': [{'type': 'text', 'text': text}],
                          'usage': {'input_tokens': 600, 'output_tokens': 10}}).encode()
        self.send_response(200)
        self.send_header('content-length', str(len(out)))
        self.end_headers()
        self.wfile.write(out)


def serve(handler):
    s = ThreadingHTTPServer(('127.0.0.1', 0), handler)
    threading.Thread(target=s.serve_forever, daemon=True).start()
    return s


def post(port, path, obj):
    rq = urllib.request.Request('http://127.0.0.1:%d%s' % (port, path), data=json.dumps(obj).encode(), method='POST')
    with urllib.request.urlopen(rq, timeout=10) as r:
        return json.loads(r.read())


failures = 0


def check(ok, what):
    global failures
    print('[ OK ]' if ok else '[FAIL]', what)
    failures += 0 if ok else 1


def start_chat(**over):
    log = tempfile.NamedTemporaryFile('w', suffix='.log', delete=False).name
    cfg = dict(chatd.DEFAULTS, log=log, **over)
    chat = chatd.Chat(cfg)
    srv = serve(chatd.make_handler(chat))
    return chat, srv.server_address[1], log


def main():
    persona = {'race': 'human', 'class': 'cleric', 'level': 3}
    tell = lambda bot, frm: {'bot': bot, 'persona': persona, 'zone': 'qeytoqrg', 'channel': 'tell', 'from': frm,
                             'text': 'hi, want to group?', 'heard': ['Botbz: inc']}

    # llm off: templates
    chat, port, log = start_chat(llm='off')
    r = post(port, '/reply', tell('Botca', 'Lanlaan'))
    check(r['source'] == 'template' and r['text'], 'llm off: a tell gets a template (%r)' % r['text'])

    # lfg interval and ooc rate
    chat, port, log = start_chat(llm='off', lfg_seconds='600', ooc_seconds='30')
    lfg = lambda bot: post(port, '/line', {'bot': bot, 'kind': 'lfg', 'channel': 'ooc',
                                           'vars': {'level': 3, 'class': 'cleric', 'zone': 'qeytoqrg'}})
    a, b = lfg('Botca'), lfg('Botca')
    check(a['text'] and not b['text'] and b['why'] == 'lfg_interval', 'lfg: once per interval per bot')
    c = lfg('Botcb')
    check(not c['text'] and c['why'] == 'ooc_rate', 'ooc: one bot line per 30 s across bots')

    # llm on, against the stub
    stub = serve(Stub)
    os.environ['ANTHROPIC_API_KEY'] = 'test-key-not-real'
    url = 'http://127.0.0.1:%d/v1/messages' % stub.server_address[1]
    chat, port, log = start_chat(llm='on', backend_url=url, calls_per_hour='3', per_bot_seconds='0')
    r1 = post(port, '/reply', tell('Botaa', 'Lanlaan'))
    check(r1['source'] == 'llm' and r1['text'] == REPLIES[0], 'llm: a normal reply passes (%r)' % r1['text'])
    r2 = post(port, '/reply', tell('Botab', 'Lanlaan'))
    check(r2['source'] == 'none' and r2['why'] == 'filtered:url', 'llm: a reply with a URL is dropped')
    r3 = post(port, '/reply', tell('Botac', 'Lanlaan'))
    check(r3['source'] == 'none' and r3['why'] == 'filtered:blocked_word', 'llm: a reply with a blocked word is dropped')
    text = open(log).read()
    check(text.count('chat=filtered') == 2, 'filtered replies are logged (chat=filtered)')
    check('hash=' in text, 'llm lines carry the prompt hash')
    sent = stub_calls[0]
    check(sent['system'][0].get('cache_control') is not None and 'quoted data' in sent['system'][0]['text'],
          'the fixed system prompt is cacheable and says player text is data')
    # burst: 20 tells within the hour, cap 3 (already used 3)
    before = len(stub_calls)
    for i in range(20):
        post(port, '/reply', tell('Bot%02d' % i, 'Lanlaan'))
    check(len(stub_calls) == before, 'calls per hour: no more LLM calls past the cap (%d made)' % (len(stub_calls) - before))
    check(open(log).read().count('why=calls_per_hour') == 20, 'capped requests fall back to templates, logged')
    # bots never answer bots
    chat2, port2, _ = start_chat(llm='on', backend_url=url, calls_per_hour='100', per_bot_seconds='0')
    post(port2, '/line', {'bot': 'Botbz', 'kind': 'inc', 'vars': {'mob': 'a_rat'}})
    before = len(stub_calls)
    r = post(port2, '/reply', tell('Botca', 'Botbz'))
    check(r['source'] == 'none' and r['why'] == 'from_bot' and len(stub_calls) == before, 'a bot gets no answer from a bot')
    # budget
    chat3, port3, log3 = start_chat(llm='on', backend_url=url, daily_budget='0', per_bot_seconds='0')
    r = post(port3, '/reply', tell('Botca', 'Lanlaan'))
    check(r['source'] == 'template' and 'chat=budget' in open(log3).read(), 'budget spent: templates only')
    # kill switch without a key
    del os.environ['ANTHROPIC_API_KEY']
    chat4, port4, _ = start_chat(llm='on', backend_url=url)
    before = len(stub_calls)
    r = post(port4, '/reply', tell('Botca', 'Lanlaan'))
    check(r['source'] == 'template' and len(stub_calls) == before, 'no key: templates only')
    print('[ OK ] chatd checks' if not failures else '[FAIL] chatd checks')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
