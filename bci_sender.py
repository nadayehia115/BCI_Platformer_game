import msvcrt  # Windows-only; reads a key without needing Enter
import socket
import statistics
import threading
import time

UNITY_ADDR = ("127.0.0.1", 5005)

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(("127.0.0.1", 0))  # Unity's ack comes back to this same socket

sent_at = {}      # seq -> perf_counter() at send time
latencies = []    # round-trip times in ms


def receive_acks():
    while True:
        try:
            data, _ = sock.recvfrom(1024)
        except OSError:
            return
        now = time.perf_counter()
        parts = data.decode().split("|")
        if parts[0] != "ack":
            continue
        seq = int(parts[1])
        queue_ms = parts[2] if len(parts) > 2 else "?"
        t0 = sent_at.pop(seq, None)
        if t0 is None:
            continue
        rtt = (now - t0) * 1000
        latencies.append(rtt)
        print(f"  #{seq}: round trip {rtt:.2f} ms  (waited {queue_ms} ms for Unity's next frame)")


threading.Thread(target=receive_acks, daemon=True).start()

print("Press T to toggle to the next profile. Q to quit.")
seq = 0
while True:
    key = msvcrt.getwch().lower()
    if key == "t":
        seq += 1
        sent_at[seq] = time.perf_counter()
        sock.sendto(f"toggle|{seq}".encode(), UNITY_ADDR)
        print(f"sent: toggle #{seq}")
    elif key == "q":
        break

if latencies:
    print(f"\n{len(latencies)} measurements: "
          f"mean {statistics.mean(latencies):.2f} ms, "
          f"min {min(latencies):.2f} ms, max {max(latencies):.2f} ms")
