using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Listens for UDP messages from the Python BCI script and drives the CharacterSwitcher.
// Message format: "toggle" or "toggle|<seq>". With a seq, an "ack|<seq>|<queueMs>" reply is sent
// back right after the toggle is applied, so the sender can measure end-to-end latency.
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
            string[] parts = p.text.Split('|');
            if (parts[0] != "toggle") continue;

            switcher.Select(switcher.Current + 1);

            // Time the packet sat in the queue waiting for this frame's Update.
            double queueMs = (Stopwatch.GetTimestamp() - p.receivedTicks) * 1000.0 / Stopwatch.Frequency;
            Debug.Log($"BciListener toggle applied (waited {queueMs:0.00} ms for the frame)");

            if (parts.Length > 1)
            {
                byte[] ack = Encoding.UTF8.GetBytes($"ack|{parts[1]}|{queueMs:0.00}");
                client.Send(ack, ack.Length, p.from);
            }
        }
    }

    void OnDestroy()
    {
        running = false;
        client?.Close();
        thread?.Join(200);
    }
}
