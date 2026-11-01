
import argparse
import os
import subprocess
import sys
import uuid


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--gnn-dir', required=True)
    p.add_argument('--data', required=True)
    p.add_argument('--output', required=True)
    p.add_argument('--tag', required=True)
    p.add_argument('--epochs', type=int, default=200)
    a = p.parse_args()
    if a.epochs < 1:
        p.error('--epochs must be positive')
    gnn = os.path.abspath(a.gnn_dir)
    out = os.path.abspath(a.output)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    temporary = out + '.' + uuid.uuid4().hex + '.tmp'
    env = dict(os.environ, PYTHONIOENCODING='utf-8', PYTHONUNBUFFERED='1',
               OMP_NUM_THREADS='2', MKL_NUM_THREADS='2')
    try:
        subprocess.run([sys.executable, os.path.join(gnn, 'train.py'),
                        '--data', os.path.abspath(a.data), '--out', temporary,
                        '--epochs', str(a.epochs)], env=env, check=True)
        sys.path.insert(0, gnn)
        from dispatch import Dispatcher
        Dispatcher(temporary, device='cpu')
        if os.path.exists(out):
            import shutil
            shutil.copy2(out, out + '.' + uuid.uuid4().hex + '.bak')
        os.replace(temporary, out)
        print('published LkGnn checkpoint:', out)
    finally:
        if os.path.exists(temporary):
            os.remove(temporary)


if __name__ == '__main__':
    main()
