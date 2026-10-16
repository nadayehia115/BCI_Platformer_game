"""Starts the game (when a published build exists) and controls it over UDP.

    python bci_game.py                 opens Build/<game>.exe if there is one, otherwise talks to the game running in Unity
    python bci_game.py --exe "C:\\path\\Game.exe"
    python bci_game.py --no-launch     only talk to a game that is already running (Unity editor Play mode)

Movement (real key state, so letting go stops the player; works whichever window is in front):
    hold D / Right arrow   run right          hold A / Left arrow   run left
    W / Space / Up arrow   jump
One-off commands (type them in this console window):
    T  the one switch: hides whoever is out and brings the next character in (Ralsei, Noelle, Susie, Ralsei ...)
    R  run the game: opens the published game, or presses Play in the Unity editor
    Q  close the game (stops Play mode in Unity, or closes the published game); R runs it again
    X  exit this script (also closes the game if the script opened it)

The same GameLink class is what the BCI / ML code should use: link.switch(), link.move(1), link.jump() ...
(link.toggle() and link.confirm() are still there for the two-step version: highlight the next one, then bring it in.)

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

UNITY_ADDR = ("127.0.0.1", 5005)    # the game (BciListener)
EDITOR_ADDR = ("127.0.0.1", 5006)   # the Unity editor itself (EditorRemote), so "play" works before the game is running
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
        self.exe = exe                      # published game to open with run_game(); None = the Unity editor
        self.process = None
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self._ready = threading.Event()
        self._move = 0
        self._running = True
        threading.Thread(target=self._receive, daemon=True).start()
        threading.Thread(target=self._heartbeat, daemon=True).start()

    # ---- connection ----
    def is_up(self, timeout=0.5):
        """True if the game is running and answers right now."""
        self._ready.clear()
        self._send("ping")
        return self._ready.wait(timeout)

    def wait_until_ready(self, timeout=60.0):
        """UDP does not queue: anything sent before the game is listening is lost, so ping until it answers."""
        self._ready.clear()
        end = time.time() + timeout
        while time.time() < end and not self._ready.is_set():
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError("The game closed before it was ready.")
            self._send("ping")
            self._ready.wait(0.5)
        return self._ready.is_set()

    def run_game(self, timeout=60.0):
        """Start the game and wait until it listens: opens the published .exe, or presses Play in the Unity editor."""
        if self.is_up():
            print("The game is already running.")
            return True
        if self.exe:
            print(f"Opening the game: {self.exe}")
            self.process = subprocess.Popen([self.exe], cwd=os.path.dirname(self.exe))
            ok = self.wait_until_ready(timeout)
        else:
            print("Pressing Play in the Unity editor...")
            ok = False
            end = time.time() + timeout
            while not ok and time.time() < end:
                # the editor stops listening for a few seconds while scripts reload, so ask again every 2 s;
                # a "play" that arrives once Play mode has started is ignored
                self.sock.sendto(b"play", EDITOR_ADDR)
                ok = self.wait_until_ready(2.0)
        print("The game is running." if ok else
              "The game did not answer. (From the build: press Play at the main menu. From Unity: is the editor open on this project?)")
        return ok

    # ---- commands ----
    def switch(self):
        """One button for both jobs: send out whoever is on screen and bring the next character in."""
        self._send("switch")

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
        """Ask the game to close: stops Play mode in the Unity editor, closes a published build. run_game() starts it again."""
        self.move(0)
        self._send("quit")
        end = time.time() + 3.0
        while time.time() < end and self.is_up(0.3):         # wait until it has really gone
            time.sleep(0.1)
        self._ready.clear()
        print("The game is closed.")

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
    link = GameLink(exe)
    keys = None
    try:
        print("Opens a published build from Build/ if there is one, otherwise presses Play in the Unity editor.")
        if exe or link.is_up(1.0):
            link.run_game()                          # opens the build / already running in Unity: just connects
        else:
            print("The game is not running. Press R to run it.")
        keys = KeyWatcher(link)
        print("Hold D/A (or the arrows) to run and let go to stop, W/Space to jump.")
        print("In this window: T switch character, R run the game, Q close the game, X exit this script.")
        while True:
            key = msvcrt.getwch().lower()           # one-off commands: typed in this console window
            if key == "t":
                link.switch()
            elif key == "r":
                link.run_game()
            elif key == "q":
                link.quit_game()
            elif key in ("x", "\x03"):              # X or Ctrl+C
                break
    finally:
        if keys is not None:
            keys.stop()
        link.close()


if __name__ == "__main__":
    main()
