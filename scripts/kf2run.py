"""Launch the port, drive it through KF2_SHELL, harvest its stdout.

Every empirical claim in docs/ comes from a run like this, and until this module
existed each one was made with throwaway glue that did not survive the session.
The measurements are only worth as much as their reproducibility, so the driving
lives here rather than in a scratch file.

Four things in here are scar tissue and are the reason the module is worth having
at all:

  * Never `pkill -f` on a pattern that also matches the calling shell's own
    command line -- the harness kills itself, and the symptom is a run that
    reports nothing rather than an error.  `stop()` matches on the executable and
    excludes this process tree.
  * `state.inGame` goes true the moment the save is loaded, but pressing a button
    then lands during an area transition and is swallowed.  `wait_in_game()`
    therefore has a settle delay, defaulting to ten seconds.
  * Probes report per-second windows.  The first window is short and the last is
    truncated, so a scenario has to say which windows it means rather than taking
    the first or the mean of all of them.
  * The port needs a display.  These runs open a real window; they are not
    headless, and a session without one will fail at launch rather than hang.
"""

from __future__ import annotations

import json
import os
import re
import signal
import socket
import subprocess
import time
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
EXE_MATCH = "bin/Release/net10.0/Deception"
PORT = 27900


class ShellError(RuntimeError):
    pass


def shell(cmd: str, timeout: float = 6.0) -> dict:
    """One request, one single-line JSON response, one connection."""
    with socket.create_connection(("127.0.0.1", PORT), timeout) as s:
        s.settimeout(timeout)
        s.sendall((cmd + "\n").encode())
        buf = b""
        while not buf.endswith(b"\n"):
            chunk = s.recv(4096)
            if not chunk:
                break
            buf += chunk
    text = buf.decode().strip()
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        raise ShellError(f"{cmd!r} -> {text[:200]!r}") from e


def running() -> list[int]:
    out = subprocess.run(["pgrep", "-f", EXE_MATCH], capture_output=True, text=True).stdout
    mine = {os.getpid(), os.getppid()}
    return [int(p) for p in out.split() if p.isdigit() and int(p) not in mine]


def stop(grace: float = 3.0) -> None:
    """Kill every instance of the port, never the caller. See the module note."""
    for pid in running():
        try:
            os.kill(pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    deadline = time.time() + grace
    while time.time() < deadline and running():
        time.sleep(0.2)
    for pid in running():
        try:
            os.kill(pid, signal.SIGKILL)
        except ProcessLookupError:
            pass


@dataclass
class Run:
    log: Path
    env: dict[str, str] = field(default_factory=dict)

    def lines(self) -> list[str]:
        try:
            return self.log.read_text(errors="replace").splitlines()
        except FileNotFoundError:
            return []

    def matching(self, pattern: str) -> list[re.Match]:
        rx = re.compile(pattern)
        return [m for m in (rx.search(l) for l in self.lines()) if m]


def launch(env: dict[str, str], log: Path, extra_args: list[str] | None = None) -> Run:
    """Start the port detached, with KF2_SHELL and KF2_AUTOSTART already set."""
    stop()
    log.parent.mkdir(parents=True, exist_ok=True)
    if log.exists():
        log.unlink()

    full = dict(os.environ)
    full.update({"KF2_SHELL": "1", "KF2_AUTOSTART": "2"})
    full.update(env)

    cmd = [
        "dotnet", "run", "--project", "Deception.csproj",
        "-c", "Release", "--no-build", "--", "disc/KingsField2.cue",
    ] + (extra_args or [])

    with log.open("wb") as fh:
        subprocess.Popen(cmd, cwd=REPO, env=full, stdout=fh, stderr=subprocess.STDOUT,
                         stdin=subprocess.DEVNULL, start_new_session=True)
    return Run(log=log, env=dict(env))


def wait_in_game(boot_timeout: float = 120.0, settle: float = 10.0) -> dict:
    """Block until an area is up, then let it settle. Returns the state block.

    `inGame` is `MaxHp != 0`, which goes true as the save loads -- before the area
    transition has finished. Pressing a button in that window does nothing, so the
    settle is not optional.
    """
    deadline = time.time() + boot_timeout
    while time.time() < deadline:
        try:
            st = shell("state").get("state", {})
            if st.get("inGame"):
                time.sleep(settle)
                return st
        except (OSError, ShellError):
            pass
        time.sleep(1.0)
    raise TimeoutError(f"never reached an area within {boot_timeout:.0f}s")


def press(button: str, hold_ms: int = 150) -> dict:
    return shell(f"press {button} {hold_ms}")


def hold(button: str, seconds: float, hold_ms: int | None = None) -> None:
    """Assert a button for `seconds`, then wait for it to be released."""
    ms = hold_ms if hold_ms is not None else int(seconds * 1000)
    press(button, ms)
    time.sleep(seconds + 0.5)


def state() -> dict:
    return shell("state").get("state", {})


def nearby() -> dict:
    return shell("nearby")


def fmt_env(env: dict[str, str]) -> str:
    return " ".join(f"{k}={v}" for k, v in sorted(env.items())) or "(defaults)"
