using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Listens for UDP messages from the Python BCI script and drives the CharacterSwitcher.
[RequireComponent(typeof(CharacterSwitcher))]
public class BciListener : MonoBehaviour
{
    [SerializeField] int port = 5005;

    CharacterSwitcher switcher;
    UdpClient client;
    Thread thread;
    volatile bool running;
    readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();

    void Awake() => switcher = GetComponent<CharacterSwitcher>();

    void Start()
    {
        client = new UdpClient(port);
        running = true;
        thread = new Thread(Listen) { IsBackground = true };
        thread.Start();
        Debug.Log($"BciListener listening on UDP port {port}");
    }

    void Listen()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try { queue.Enqueue(Encoding.UTF8.GetString(client.Receive(ref ep))); }
            catch (SocketException) { }
            catch (System.ObjectDisposedException) { }
        }
    }

    // Unity API calls must run on the main thread, so messages are handled here.
    void Update()
    {
        while (queue.TryDequeue(out string msg))
        {
            Debug.Log($"BciListener received: {msg}");
            if (msg == "toggle") switcher.Select(switcher.Current + 1);
        }
    }

    void OnDestroy()
    {
        running = false;
        client?.Close();
        thread?.Join(200);
    }
}
