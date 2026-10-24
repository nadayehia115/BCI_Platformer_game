"""Starts the game (when a published build exists) and controls it over UDP, and shows the keys on the screen.

    python bci_game.py                 opens Build/<game>.exe if there is one, otherwise talks to the game running in Unity
    python bci_game.py --exe "C:\\path\\Game.exe"
    python bci_game.py --no-launch     only talk to a game that is already running (Unity editor Play mode)
    python bci_game.py --no-window     no key display window (console only)

Keys (real key state, read system-wide, so they work whichever window is in front and letting go stops the player):
    hold D / Right arrow   run right          hold A / Left arrow   run left
    W / Space / Up arrow   jump
    S  at an obstacle (a pool, a tree, a box): the dark fountain animation plays, and then the flickering pad appears
    T  the classification says the player kept the focus on the pad: the game brings in the friend the obstacle needs by
       itself (water -> Noelle, a tree -> Ralsei, a box -> Susie). That uses the pad up: press S again at the next obstacle.
       Nothing happens before S or when no such obstacle is in sight
A window shows W A S D T lighting up while they are pressed, and the list of messages this script sends to the game (so it can
be seen that Python is controlling Unity). It also has the buttons below. In the console window these letters work too:
    R  run the game: opens the published game, or presses Play in the Unity editor
    Q  close the game (stops Play mode in Unity, or closes the published game); R runs it again
    X  exit this script (also closes the game if the script opened it)

The same GameLink class is what the BCI / ML code should use: link.switch(), link.move(1), link.jump() ...
(link.toggle() and link.confirm() are still there for the two-step version: highlight the next one, then bring it in.)
link.listeners is a list of functions that are called with every message that is sent (the window uses it for its list).

Holding a key in Unity means "down until released". Over UDP that is done by repeating the command: while move() is
non-zero a background thread resends "move|<x>" every 50 ms, and move(0) stops it. The game treats the control as held
while those keep arriving and stops by itself 0.3 s after they stop, so a crashed script can't leave the player running.
"""
import argparse
import collections
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


def describe(command):
    """A message that is sent to the game, in words: (the key, what it does), or None for messages that are not worth showing."""
    names = {
        "move|1": ("D", "run right"),
        "move|-1": ("A", "run left"),
        "move|0": ("-", "key let go: stop"),
        "jump": ("W", "jump"),
        "s": ("S", "dark fountain, then the pad appears"),
        "switch": ("T", "bring in the friend the obstacle needs"),
        "quit": ("Q", "close the game"),
    }
    if command in names:
        return names[command]
    if command in ("ping", "frequencies"):
        return None
    return ("-", command)


