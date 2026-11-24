import json, re, io, os
from opencc import OpenCC

SRC = r'D:\Projects\AGCleaner'
FIX = os.path.join(SRC, 'tools', 'lang_fix.json')
DUMP = os.path.join(SRC, 'shots', 'lang_dump.txt')

cc = OpenCC('s2twp')

def unesc(s):
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            d = s[i + 1]
            if d == 'n':
                out.append('\n'); i += 2; continue
            if d == 't':
                out.append('\t'); i += 2; continue
            if d == 'r':
                out.append(''); i += 2; continue
            if d == '\\':
                out.append('\\'); i += 2; continue
            if d == '"':
                out.append('"'); i += 2; continue
        out.append(c); i += 1
    return ''.join(out)

def cs(s):
    """C# 字符串字面量（不换行转义用 \\n）。"""
    e = json.dumps(s, ensure_ascii=False)
    return e

def esc_key(s):
    return s.replace('\r', '').replace('\n', '\\n').replace('\t', '\\t')

# ---------- 1. LangFix.cs：97 条缺失词条 ----------
fix = json.load(open(FIX, encoding='utf-8'))
rows = []
for zh, tr in fix.items():
    assert len(tr) == 5, zh
    rows.append([unesc(zh)] + tr)
# 校验：与清单里的键完全一致
missing = [l.rstrip('\n') for l in open(os.path.join(SRC, 'shots', 'missing_keys.txt'), encoding='utf-8') if l.strip()]
keys_fix = set(unesc(k) for k in fix.keys())
miss_real = set(unesc(k) for k in missing)
diff1 = miss_real - keys_fix
diff2 = keys_fix - miss_real
print('fix keys:', len(keys_fix), 'missing list:', len(miss_real))
print('未覆盖:', [esc_key(x) for x in diff1])
print('多余:', [esc_key(x) for x in diff2])

body = []
body.append('using System;\nusing System.Collections.Generic;\n\nnamespace AGCleaner\n{')
body.append('    /// <summary>补齐各语言缺失的界面词条（中文原文为键）。</summary>')
body.append('    public static class LangFix\n    {')
body.append('        private static readonly string[][] Rows = new string[][]\n        {')
for r in rows:
    body.append('            new string[] { ' + ', '.join(cs(x) for x in r) + ' },')
body.append('        };')
body.append('')
body.append('        public static void Fill(Dictionary<string, string> en, Dictionary<string, string> de,')
body.append('            Dictionary<string, string> fr, Dictionary<string, string> ru, Dictionary<string, string> es)')
body.append('        {')
body.append('            for (int i = 0; i < Rows.Length; i++)')
body.append('            {')
body.append('                string[] r = Rows[i];')
body.append('                if (!en.ContainsKey(r[0])) en[r[0]] = r[1];')
body.append('                if (!de.ContainsKey(r[0])) de[r[0]] = r[2];')
body.append('                if (!fr.ContainsKey(r[0])) fr[r[0]] = r[3];')
body.append('                if (!ru.ContainsKey(r[0])) ru[r[0]] = r[4];')
body.append('                if (!es.ContainsKey(r[0])) es[r[0]] = r[5];')
body.append('            }')
body.append('        }')
body.append('    }')
body.append('}')
open(os.path.join(SRC, 'src', 'LangFix.cs'), 'w', encoding='utf-8').write('\n'.join(body) + '\n')

# ---------- 2. LangHant.cs：全部词条的繁体表 ----------
master = set()
for line in open(DUMP, encoding='utf-8'):
    if line.startswith('key\t') or not line.strip():
        continue
    master.add(unesc(line.split('\t', 1)[0]))
master |= keys_fix
# 再加简体缺失键（已含）；过滤空键
master = sorted(m for m in master if m.strip())
print('hant master keys:', len(master))

hrows = []
POST = {'許可權': '權限', '禁用': '停用', '登錄': '登入'}
for zh in master:
    h = cc.convert(zh)
    for a, b in POST.items():
        h = h.replace(a, b)
    hrows.append((zh, h))

h = []
h.append('using System;\nusing System.Collections.Generic;\n\nnamespace AGCleaner\n{')
h.append('    /// <summary>简体中文 → 繁體中文（台灣用語）界面词表，由简体词表整体转换生成。</summary>')
h.append('    public static class LangHant\n    {')
h.append('        private static readonly string[][] Rows = new string[][]\n        {')
for zh, tw in hrows:
    h.append('            new string[] { ' + cs(zh) + ', ' + cs(tw) + ' },')
h.append('        };')
h.append('')
h.append('        public static Dictionary<string, string> Table()')
h.append('        {')
h.append('            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);')
h.append('            for (int i = 0; i < Rows.Length; i++)')
h.append('            {')
h.append('                if (!d.ContainsKey(Rows[i][0])) d[Rows[i][0]] = Rows[i][1];')
h.append('            }')
h.append('            return d;')
h.append('        }')
h.append('    }')
h.append('}')
open(os.path.join(SRC, 'src', 'LangHant.cs'), 'w', encoding='utf-8').write('\n'.join(h) + '\n')
print('written LangFix.cs / LangHant.cs')
