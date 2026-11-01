
import sys as _vc_log_sys
import re as _vc_log_re


class _VcEssentialOutput(object):
    _vc_essential_output = True

    def __init__(self, original):
        self.original = original
        self.pending = ''
        self.softspace = 0
        self.traceback = False

    def write(self, text):
        self.pending += text
        while '\n' in self.pending:
            line, self.pending = self.pending.split('\n', 1)
            self.emit(line)

    def emit(self, line):
        text = line.strip()
        if text.startswith('Traceback ('):
            self.traceback = True
        if self.traceback:
            self.original.write(line + '\n')
            if text and not line[:1].isspace() and not text.startswith('Traceback ('):
                self.traceback = False
            return
        replacements = (
            ('[CENTRAL VCMX GNN COMMAND]', '[GNN COMMAND]'),
            ('[CENTRAL RULE COMMAND RECEIVED]', '[RULE COMMAND]'),
            ('[ASRS AUTO STORAGE]', '[ASRS STORAGE]'),
        )
        for old, new in replacements:
            if text.startswith(old):
                self.original.write(text.replace(old, new, 1) + '\n')
                return

        header = text.split(']', 1)[0] if text.startswith('[') else text
        if (_vc_log_re.search(r'\b(ERROR|WARNING|FAILED|EXCEPTION|FATAL|FALLBACK)\b', header)
                or _vc_log_re.match(r'\w*(Error|Exception):', text)
                or u'\uc624\ub958' in text):
            self.original.write(line + '\n')

    def flush(self):
        if self.pending:
            self.emit(self.pending)
            self.pending = ''
        self.original.flush()

    def __getattr__(self, name):
        return getattr(self.original, name)


if not getattr(_vc_log_sys.stdout, '_vc_essential_output', False):
    _vc_log_sys.stdout = _VcEssentialOutput(_vc_log_sys.stdout)
