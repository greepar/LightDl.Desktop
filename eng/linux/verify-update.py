#!/usr/bin/env python3
"""Exercise the real NativeAOT helper's wait, replace, ready acknowledgement and rollback.

Uses disposable executable fixtures, so it requires neither a GUI nor an installed LightDl.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("helper", type=Path)
    args = parser.parse_args()
    helper = args.helper.resolve()
    with tempfile.TemporaryDirectory(prefix="lightdl-native-updater-") as temporary:
        root = Path(temporary)
        env = os.environ.copy()
        (root / "data").mkdir()
        env["XDG_DATA_HOME"] = str(root / "data")
        for fail in (False, True):
            case = root / ("rollback" if fail else "success")
            case.mkdir()
            target = case / "LightDl.AppImage"
            old = '#!/bin/sh\nprintf restored > "$(dirname "$0")/restored"\n'
            target.write_text(old)
            target.chmod(0o755)
            stage = case / (".lightdl-update-" + uuid.uuid4().hex)
            stage.mkdir(mode=0o700)
            candidate = stage / "Next"
            candidate.write_text("#!/bin/sh\nexit 3\n" if fail else '''#!/bin/sh
for argument in "$@"; do
  case "$argument" in
    --update-ready=*) printf 1.0.4 > "${argument#--update-ready=}" ;;
  esac
done
sleep 1
''')
            candidate.chmod(0o755)
            # A real short-lived parent verifies that replacement waits for process exit.
            parent = subprocess.Popen(["sleep", "2"])
            plan = {
                "Target": str(target), "Stage": str(stage), "Version": "1.0.4",
                "ExecutableHash": hashlib.sha256(candidate.read_bytes()).hexdigest(),
                "ParentPid": parent.pid, "IsBundle": False,
            }
            receipt = stage / "transaction.json"
            receipt.write_text(json.dumps(plan))
            installer = subprocess.Popen([str(helper), "--apply-update", str(receipt)], env=env)
            time.sleep(0.5)
            assert target.read_text() == old, "Helper replaced the executable before its parent exited"
            parent.wait(timeout=5)  # Reap the fixture so non-child process polling sees its exit.
            result_code = installer.wait(timeout=70)
            if fail:
                assert result_code == 1
                assert target.read_text() == old
                deadline = time.monotonic() + 3
                while not (case / "restored").exists() and time.monotonic() < deadline:
                    time.sleep(0.05)
                assert (case / "restored").read_text() == "restored"
                assert not (stage / "completed").exists()
            else:
                assert result_code == 0
                assert (stage / "Previous").read_text() == old
                assert (stage / "ready").read_text() == "1.0.4"
                assert (stage / "completed").read_text() == "1.0.4"
        print("Native updater verified: parent exit, replacement, restart acknowledgement and rollback.")


if __name__ == "__main__":
    main()
