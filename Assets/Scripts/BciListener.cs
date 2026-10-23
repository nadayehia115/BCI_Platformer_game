using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Listens for UDP messages from the Python script and drives the CharacterSwitcher and the player (via RemoteInput).
// Messages: toggle | toggle|<seq> | select|<slot> | confirm | s (fountain) | switch (t) | move|<-1..1> | jump | frequencies | ping | quit
//   toggle|<seq> is answered with "ack|<seq>|<queueMs>" right after it is applied, so the sender can measure latency.
//   ping is answered with "pong" so the sender knows the game is up.
[RequireComponent(typeof(CharacterSwitcher))]
public class BciListener : MonoBehaviour
{
    struct Packet
    {
        public string text;
        public IPEndPoint from;
        public long receivedTicks;   // Stopwatch timestamp when the socket thread got it
    }

    [SerializeField] int port = 5005;

    CharacterSwitcher switcher;
    UdpClient client;
    Thread thread;
    volatile bool running;
    readonly ConcurrentQueue<Packet> queue = new ConcurrentQueue<Packet>();

    void Awake()
    {
        switcher = GetComponent<CharacterSwitcher>();
        // Keep Update running while another window (the Python terminal) has focus.
        Application.runInBackground = true;
    }

    void Start()
    {
        try
        {
            // ReuseAddress lets us rebind if a previous Play session left the port open.
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (SocketException e)
        {
            Debug.LogError($"BciListener could not bind UDP port {port}: {e.Message}. Change the port or close whatever uses it.");
            enabled = false;
            return;
        }
        running = true;
        thread = new Thread(Listen) { IsBackground = true };
        thread.Start();
        Debug.Log($"BciListener listening on UDP port {port}");
    }

    void Listen()
    {
        while (running)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref ep);
                queue.Enqueue(new Packet
                {
                    text = Encoding.UTF8.GetString(data),
                    from = ep,
                    receivedTicks = Stopwatch.GetTimestamp()
                });
            }
            catch (SocketException) { }
            catch (System.ObjectDisposedException) { }
        }
    }

    // Unity API calls must run on the main thread, so messages are handled here.
    void Update()
    {
        while (queue.TryDequeue(out Packet p))
        {
            string[] parts = p.text.Trim().Split('|');
            switch (parts[0])
            {
                case "toggle":
                    switcher.Select(switcher.Current + 1);

                    // Time the packet sat in the queue waiting for this frame's Update.
                    double queueMs = (Stopwatch.GetTimestamp() - p.receivedTicks) * 1000.0 / Stopwatch.Frequency;
                    Debug.Log($"BciListener toggle applied (waited {queueMs:0.00} ms for the frame)");

                    if (parts.Length > 1) Reply(p, $"ack|{parts[1]}|{queueMs:0.00}");
                    break;

                case "select":                       // select|<slot>: highlight a specific character (0 = first)
                    if (parts.Length > 1 && int.TryParse(parts[1], out int slot)) switcher.Select(slot);
                    break;

                case "confirm":                      // same as pressing Z
                    switcher.Confirm();
                    break;

                case "s":                            // the Python S: play the dark fountain at the obstacle, then the pad appears
                case "fountain":
                    KrisForms.Request();
                    break;

                case "switch":                       // the Python T: the classification kept the focus, bring in the friend the obstacle needs
                    switcher.AutoSelect();
                    break;

                case "move":                         // move|-1, move|0, move|1: repeat it while the button is "held"
                    if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
                        RemoteInput.SetHorizontal(x);
                    break;

                case "jump":
                    RemoteInput.RequestJump();
                    break;

                case "frequencies":                  // the flicker rates of the SSVEP pads, left to right (Ralsei, Noelle, Susie): freq|6|7.5|5
                    var reply = new System.Text.StringBuilder("freq");
                    foreach (float f in SsvepBoard.Frequencies) reply.Append('|').Append(f.ToString(CultureInfo.InvariantCulture));
                    Reply(p, reply.ToString());
                    break;

                case "ping":                         // lets the sender wait until the game has started listening
                    Reply(p, "pong");
                    break;

                case "quit":                         // the sender is done: stop Play mode in the editor, close a published game
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }
    }

    void Reply(Packet p, string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        client.Send(data, data.Length, p.from);
    }

    void OnDestroy()
    {
        running = false;
        client?.Close();
        thread?.Join(200);
    }
}
