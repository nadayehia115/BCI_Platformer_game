"""
Unicorn LSL streamer with live contact-quality monitor (silent background version)
----------------------------------------------------------------------------------
Started by Unity (BCIProcessManager) the moment the player has chosen a profile.

  * connects to the g.tec Unicorn through BrainFlow and broadcasts the 8 EEG channels on LSL (stream "Unicorn", type "EEG"),
  * every second judges the contact quality of each electrode and sends it to Unity on UDP (port 5010) as
        status|Fz=GOOD,C3=NOISY,Cz=GOOD,C4=GOOD,Pz=FLATLINE,PO7=GOOD,Oz=GOOD,PO8=RAILED
    (GOOD / NOISY / FLATLINE / RAILED / NO DATA) - Unity colours the 8 electrodes of the brain picture with it,
  * runs until Unity asks it to stop (UDP "quit" on port 5011) or Unity dies; it then RELEASES the headset (the Unicorn holds only
    one Bluetooth session, so a clean release matters).

No window, no viewer, no keyboard. Files 2 (recorder) and bci_game.py (live classifier) read the same LSL stream.

Options for testing without the headset:
    --simulate                      fake EEG (noise + a little alpha), all electrodes GOOD
    --simulate --sim-bad "Pz=flat,Oz=noisy"   make some electrodes bad, to see the colours in Unity
"""
import argparse
import multiprocessing
import os
import sys
import time

import numpy as np
from pylsl import StreamInfo, StreamOutlet

import bci_common as bc

# CONFIGURATION
SERIAL_NUMBER = "UN-2022.03.74"

# --- Contact-quality thresholds (per 1 s window, after detrend) ---
FLATLINE_RMS_UV = 1.0       # EEG-band (1-40 Hz) RMS below this => disconnected / flatline
MAX_EEG_RMS_UV = 100.0      # EEG-band RMS above this => gross artifact / motion
MAX_LINE_RATIO = 0.7        # (mains-band RMS / EEG-band RMS) above this => poor contact
STUCK_FRACTION = 0.5        # fraction of zero-difference samples above which => railed ADC

STREAM_START_TIMEOUT = 25.0
STREAM_STOP_TIMEOUT = 5.0


# ============================================================ the streaming process
def _parent_alive():
    parent = multiprocessing.parent_process()
    return parent is None or parent.is_alive()


