#!/usr/bin/env bash
# Diagnose why the PythonAnalysis conda environment is not offered as a notebook kernel in VS Code,
# and register it explicitly, which is the fix that does not depend on VS Code discovering it.
#
#   bash Analysis/check_kernel.sh

set -uo pipefail
ENV_NAME="${1:-PythonAnalysis}"

echo "=== 1. does conda know the environment? ==="
if ! command -v conda >/dev/null 2>&1; then
    echo "   conda is not on PATH in this shell."
    echo "   VS Code discovers environments through the same PATH, so this alone can explain it."
    echo "   Fix: run 'conda init zsh' (or bash), open a new terminal, and try again."
    exit 1
fi

conda env list | sed 's/^/   /'

ENV_PREFIX="$(conda env list | awk -v n="$ENV_NAME" '$1==n {print $NF}')"
if [ -z "$ENV_PREFIX" ]; then
    echo
    echo "   No environment named '$ENV_NAME'."
    echo "   Fix: conda env create -f Analysis/environment.yml"
    exit 1
fi
echo
echo "   '$ENV_NAME' lives at: $ENV_PREFIX"

PY="$ENV_PREFIX/bin/python"
if [ ! -x "$PY" ]; then
    echo "   No python at $PY — the environment looks incomplete."
    exit 1
fi

echo
echo "=== 2. are the packages actually in THAT environment? ==="
"$PY" - <<'PYEOF'
import importlib, sys
print(f"   interpreter: {sys.executable}")
print(f"   python     : {sys.version.split()[0]}")
missing = []
for mod in ("ipykernel", "matplotlib", "numpy", "pandas", "scipy"):
    try:
        m = importlib.import_module(mod)
        print(f"   {mod:12s} {getattr(m, '__version__', 'ok')}")
    except ImportError:
        print(f"   {mod:12s} MISSING")
        missing.append(mod)
sys.exit(1 if "ipykernel" in missing else 0)
PYEOF

if [ $? -ne 0 ]; then
    echo
    echo "   ipykernel is missing from the environment, which is why VS Code will not list it."
    echo "   Fix: conda install -n $ENV_NAME -c conda-forge ipykernel"
    exit 1
fi

echo
echo "=== 3. is a kernelspec registered? ==="
"$PY" -m jupyter kernelspec list 2>/dev/null | sed 's/^/   /' || echo "   (no jupyter client in this env, which is fine)"

echo
echo "=== 4. registering the kernelspec explicitly ==="
# This writes ~/Library/Jupyter/kernels/<name>/kernel.json pointing at this exact interpreter.
# VS Code reads that directly, so it works even when environment auto-discovery does not.
"$PY" -m ipykernel install --user \
    --name "$(echo "$ENV_NAME" | tr '[:upper:]' '[:lower:]')" \
    --display-name "Python ($ENV_NAME)" | sed 's/^/   /'

echo
echo "=== done ==="
echo "In VS Code:"
echo "   1. Open Analysis/Notebooks/explore_recordings.ipynb"
echo "   2. Click the kernel picker, top right"
echo "   3. Select Another Kernel...  ->  Jupyter Kernel...  ->  Python ($ENV_NAME)"
echo
echo "The first list VS Code shows is only suggestions. 'Select Another Kernel...' is the step"
echo "that is easy to miss."
echo
echo "If it is still absent, paste this interpreter path into the Python: Select Interpreter"
echo "command (Cmd+Shift+P), using 'Enter interpreter path...':"
echo "   $PY"
