"""Runs asset batches once the GPU is idle, so they can be queued while something else has the card.
Idle = nobody holds the AAMT GPU lock, under 4 GB of VRAM in use and under 40% load, three checks in a row a minute
apart. Each batch runs in turn; SD is stopped afterwards so the next job gets the whole card.
Usage: python when_gpu_free.py "<command>" ["<command>" ...]   (log: _modding/item_art/gpu_queue.log)"""
import subprocess, sys, time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
LOG = HERE / "item_art" / "gpu_queue.log"
sys.path.insert(0, str(AAMT))


def log(msg):
    LOG.parent.mkdir(parents=True, exist_ok=True)
    line = f"{datetime.now():%H:%M:%S} {msg}"
    print(line, flush=True)
    with LOG.open("a", encoding="utf-8") as f: f.write(line + "\n")


def idle():
    import ai_resources
    holder = ai_resources.gpu_holder()
    if holder: return False, f"lock held by {holder.get('label')!r}"
    out = subprocess.run(["nvidia-smi", "--query-gpu=utilization.gpu,memory.used", "--format=csv,noheader,nounits"],
                         capture_output=True, text=True).stdout.strip().split(",")
    util, mem = int(out[0]), int(out[1])
    return util < 40 and mem < 4000, f"{util}% load, {mem} MiB"


def main(cmds):
    streak = 0
    while streak < 3:
        ok, why = idle()
        streak = streak + 1 if ok else 0
        log(f"{'idle' if ok else 'busy'} ({why}) {streak}/3")
        if streak < 3: time.sleep(60)
    failed = 0
    for cmd in cmds:
        log(f"run: {cmd}")
        rc = subprocess.run(cmd, shell=True, cwd=HERE).returncode
        log(f"exit {rc}: {cmd}")
        failed += rc != 0
    try:
        from sd_server_lifecycle import stop_sd_server
        stop_sd_server()
        log("SD stopped")
    except Exception as e:
        log(f"SD stop failed: {e}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main(sys.argv[1:])