def start_stream(serial_number, stop_event, status_queue, simulate, sim_bad, status_port, line_freq):
    """Background process: connect to the Unicorn via BrainFlow (or fake it), broadcast on LSL and judge the electrodes.
    The contact quality is judged right here from the very samples that are broadcast (no second LSL lookup needed), once per
    second, and sent to Unity. Reports readiness / failure to the parent through status_queue and shuts down cleanly when
    stop_event is set or when the parent process is gone."""
    channel_names = bc.EEG_CHANNELS
    sampling_rate = bc.FS
    board = None
    eeg_channels = None

    if not simulate:
        from brainflow.board_shim import BoardShim, BrainFlowInputParams, BoardIds
        BoardShim.enable_dev_board_logger()
        params = BrainFlowInputParams()
        params.serial_number = serial_number
        board_id = BoardIds.UNICORN_BOARD
        try:
            board = BoardShim(board_id, params)
            board.prepare_session()
            board.start_stream()
        except Exception as e:
            try:
                status_queue.put(("error", f"{type(e).__name__}: {e}"))
            except Exception:
                pass
            try:
                if board is not None and board.is_prepared():
                    board.release_session()
            except Exception:
                pass
            return
        eeg_channels = BoardShim.get_eeg_channels(board_id)
        channel_names = BoardShim.get_eeg_names(board_id)
        sampling_rate = BoardShim.get_sampling_rate(board_id)

    info = StreamInfo("Unicorn", "EEG", len(channel_names), sampling_rate, "float32", serial_number)
    chns = info.desc().append_child("channels")
    for label in channel_names:
        ch = chns.append_child("channel")
        ch.append_child_value("label", label)
        ch.append_child_value("unit", "microvolts")
        ch.append_child_value("type", "EEG")
    outlet = StreamOutlet(info)

    try:
        status_queue.put(("ready", {"rate": sampling_rate, "channels": list(channel_names), "simulated": simulate}))
    except Exception:
        pass
    bc.log(f"Unicorn {'(SIMULATED) ' if simulate else ''}connected: {len(channel_names)} channels at {sampling_rate} Hz -> LSL")

    rng = np.random.default_rng(1)
    t_sim = 0
    last = time.perf_counter()
    window = np.zeros((int(sampling_rate), len(channel_names)))        # the last second of every channel
    window_fill = 0
    arrived = 0                                                         # samples since the last status
    next_status = time.perf_counter() + 1.5
    try:
        while not stop_event.is_set() and _parent_alive():
            if simulate:
                now = time.perf_counter()
                n = int((now - last) * sampling_rate)
                if n > 0:
                    last += n / sampling_rate
                    t = (t_sim + np.arange(n)) / sampling_rate
                    t_sim += n
                    data = rng.normal(0.0, 6.0, (len(channel_names), n)) + 8.0 * np.sin(2 * np.pi * 10.0 * t)
                    for i, name in enumerate(channel_names):
                        kind = sim_bad.get(name)
                        if kind == "flat":
                            data[i] = 0.0
                        elif kind == "noisy":
                            data[i] = rng.normal(0.0, 300.0, n) + 120.0 * np.sin(2 * np.pi * bc.LINE_FREQ * t)
                    outlet.push_chunk(data.T.tolist())
                    window, window_fill, arrived = _slide(window, window_fill, data.T), min(len(window), window_fill + n), arrived + n
            else:
                data = board.get_board_data()
                if data.shape[1] > 0:
                    chunk = data[eeg_channels, :].T
                    outlet.push_chunk(chunk.tolist())
                    window, window_fill, arrived = (_slide(window, window_fill, chunk), min(len(window), window_fill + len(chunk)),
                                                    arrived + len(chunk))
            if time.perf_counter() >= next_status:
                next_status += 1.0
                send_status(window, window_fill, arrived, channel_names, sampling_rate, status_port, line_freq)
                arrived = 0
            time.sleep(0.02)
    finally:
        if board is not None:
            try:
                board.stop_stream()
            except Exception:
                pass
            try:
                board.release_session()
            except Exception:
                pass
        bc.log("BrainFlow session released.")


# ============================================================ contact quality
def _slide(window, fill, chunk):
    """Push a chunk (n x channels) into the 1 s rolling window."""
    n = len(chunk)
    if n >= len(window):
        return np.array(chunk[-len(window):], dtype=float)
    out = np.roll(window, -n, axis=0)
    out[-n:] = chunk
    return out


def send_status(window, fill, arrived, channels, fs, status_port, line_freq):
    """Judge each electrode on the last second and send it to Unity (UDP) as  status|Fz=GOOD,C3=NOISY,..."""
    if fill < fs * 0.5 or arrived == 0:
        payload = "status|" + ",".join(f"{c}=NO DATA" for c in channels)
    else:
        parts = []
        for i, ch in enumerate(channels):
            status, _ = assess_channel(window[:, i], fs, line_freq)
            if arrived < 0.7 * fs and status == "GOOD":
                status = "NOISY"                                 # too few samples this second: the link is unstable
            parts.append(f"{ch}={status}")
        payload = "status|" + ",".join(parts)
    bc.udp_send(payload, status_port)
    bc.log(payload, f"({arrived} samples)")


