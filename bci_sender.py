import msvcrt  # Windows-only; reads a key without needing Enter
import socket

UNITY_ADDR = ("127.0.0.1", 5005)
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

print("Press T to toggle to the next profile. Q to quit.")
while True:
    key = msvcrt.getwch().lower()
    if key == "t":
        sock.sendto(b"toggle", UNITY_ADDR)
        print("sent: toggle")
    elif key == "q":
        break