class KeyWatcher:
    """Follows the real state of the keys (down / up), so letting go of D stops the player.

    A console only receives typed characters, which can't tell when a key is released. Windows can be asked whether a key
    is down right now (GetAsyncKeyState), so a small thread polls A/D (or the arrow keys), W/Space, S and T and tells the game.
    It reads those keys system-wide, so it also works while the game window is in front; the game getting the same key
    directly at the same time is harmless (same direction, a jump is only allowed from the ground, and the fountain and the
    power ignore a second request).
    """
    LEFT = (0x41, 0x25)        # A, Left arrow
    RIGHT = (0x44, 0x27)       # D, Right arrow
    JUMP = (0x57, 0x20, 0x26)  # W, Space, Up arrow
    FOUNTAIN = (0x53,)         # S
    SWITCH = (0x54,)           # T
    POLL_SECONDS = 0.01
    # how long (s) a key stays lit in the window after it was pressed, so that a short tap can be seen
    LIT_SECONDS = {"W": 0.35, "A": 0.15, "S": 0.7, "D": 0.15, "T": 0.7}

    def __init__(self, link, is_down=None):
        import ctypes
        self.link = link
        self.is_down = is_down or (lambda vk: bool(ctypes.windll.user32.GetAsyncKeyState(vk) & 0x8000))
        self._running = True
        self._direction = 0
        self._jump_was_down = False
        self._fountain_was_down = False
        self._switch_was_down = False
        self._lit_until = {}
        threading.Thread(target=self._loop, daemon=True).start()

    def stop(self):
        self._running = False

    def is_lit(self, name):
        """True while the key (W, A, S, D or T) is down, and a moment after, for the key display."""
        return time.time() < self._lit_until.get(name, 0.0)

    def _light(self, name):
        self._lit_until[name] = time.time() + self.LIT_SECONDS[name]

    def poll(self):
        left = any(self.is_down(k) for k in self.LEFT)
        right = any(self.is_down(k) for k in self.RIGHT)
        if left:
            self._light("A")
        if right:
            self._light("D")
        direction = (1 if right else 0) - (1 if left else 0)        # both held = stand still, like the keyboard
        if direction != self._direction:
            self._direction = direction
            self.link.move(direction)                              # 0 when the key is let go
        jump = any(self.is_down(k) for k in self.JUMP)
        if jump:
            self._light("W")
        if jump and not self._jump_was_down:                       # once per press, not while held
            self.link.jump()
        self._jump_was_down = jump

        fountain = any(self.is_down(k) for k in self.FOUNTAIN)
        if fountain:
            self._light("S")
        if fountain and not self._fountain_was_down:
            self.link.fountain()
        self._fountain_was_down = fountain

        switch = any(self.is_down(k) for k in self.SWITCH)
        if switch:
            self._light("T")
        if switch and not self._switch_was_down:
            self.link.switch()
        self._switch_was_down = switch

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
        self.listeners = []                 # called with every message that is sent (not the repeated "move" and the pings)
        self.last_pong = 0.0                # when the game last answered
        self._ready = threading.Event()
        self._freq_event = threading.Event()
        self._freq = None
        self._move = 0
        self._running = True
        threading.Thread(target=self._receive, daemon=True).start()
        threading.Thread(target=self._heartbeat, daemon=True).start()

    # ---- connection ----
    @property
    def connected(self):
        """True while the game has answered a ping in the last 3 s (start_status_pings() keeps asking)."""
        return time.time() - self.last_pong < 3.0

    def start_status_pings(self):
        """Ping the game once a second, so `connected` stays up to date (used by the key display window)."""
        def loop():
            while self._running:
                try:
                    self._send("ping", log=False)
                except OSError:
                    return
                time.sleep(1.0)
        threading.Thread(target=loop, daemon=True).start()

    def is_up(self, timeout=0.5):
        """True if the game is running and answers right now."""
        self._ready.clear()
        self._send("ping", log=False)
        return self._ready.wait(timeout)

    def wait_until_ready(self, timeout=60.0):
        """UDP does not queue: anything sent before the game is listening is lost, so ping until it answers."""
        self._ready.clear()
        end = time.time() + timeout
        while time.time() < end and not self._ready.is_set():
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError("The game closed before it was ready.")
            self._send("ping", log=False)
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
    def fountain(self):
        """The "S" of the BCI flow: at an obstacle that needs a friend's power (water, a tree, a box) the game plays the dark
        fountain animation where Kris stands, and when it is over the flickering pad appears. Nothing happens when no such
        obstacle is in sight, while the animation plays, or while the pad is already out. Needed at EVERY obstacle."""
        self._send("s")

    def switch(self):
        """The "t" of the BCI: the classification (next stage) showed that the player kept the required focus. The game
        then picks the power the nearest obstacle needs and brings that friend in: water -> Noelle, a tree -> Ralsei,
        a box -> Susie. Only works after fountain() has played at the obstacle (the pad is out); it uses the pad up.
        Nothing happens before that or when no such obstacle is in sight."""
        self._send("switch")

    def frequencies(self, timeout=1.0):
        """The flicker rate (Hz) of the one SSVEP pad ("activate power", in the middle of the game screen), as a list with one
        number, e.g. [6.0] on a 60 Hz screen. The game works it out from the screen it runs on (never 8-25 Hz), or uses the
        one chosen in the main menu SETTINGS tab. None if the game does not answer."""
        self._freq_event.clear()
        self._send("frequencies", log=False)
        return self._freq if self._freq_event.wait(timeout) else None

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
        self.last_pong = 0.0
        print("The game is closed.")

    def close(self):
        self._running = False
        try:
            self._send("move|0", log=False)
        except OSError:
            pass
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
        self.sock.close()

    # ---- internals ----
    def _send(self, text, log=True):
        self.sock.sendto(text.encode(), UNITY_ADDR)
        if log:
            for listener in list(self.listeners):
                listener(text)

    def _heartbeat(self):
        while self._running:
            if self._move != 0:
                try:
                    self._send(f"move|{self._move}", log=False)
                except OSError:
                    return
            time.sleep(HEARTBEAT_SECONDS)

    def _receive(self):
        while self._running:
            try:
                data, _ = self.sock.recvfrom(1024)
            except OSError:
                return
            text = data.decode()
            if text.startswith("pong"):
                self.last_pong = time.time()
                self._ready.set()
            elif text.startswith("freq|"):
                self._freq = [float(x) for x in text.split("|")[1:]]
                self._freq_event.set()