def assess_channel(raw, fs, line_freq):
    """Judge one channel from a short window of RAW samples (microvolts). Returns (status, metrics).
    Raw Unicorn data is dominated by DC offset and drift, so the window is DETRENDED first; then the 1-40 Hz power is compared with
    the mains-band power: a poorly seated electrode acts as an antenna, so a high mains / EEG ratio means "not on properly"."""
    from brainflow.data_filter import DataFilter, FilterTypes, DetrendOperations
    raw = np.ascontiguousarray(raw, dtype=np.float64)
    n = raw.size
    m = {"eeg_rms": 0.0, "line_rms": 0.0, "line_ratio": 0.0, "stuck_frac": 1.0}
    if n < int(fs * 0.5):
        return "NO DATA", m
    m["stuck_frac"] = float(np.mean(np.abs(np.diff(raw)) < 1e-6))

    work = raw.copy()
    DataFilter.detrend(work, DetrendOperations.LINEAR.value)
    eeg = work.copy()
    DataFilter.perform_bandpass(eeg, fs, 1.0, 40.0, 4, FilterTypes.BUTTERWORTH.value, 0)
    eeg_rms = float(np.sqrt(np.mean(eeg ** 2)))
    line = work.copy()
    DataFilter.perform_bandpass(line, fs, line_freq - 2.0, line_freq + 2.0, 4, FilterTypes.BUTTERWORTH.value, 0)
    line_rms = float(np.sqrt(np.mean(line ** 2)))

    m["eeg_rms"], m["line_rms"] = eeg_rms, line_rms
    m["line_ratio"] = line_rms / (eeg_rms + 1e-9)
    if not (np.isfinite(eeg_rms) and np.isfinite(line_rms)):
        return "NO DATA", m
    if m["stuck_frac"] > STUCK_FRACTION:
        return "RAILED", m
    if eeg_rms < FLATLINE_RMS_UV:
        return "FLATLINE", m
    if m["line_ratio"] > MAX_LINE_RATIO or eeg_rms > MAX_EEG_RMS_UV:
        return "NOISY", m
    return "GOOD", m


def wait_for_quit(control_sock, stream_process):
    """The parent has nothing else to do: wait until Unity says quit (or the stream process dies)."""
    while stream_process.is_alive():
        for text, _ in bc.udp_poll(control_sock):
            if text.lower() == "quit":
                return
        time.sleep(0.2)
    bc.log("the stream process ended by itself")


# ============================================================ main
def main():
    bc.stdout_utf8()
    parser = argparse.ArgumentParser()
    parser.add_argument("--serial", default=SERIAL_NUMBER)
    parser.add_argument("--line-freq", type=float, default=bc.LINE_FREQ)
    parser.add_argument("--status-port", type=int, default=bc.PORT_STATUS)
    parser.add_argument("--control-port", type=int, default=bc.PORT_CONTROL)
    parser.add_argument("--simulate", action="store_true", help="fake EEG, for testing without the headset")
    parser.add_argument("--sim-bad", default="", help='e.g. "Pz=flat,Oz=noisy" (only with --simulate)')
    args = parser.parse_args()

    sim_bad = {}
    for item in args.sim_bad.split(","):
        if "=" in item:
            k, v = item.split("=", 1)
            sim_bad[k.strip()] = v.strip().lower()

    stop_event = multiprocessing.Event()
    status_queue = multiprocessing.Queue()
    stream_process = multiprocessing.Process(
        target=start_stream, args=(args.serial, stop_event, status_queue, args.simulate, sim_bad, args.status_port, args.line_freq),
        daemon=True)
    control_sock = None
    try:
        control_sock = bc.udp_listener(args.control_port)
        stream_process.start()
        try:
            kind, payload = status_queue.get(timeout=STREAM_START_TIMEOUT)
        except Exception:
            kind, payload = "error", "timed out waiting for the Unicorn to start"
        if kind == "error":
            bc.udp_send(f"error|{payload}", args.status_port)
            bc.log("ERROR: could not start the Unicorn stream:", payload)
            return
        bc.udp_send("ready|", args.status_port)
        bc.log("stream up:", payload)
        wait_for_quit(control_sock, stream_process)
    except KeyboardInterrupt:
        pass
    finally:
        stop_event.set()
        stream_process.join(timeout=STREAM_STOP_TIMEOUT)
        if stream_process.is_alive():
            bc.log("stream did not stop in time; forcing termination")
            stream_process.terminate()
            stream_process.join(2)
        if control_sock is not None:
            control_sock.close()
        bc.log("stopped; headset released.")


if __name__ == "__main__":
    multiprocessing.freeze_support()
    main()
