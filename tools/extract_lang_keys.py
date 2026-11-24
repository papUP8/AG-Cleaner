import os, re, json, sys

SRC = r'D:\Projects\AGCleaner\src'
OUT = r'D:\Projects\AGCleaner\shots\keys.txt'

pat = re.compile(r'Lang\.(?:T|F)\(\s*"(?P<raw>(?:[^"\\]|\\.)*)"')

def unescape(s):
    out = []
    i = 0
    n = len(s)
    while i < n:
        c = s[i]
        if c == '\\' and i + 1 < n:
            d = s[i+1]
            if d == 'n':
                out.append('\n'); i += 2; continue
            if d == 't':
                out.append('\t'); i += 2; continue
            if d == 'r':
                out.append(''); i += 2; continue
            if d == '"':
                out.append('"'); i += 2; continue
            if d == '\\':
                out.append('\\'); i += 2; continue
            if d == 'u' and i + 5 < n:
                try:
                    out.append(chr(int(s[i+2:i+6], 16))); i += 6; continue
                except ValueError:
                    pass
            out.append(c); i += 1; continue
        out.append(c)
        i += 1
    return ''.join(out)

keys = set()
for root, dirs, files in os.walk(SRC):
    for fn in files:
        if not fn.endswith('.cs'):
            continue
        if fn.startswith('Lang'):
            continue
        text = open(os.path.join(root, fn), encoding='utf-8-sig').read()
        for m in pat.finditer(text):
            real = unescape(m.group('raw'))
            if any(ord(ch) > 127 for ch in real):
                keys.add(real)

def esc(s):
    return s.replace('\r', '').replace('\n', '\\n').replace('\t', '\\t')

lines = sorted(esc(k) for k in keys)
open(OUT, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('unique keys:', len(lines))