class KeyDisplay:
    """A window that shows W A S D T lighting up while they are pressed and the list of messages sent to the game, so that
    anybody watching can see that the Python script is controlling Unity. Everything on it is drawn from the main thread; the
    other threads only fill the lists it reads."""
    BACK = "#14181f"
    PANEL = "#1f2630"
    KEY_OFF = "#2d3745"
    KEY_ON = "#5df0ff"
    TEXT = "#e8eef5"
    DIM = "#8a98a8"
    GOOD = "#6ee07a"
    BAD = "#ff6b6b"
    # name: (column, row, caption)
    KEYS = {"W": (1, 0, "jump"), "A": (0, 1, "left"), "S": (1, 1, "fountain"), "D": (2, 1, "right"), "T": (3.4, 1, "power")}
    CAP = 92
    GAP = 10

    def __init__(self, link, watcher, on_run, on_quit_game, on_exit):
        import tkinter as tk
        self.tk = tk
        self.link = link
        self.watcher = watcher
        self.on_exit = on_exit
        self._log = collections.deque(maxlen=9)
        self._log_lock = threading.Lock()
        link.listeners.append(self._on_command)
        link.start_status_pings()

        self.root = tk.Tk()
        self.root.title("BCI Busters - Python keyboard control")
        self.root.configure(bg=self.BACK)
        self.root.resizable(False, False)
        self.root.attributes("-topmost", True)
        self.root.protocol("WM_DELETE_WINDOW", on_exit)

        tk.Label(self.root, text="Python  \u2192  Unity", font=("Segoe UI", 20, "bold"), fg=self.KEY_ON, bg=self.BACK).pack(pady=(12, 0))
        self.status = tk.Label(self.root, text="", font=("Segoe UI", 12), bg=self.BACK)
        self.status.pack()

        width = 4 * (self.CAP + self.GAP) + 90
        self.canvas = tk.Canvas(self.root, width=width, height=2 * (self.CAP + self.GAP) + 10, bg=self.BACK, highlightthickness=0)
        self.canvas.pack(padx=16, pady=(8, 0))
        self.caps = {}
        for name, (col, row, caption) in self.KEYS.items():
            x = 12 + col * (self.CAP + self.GAP)
            y = 4 + row * (self.CAP + self.GAP)
            rect = self.canvas.create_rectangle(x, y, x + self.CAP, y + self.CAP, fill=self.KEY_OFF, outline="#46556a", width=2)
            letter = self.canvas.create_text(x + self.CAP / 2, y + self.CAP / 2 - 10, text=name, font=("Segoe UI", 34, "bold"), fill=self.TEXT)
            word = self.canvas.create_text(x + self.CAP / 2, y + self.CAP - 18, text=caption, font=("Segoe UI", 11), fill=self.DIM)
            self.caps[name] = (rect, letter, word)

        tk.Label(self.root, text="sent to the game", font=("Segoe UI", 11), fg=self.DIM, bg=self.BACK, anchor="w").pack(fill="x", padx=18, pady=(8, 0))
        self.log_text = tk.Label(self.root, text="", font=("Consolas", 11), fg=self.TEXT, bg=self.PANEL, anchor="nw", justify="left",
                                 width=66, height=9, padx=8, pady=6)
        self.log_text.pack(padx=16, pady=(2, 8))

        buttons = tk.Frame(self.root, bg=self.BACK)
        buttons.pack(pady=(0, 12))
        for label, action in (("Run game (R)", on_run), ("Close game (Q)", on_quit_game), ("Exit (X)", on_exit)):
            tk.Button(buttons, text=label, command=action, font=("Segoe UI", 10), bg=self.PANEL, fg=self.TEXT, activebackground=self.KEY_OFF,
                      activeforeground=self.TEXT, relief="flat", padx=12, pady=4).pack(side="left", padx=6)

        self._shown_log = None
        self._shown_status = None

    def _on_command(self, command):
        meaning = describe(command)
        if meaning is None:
            return
        key, what = meaning
        line = f"{time.strftime('%H:%M:%S')}  {key:<2}  {what:<40}{command}"
        with self._log_lock:
            self._log.append(line)

    def _tick(self):
        for name, (rect, letter, word) in self.caps.items():
            lit = self.watcher.is_lit(name)
            self.canvas.itemconfig(rect, fill=self.KEY_ON if lit else self.KEY_OFF)
            self.canvas.itemconfig(letter, fill="#06121a" if lit else self.TEXT)
            self.canvas.itemconfig(word, fill="#06303a" if lit else self.DIM)
        with self._log_lock:
            text = "\n".join(reversed(self._log)) or "(nothing sent yet: press W A S D or T)"
        if text != self._shown_log:
            self._shown_log = text
            self.log_text.config(text=text)
        connected = self.link.connected
        if connected != self._shown_status:
            self._shown_status = connected
            self.status.config(text="the game answers: connected" if connected else "the game does not answer (is it running?)",
                               fg=self.GOOD if connected else self.BAD)
        self.root.after(25, self._tick)

    def run(self):
        self._tick()
        self.root.mainloop()

    def close(self):
        try:
            self.root.destroy()
        except Exception:
            pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", help="path of the published game (default: first .exe in Build/)")
    parser.add_argument("--no-launch", action="store_true", help="do not start the game, it is already running")
    parser.add_argument("--no-window", action="store_true", help="do not show the key display window")
    args = parser.parse_args()

    exe = None if args.no_launch else (args.exe or find_game_exe())
    link = GameLink(exe)
    keys = KeyWatcher(link)
    stop = threading.Event()

    def start_game():
        try:
            print("Opens a published build from Build/ if there is one, otherwise presses Play in the Unity editor.")
            if exe or link.is_up(1.0):
                link.run_game()                      # opens the build / already running in Unity: just connects
            else:
                print("The game is not running. Press R (or the Run game button) to run it.")
        except Exception as error:                  # e.g. the game closed again before it was ready
            print(error)

    def run_game():
        threading.Thread(target=link.run_game, daemon=True).start()

    def quit_game():
        threading.Thread(target=link.quit_game, daemon=True).start()

    def console_loop():
        while not stop.is_set():
            key = msvcrt.getwch().lower()           # R / Q / X typed in this console window
            if key == "r":
                run_game()
            elif key == "q":
                quit_game()
            elif key in ("x", "\x03"):              # X or Ctrl+C
                stop.set()

    display = None
    try:
        if not args.no_window:
            try:
                display = KeyDisplay(link, keys, run_game, quit_game, stop.set)
            except Exception as error:              # no tkinter: the console is enough
                print(f"No key display window ({error}).")
        threading.Thread(target=start_game, daemon=True).start()
        threading.Thread(target=console_loop, daemon=True).start()
        print("Hold D/A (or the arrows) to run and let go to stop, W/Space to jump, S = dark fountain at the obstacle (then the pad "
              "appears), T = focus kept (the game brings in the friend the obstacle needs). These keys work whichever window is in front.")
        print("In this console window: R run the game, Q close the game, X exit this script.")
        if display is not None:
            def watch_stop():                        # X in the console (another thread) closes the window
                if stop.is_set():
                    display.close()
                else:
                    display.root.after(100, watch_stop)
            display.root.after(100, watch_stop)
            display.run()
        else:
            while not stop.is_set():
                time.sleep(0.1)
    except KeyboardInterrupt:
        pass
    finally:
        keys.stop()
        link.close()
        if display is not None:
            display.close()


if __name__ == "__main__":
    main()
