$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw 'Install uv (https://docs.astral.sh/uv/getting-started/installation/) and run this script again.' }
    $env:UV_CACHE_DIR = Join-Path $projectRoot '.tools/uv-cache'
    $env:UV_PYTHON_BIN_DIR = Join-Path $projectRoot '.tools/python-bin'
    & uv python install 3.12.16 --install-dir .tools/python
    if ($LASTEXITCODE -ne 0) { throw 'Python download failed.' }
    $runtime = Join-Path $projectRoot '.tools/python/cpython-3.12.16-windows-x86_64-none/python.exe'
    $python = Join-Path $projectRoot '.tools/qwen-asr-env/Scripts/python.exe'
    & uv venv --python $runtime --allow-existing .tools/qwen-asr-env
    if ($LASTEXITCODE -ne 0) { throw 'Python environment creation failed.' }
    & uv pip install --python $python 'torch==2.10.0+cpu' 'torchaudio==2.10.0+cpu' --index-url https://download.pytorch.org/whl/cpu
    if ($LASTEXITCODE -ne 0) { throw 'CPU PyTorch installation failed.' }
    & uv pip install --python $python -r workers/qwen_asr/requirements.lock.txt
    if ($LASTEXITCODE -ne 0) { throw 'Qwen dependencies installation failed.' }
    & uv pip install --python $python 'sherpa-onnx==2.0.1'
    if ($LASTEXITCODE -ne 0) { throw 'sherpa-onnx installation failed.' }
    & $python -c "import torch; import qwen_asr; import sherpa_onnx; print('ASR runtimes ready:', torch.__version__, sherpa_onnx.__version__)"
    if ($LASTEXITCODE -ne 0) { throw 'Qwen import verification failed.' }
    Write-Host "Python: $python"
    Write-Host 'Models: download Qwen3-ASR and sherpa-onnx streaming Zipformer separately; see README.'
} finally { Pop-Location }
