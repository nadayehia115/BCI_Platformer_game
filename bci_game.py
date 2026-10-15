"""Starts the game (when a published build exists) and controls it over UDP.

    python bci_game.py                 opens Build/<game>.exe if there is one, otherwise talks to the game running in Unity
    python bci_game.py --exe "C:\\path\\Game.exe"
    python bci_game.py --no-launch     only talk to a game that is already running (Unity editor Play mode)

Movement (real key state, so letting go stops the player; works whichever window is in front):
    hold D / Right arrow   run right          hold A / Left arrow   run left
    W / Space / Up arrow   jump
One-off commands (type them in this console window):
    T  next character (toggle)      Z  confirm (bring the highlighted character in / out)
    Q  quit: closes the game too (stops Play mode in Unity, or closes the published game)

The same GameLink class is what the BCI / ML code should use: link.toggle(), link.confirm(), link.move(1), link.jump() ...

Holding a key in Unity means "down until released". Over UDP that is done by repeating the command: while move() is
non-zero a background thread resends "move|<x>" every 50 ms, and move(0) stops it. The game treats the control as held
while those keep arriving and stops by itself 0.3 s after they stop, so a crashed script can't leave the player running.
"""
import argparse
import glob
import msvcrt  # Windows-only; reads a key without needing Enter
import os
import socket
import subprocess
import threading
import time

UNITY_ADDR = ("127.0.0.1", 5005)
HERE = os.path.dirname(os.path.abspath(__file__))
HEARTBEAT_SECONDS = 0.05        # how often "move" is repeated while a direction is held


def find_game_exe():
    """First .exe in Build/ next to this script (the Unity crash handler is skipped)."""
    for path in sorted(glob.glob(os.path.join(HERE, "Build", "*.exe"))):
        if not os.path.basename(path).lower().startswith("unitycrashhandler"):
            return path
    return None


class KeyWatcher:
    """Follows the real state of the movement keys (down / up), so letting go of D stops the player.

    A console only receives typed characters, which can't tell when a key is released. Windows can be asked whether a key
    is down right now (GetAsyncKeyState), so a small thread polls A/D (or the arrow keys) and W/Space and tells the game.
    It reads those keys system-wide, so it also works while the game window is in front; the game getting the same key
    directly at the same time is harmless (same direction, and a jump is only allowed from the ground).
    """
    LEFT = (0x41, 0x25)        # A, Left arrow
    RIGHT = (0x44, 0x27)       # D, Right arrow
    JUMP = (0x57, 0x20, 0x26)  # W, Space, Up arrow
    POLL_SECONDS = 0.01

    def __init__(self, link, is_down=None):
        import ctypes
        self.link = link
        self.is_down = is_down or (lambda vk: bool(ctypes.windll.user32.GetAsyncKeyState(vk) & 0x8000))
        self._running = True
        self._direction = 0
        self._jump_was_down = False
        threading.Thread(target=self._loop, daemon=True).start()

    def stop(self):
        self._running = False

    def poll(self):
        left = any(self.is_down(k) for k in self.LEFT)
        right = any(self.is_down(k) for k in self.RIGHT)
        direction = (1 if right else 0) - (1 if left else 0)        # both held = stand still, like the keyboard
        if direction != self._direction:
            self._direction = direction
            self.link.move(direction)                              # 0 when the key is let go
        jump = any(self.is_down(k) for k in self.JUMP)
        if jump and not self._jump_was_down:                       # once per press, not while held
            self.link.jump()
        self._jump_was_down = jump

    def _loop(self):
        while self._running:
            self.poll()
            time.sleep(self.POLL_SECONDS)


class GameLink:
    def __init__(self, exe=None):
        self.process = subprocess.Popen([exe], cwd=os.path.dirname(exe)) if exe else None
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self._ready = threading.Event()
        self._move = 0
        self._running = True
        threading.Thread(target=self._receive, daemon=True).start()
        threading.Thread(target=self._heartbeat, daemon=True).start()

    # ---- connection ----
    def wait_until_ready(self, timeout=60.0):
        """UDP does not queue: anything sent before the game is listening is lost, so ping until it answers."""
        end = time.time() + timeout
        while time.time() < end and not self._ready.is_set():
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError("The game closed before it was ready.")
            self._send("ping")
            self._ready.wait(0.5)
        return self._ready.is_set()

    # ---- commands ----
    def toggle(self):
        self._send("toggle")

    def select(self, slot):
        self._send(f"select|{slot}")

    def confirm(self):
        self._send("confirm")

    def jump(self):
        self._send("jump")

    def move(self, direction):
        """-1 left, 0 stop, 1 right. Stays in effect until changed."""
        self._move = max(-1, min(1, int(direction)))
        self._send(f"move|{self._move}")

    def quit_game(self):
        """Ask the game to close: stops Play mode in the Unity editor, closes a published build."""
        self.move(0)
        self._send("quit")

    def close(self):
        self._running = False
        try:
            self._send("move|0")
        except OSError:
            pass
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
        self.sock.close()

    # ---- internals ----
    def _send(self, text):
        self.sock.sendto(text.encode(), UNITY_ADDR)

    def _heartbeat(self):
        while self._running:
            if self._move != 0:
                try:
                    self._send(f"move|{self._move}")
                except OSError:
                    return
            time.sleep(HEARTBEAT_SECONDS)

    def _receive(self):
        while self._running:
            try:
                data, _ = self.sock.recvfrom(1024)
            except OSError:
                return
            if data.decode().startswith("pong"):
                self._ready.set()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", help="path of the published game (default: first .exe in Build/)")
    parser.add_argument("--no-launch", action="store_true", help="do not start the game, it is already running")
    args = parser.parse_args()

    exe = None if args.no_launch else (args.exe or find_game_exe())
    if exe:
        print(f"Opening the game: {exe}")
    else:
        print("No published build found in Build/ (or --no-launch given): talking to a game that is already running.")

    link = GameLink(exe)
    try:
        print("Waiting for the game to start listening...")
        if not link.wait_until_ready():
            print("The game did not answer. Is GameScene running (Unity Play mode, or the build past the main menu)?")
            return
        keys = KeyWatcher(link)
        print("Connected. Hold D/A (or the arrows) to run and let go to stop, W/Space to jump.")
        print("In this window: T toggle, Z confirm, Q quit (also closes the game).")
        while True:
            key = msvcrt.getwch().lower()           # one-off commands: typed in this console window
            if key == "t":
                link.toggle()
            elif key == "z":
                link.confirm()
            elif key == "q":
                link.quit_game()
                time.sleep(0.2)                     # let the message leave before the socket is closed
                break
    finally:
        link.close()


if __name__ == "__main__":
    main()
