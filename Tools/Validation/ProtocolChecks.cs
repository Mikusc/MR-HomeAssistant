using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MRHomeAssistant.Runtime.Application.HomeAssistant;
using MRHomeAssistant.Runtime.Application.Settings;
using MRHomeAssistant.Runtime.Infrastructure.Persistence;

internal static class ProtocolChecks
{
    private static int checks;
    private static void Check(bool success, string name)
    {
        if (!success) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    public static int Main(string[] args)
    {
        using var context = new EditorContext();
        SynchronizationContext.SetSynchronizationContext(context);
        Task task = args.Contains("--serve") ? Serve() : Run();
        while (!task.IsCompleted) context.Tick();
        try { task.GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }

    private static async Task Serve()
    {
        using var server = new FakeHomeAssistant();
        Console.WriteLine("Local mock Home Assistant: " + server.Url);
        Console.WriteLine("Synthetic test credential: test-session");
        await Task.Delay(Timeout.Infinite);
    }

    private static async Task Run()
    {
        Check(HomeAssistantConnection.TryBuildWebSocketUrl("homeassistant.local:8123", out var url) &&
            url == "ws://homeassistant.local:8123/api/websocket", "normalize local address");
        Check(HomeAssistantConnection.TryBuildWebSocketUrl("https://example.org/ha/", out url) &&
            url == "wss://example.org/ha/api/websocket", "preserve reverse proxy path");
        Check(HomeAssistantConnection.TryBuildWebSocketUrl("wss://example.org/api/websocket", out url) &&
            url == "wss://example.org/api/websocket", "do not duplicate endpoint");
        foreach (string invalid in new[] { "", "ftp://example.org", "http://user:password@example.org", "http://example.org/?token=x", "http://bad host" })
            Check(!HomeAssistantConnection.TryBuildWebSocketUrl(invalid, out _), "reject invalid or credential-bearing address");

        var demo = HomeAssistantDevices.DemoStates();
        Check(HomeAssistantDevices.Filter(demo, "Lights", "").Count == 3, "lights category");
        Check(HomeAssistantDevices.Filter(demo, "All", "ROOM").Count == 3, "case-insensitive name search");
        Check(HomeAssistantDevices.Filter(demo, "All", "sensor.humidity").Count == 1, "entity ID search");
        Check(!HomeAssistantDevices.CanControl(demo.Single(s => s.state == "unavailable")), "unavailable device is read-only");
        Check(HomeAssistantDevices.StateLabel(demo.Single(s => s.entity_id == "binary_sensor.window")) == "Closed", "sensor-specific state label");
        var repository = new AppSettingsRepository();
        repository.Save(new AppSettingsData { homeAssistantUrl = "http://localhost:8123", homeAssistantToken = "DO-NOT-PERSIST", onboardingCompleted = true });
        Check(!File.ReadAllText(repository.FilePath).Contains("DO-NOT-PERSIST"), "settings never persist token");
        Check(repository.TryLoad(out var saved) && saved.onboardingCompleted && saved.homeAssistantToken == "", "settings round trip");
        var catalog = new HomeAssistantCatalog();
        catalog.LoadDemo();
        Check(catalog.Group(demo).Count == 7 && catalog.Group(demo).Single(g => g.Name == "Room climate").Entities.Count == 2, "aggregate device functions");
        Check(catalog.Group(demo, areaId: "study").Single().Primary.entity_id == "light.desk", "room filter uses registry metadata");
        Check(catalog.Group(demo, query: "Living room").Count == 2, "room name search includes inherited area");
        catalog.Load(new[] { new HomeAssistantArea { area_id = "override", name = "Override" } },
            new[] { new HomeAssistantDevice { id = "device", area_id = "original" } },
            new[] { new HomeAssistantEntity { entity_id = "light.desk", device_id = "device", area_id = "override" },
                new HomeAssistantEntity { entity_id = "light.bedroom", hidden_by = "user" } });
        Check(catalog.Group(demo).Count == 7 && catalog.Group(demo, areaId: "override").Count == 1, "entity room override and hidden entities");
        var preferencesDirectory = Path.Combine(Path.GetTempPath(), "mr-preferences-" + Guid.NewGuid());
        var preferencesRepo = new DashboardPreferencesRepository(preferencesDirectory);
        try
        {
            var profile = preferencesRepo.Load("server-a"); profile.ToggleFavorite("desk"); profile.RecordUse("desk");
            preferencesRepo.Save(profile); profile.RecordUse("other"); preferencesRepo.Save(profile);
            Check(preferencesRepo.Load("server-a").IsFavorite("desk") && preferencesRepo.Load("server-a").recent[0] == "other", "preferences atomic replacement and round trip");
            Check(!preferencesRepo.Load("server-b").IsFavorite("desk"), "favorites are server scoped");
            profile.server = "demo"; preferencesRepo.Save(profile);
            Check(!File.ReadAllText(Path.Combine(preferencesDirectory, "dashboard-preferences.json")).Contains("demo"), "demo preferences do not persist");
            for (int i = 0; i < 30; i++) profile.RecordUse(i.ToString());
            Check(profile.recent.Count == 20 && profile.recent[0] == "29", "recent list is bounded and most recent first");
            File.WriteAllText(Path.Combine(preferencesDirectory, "dashboard-preferences.json"), "invalid");
            Check(preferencesRepo.Load("server-a").favorites.Count == 0, "corrupt preferences fail safely");
        }
        finally { if (Directory.Exists(preferencesDirectory)) Directory.Delete(preferencesDirectory, true); }

        var client = new HomeAssistantConnection();
        typeof(HomeAssistantConnection).GetField("heartbeatSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(client, 1);
        client.StartDemo();
        Check(client.IsDemo && !client.IsConnected && client.States.Count == 8, "demo is explicitly isolated");
        var action = client.SetPowerAsync("light.desk", true);
        Check(client.IsPending("light.desk"), "operation exposes pending state");
        Check(await action == "" && client.TryGetCachedState("light.desk", out var state) && state.state == "on", "demo switch confirmation");
        action = client.SetPowerAsync("light.desk", false);
        client.StartDemo();
        await action;
        Check(client.TryGetCachedState("light.desk", out state) && state.state == "off" && !client.IsPending("light.desk"), "old demo operation cannot mutate a new session");
        Check(await client.SetLightAsync("light.desk", 100, 4000) == "" && client.TryGetCachedState("light.desk", out state) && state.attributes.brightness == 100, "demo capability-driven light settings");
        Check((await client.SetLightAsync("light.bedroom", 100, null)).Length > 0, "unsupported brightness is rejected");
        Check((await client.SetLightAsync("light.desk", 0, 1000)).Length > 0, "invalid brightness and temperature rejected");

        using var server = new FakeHomeAssistant();
        Check(client.Configure(server.Url, "test-session", out _), "configure test endpoint");
        Task connect = client.ConnectAndInitializeAsync();
        await client.ConnectAndInitializeAsync();
        await connect;
        Check(client.IsConnected && client.IsAuthenticated && !client.IsDemo && server.Connections == 1, "single connection on repeated connect");
        Check(client.TryGetCachedState("light.desk", out state) && state.attributes.friendly_name.Contains("\u684c\u706f"), "fragmented UTF-8 snapshot");
        Check(client.TryGetCachedState("sensor.humidity", out state) && state.state == "51", "event during snapshot is retained");
        await Task.Delay(200);
        Check(client.Catalog.IsLoaded && client.Catalog.Group(client.States, areaId: "study").Count == 1, "registry metadata loaded over WebSocket");
        string error = await client.SetPowerAsync("light.desk", true);
        Check(error == "" && client.TryGetCachedState("light.desk", out state) && state.state == "on", "real WebSocket service result plus state confirmation");
        Check(await client.SetLightAsync("light.desk", 120, 4200) == "" && client.TryGetCachedState("light.desk", out state) && state.attributes.color_temp_kelvin == 4200, "light payload and reported values confirmed");
        error = await client.SetPowerAsync("switch.denied", true);
        Check(error.Length > 0 && !client.IsPending("switch.denied"), "service rejection clears pending state");
        server.SendStateUpdates = false;
        action = client.SetPowerAsync("light.desk", false);
        await Task.Delay(200);
        Check(client.IsPending("light.desk") && client.TryGetCachedState("light.desk", out state) && state.state == "on",
            "service acceptance alone does not fake a device state");
        error = await action;
        Check(error.Length > 0 && !client.IsPending("light.desk") && client.TryGetCachedState("light.desk", out state) && state.state == "on",
            "missing state confirmation times out and preserves the reported state");
        server.SendStateUpdates = true;
        Check(server.Pings > 0 && client.IsConnected, "heartbeat pong maintains healthy connection");

        client.Disconnect();
        Check(!client.IsConnected && client.States.Count == 0, "disconnect clears stale state");
        client.Configure(server.Url, "bad-test-token", out _);
        await client.ConnectAndInitializeAsync();
        Check(!client.IsConnected && client.LastError.Contains("token"), "authentication failure is actionable");
        client.Configure(server.Url, "test-session", out _);
        connect = client.ConnectAndInitializeAsync();
        client.Disconnect();
        client.Configure(server.Url, "test-session", out _);
        Task newConnect = client.ConnectAndInitializeAsync();
        await Task.WhenAll(connect, newConnect);
        Check(client.IsConnected, "cancelled connection cannot overwrite a newer session");
        server.DropConnections();
        await Task.Delay(300);
        Check(!client.IsConnected && client.IsStale && client.IsBusy && client.LastError.Contains("lost"), "connection loss retains read-only snapshot while retrying");
        int calls = server.ServiceCalls;
        Check((await client.SetPowerAsync("light.desk", true)).Length > 0 && server.ServiceCalls == calls, "stale controls cannot send commands");
        await Task.Delay(2600);
        Check(client.IsConnected && !client.IsStale && server.ServiceCalls == calls, "automatic reconnect refreshes state without replaying commands");
        client.SetApplicationPaused(true);
        Check(client.IsStale && !client.IsConnected, "application pause suspends connection");
        client.SetApplicationPaused(false);
        await Task.Delay(400);
        Check(client.IsConnected, "application resume restores connection");
        server.DropConnections(); await Task.Delay(200);
        client.Disconnect();
        int connections = server.Connections;
        await Task.Delay(2300);
        Check(server.Connections == connections && !client.IsBusy && client.States.Count == 0, "manual disconnect cancels automatic recovery");
        server.DenyMetadata = true;
        await client.ConnectAndInitializeAsync(); await Task.Delay(200);
        Check(client.IsConnected && !client.Catalog.IsLoaded && client.MetadataStatus.Contains("unavailable"), "registry denial preserves basic controls");
        server.IgnorePings = true;
        await Task.Delay(6400);
        Check(client.IsStale && client.IsBusy, "silent connection detected by heartbeat timeout");
        server.IgnorePings = false;
        await Task.Delay(2400);
        Check(client.IsConnected, "heartbeat failure recovers without manual intervention");
        client.ForgetCredentials();
        Check(!client.HasValidConfiguration && !client.IsConnected, "forget clears session configuration");
        Console.WriteLine("Completed " + checks + " checks. UnityFacade replaces native JSON and lifecycle APIs only; this is not a Play Mode test.");
    }

    private sealed class EditorContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback, object)> queue = new();
        private bool disposed;
        public override void Post(SendOrPostCallback callback, object value)
        {
            if (disposed) return;
            try { queue.Add((callback, value)); }
            catch (ObjectDisposedException) when (disposed) { }
            catch (InvalidOperationException) when (disposed) { }
        }
        public void Tick() { if (queue.TryTake(out var item, 100)) item.Item1(item.Item2); }
        public void Dispose() { disposed = true; queue.Dispose(); }
    }

    private sealed class FakeHomeAssistant : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly ConcurrentBag<WebSocket> clients = new();
        public int Connections;
        public bool SendStateUpdates = true;
        public bool DenyMetadata;
        public int ServiceCalls;
        public int Pings;
        public bool IgnorePings;
        public string Url { get; }
        public FakeHomeAssistant()
        {
            listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(Accept);
        }

        private async Task Accept()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    TcpClient tcp = await listener.AcceptTcpClientAsync(stop.Token);
                    _ = Task.Run(() => Handle(tcp));
                }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task Handle(TcpClient tcp)
        {
            using (tcp)
            {
                try
                {
                    var stream = tcp.GetStream();
                    var header = new StringBuilder();
                    byte[] one = new byte[1];
                    while (!header.ToString().EndsWith("\r\n\r\n"))
                    {
                        if (await stream.ReadAsync(one, stop.Token) == 0 || header.Length > 8192) return;
                        header.Append((char)one[0]);
                    }
                    string key = header.ToString().Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
                    string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"), stop.Token);
                    using var ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(20));
                    clients.Add(ws);
                    Interlocked.Increment(ref Connections);
                    await Send(ws, new { type = "auth_required" });
                    using JsonDocument auth = JsonDocument.Parse(await Receive(ws));
                    bool valid = auth.RootElement.GetProperty("access_token").GetString() == "test-session";
                    await Send(ws, new { type = valid ? "auth_ok" : "auth_invalid" });
                    if (!valid) return;
                    int subscription = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        using JsonDocument request = JsonDocument.Parse(await Receive(ws));
                        var root = request.RootElement;
                        int id = root.GetProperty("id").GetInt32();
                        switch (root.GetProperty("type").GetString())
                        {
                            case "ping":
                                Interlocked.Increment(ref Pings);
                                if (!IgnorePings) await Send(ws, new { id, type = "pong" }); break;
                            case "config/area_registry/list":
                                await Send(ws, new { id, type = "result", success = !DenyMetadata, result = new[] { new { area_id = "study", name = "Study" } } }); break;
                            case "config/device_registry/list":
                                await Send(ws, new { id, type = "result", success = true, result = Array.Empty<object>() }); break;
                            case "config/entity_registry/list":
                                await Send(ws, new { id, type = "result", success = true, result = new[] { new { entity_id = "light.desk", area_id = "study" } } }); break;
                            case "subscribe_events":
                                subscription = id;
                                await Send(ws, new { id, type = "result", success = true });
                                break;
                            case "get_states":
                                await StateEvent(ws, subscription, "sensor.humidity", "51");
                                var snapshot = new
                                {
                                    id, type = "result", success = true, result = new object[]
                                    {
                                        new { entity_id = "light.desk", state = "off", attributes = LightAttributes(180, 3500) },
                                        new { entity_id = "switch.denied", state = "off", attributes = new { friendly_name = "Denied device" } },
                                        new { entity_id = "sensor.humidity", state = "48", attributes = new { friendly_name = "Humidity" } }
                                    }
                                };
                                await Send(ws, snapshot, true);
                                break;
                            case "call_service":
                                Interlocked.Increment(ref ServiceCalls);
                                string entity = root.GetProperty("target").GetProperty("entity_id").GetString();
                                bool allowed = entity != "switch.denied";
                                int brightness = 180, temperature = 3500;
                                if (root.TryGetProperty("service_data", out var data))
                                {
                                    if (data.TryGetProperty("brightness", out var b)) brightness = b.GetInt32();
                                    if (data.TryGetProperty("color_temp_kelvin", out var t)) temperature = t.GetInt32();
                                }
                                if (allowed && SendStateUpdates) await StateEvent(ws, subscription, entity, root.GetProperty("service").GetString() == "turn_on" ? "on" : "off", brightness, temperature);
                                await Send(ws, new { id, type = "result", success = allowed });
                                break;
                        }
                    }
                }
                catch (Exception) when (stop.IsCancellationRequested) { }
                catch (WebSocketException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private static object LightAttributes(int brightness, int temperature) => new { friendly_name = "Desk \u684c\u706f",
            brightness, color_temp_kelvin = temperature, supported_color_modes = new[] { "color_temp" },
            color_mode = "color_temp", min_color_temp_kelvin = 2200, max_color_temp_kelvin = 6500 };

        private Task StateEvent(WebSocket ws, int id, string entity, string value, int brightness = 180, int temperature = 3500) => Send(ws, new
        {
            id, type = "event", @event = new { event_type = "state_changed", data = new
            {
                entity_id = entity, new_state = new { entity_id = entity, state = value, attributes = entity.StartsWith("light.") ? LightAttributes(brightness, temperature) : (object)new { friendly_name = entity } }
            } }
        });

        private async Task Send(WebSocket ws, object value, bool fragment = false)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            int split = fragment ? Array.FindIndex(bytes, b => b >= 0xE0) + 1 : 0;
            if (split > 0) await ws.SendAsync(new ArraySegment<byte>(bytes, 0, split), WebSocketMessageType.Text, false, stop.Token);
            await ws.SendAsync(new ArraySegment<byte>(bytes, split, bytes.Length - split), WebSocketMessageType.Text, true, stop.Token);
        }

        private async Task<string> Receive(WebSocket ws)
        {
            byte[] buffer = new byte[16384];
            using var memory = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException();
                memory.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            return Encoding.UTF8.GetString(memory.ToArray());
        }

        public void DropConnections() { foreach (var ws in clients) ws.Abort(); }
        public void Dispose() { stop.Cancel(); listener.Stop(); DropConnections(); }
    }
}
