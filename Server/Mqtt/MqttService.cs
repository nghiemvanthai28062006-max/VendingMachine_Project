using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using Server.Infrastructure;
using Server.Models;
using Server.Services;

namespace Server.Mqtt;

// Singleton: chạy nền (kết nối broker) và là IDeviceNotifier (để Service publish xuống Device).
// Service/Repository là Scoped nên chỉ lấy qua IServiceScopeFactory, mỗi message một scope riêng.
public class MqttService : BackgroundService, IDeviceNotifier
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MqttService> _log;
    private IManagedMqttClient? _client;

    public MqttService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<MqttService> log)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = _config["Mqtt:Host"] ?? "localhost";
        var port = _config.GetValue("Mqtt:Port", 1883);
        var user = _config["Mqtt:Username"];
        var pass = _config["Mqtt:Password"];
        var clientId = _config["Mqtt:ClientId"] ?? "vending-server";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            _log.LogError("Thiếu Mqtt:Username hoặc Mqtt:Password trong appsettings.Local.json, MQTT bị tắt");
            return;   // REST vẫn chạy bình thường
        }

        var client = new MqttFactory().CreateManagedMqttClient();
        client.ApplicationMessageReceivedAsync += OnMessageAsync;
        client.ConnectedAsync += OnConnectedAsync;
        client.DisconnectedAsync += e =>
        {
            _log.LogWarning("Mất kết nối broker MQTT ({Reason})", e.Reason);
            return Task.CompletedTask;
        };
        client.ConnectingFailedAsync += e =>
        {
            _log.LogWarning("Không kết nối được broker MQTT: {Message}", e.Exception?.Message);
            return Task.CompletedTask;
        };

        var options = new ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithClientOptions(new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .WithClientId(clientId)
                .WithCredentials(user, pass)
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .Build())
            .Build();

        _client = client;

        // ManagedMqttClient tự subscribe lại sau mỗi lần kết nối lại
        await client.SubscribeAsync(new[]
        {
            Filter("vending/+/heartbeat", MqttQualityOfServiceLevel.AtMostOnce),
            Filter("vending/+/status", MqttQualityOfServiceLevel.AtLeastOnce),
            Filter("vending/+/order/create", MqttQualityOfServiceLevel.AtLeastOnce),
            Filter("vending/+/payment/callback", MqttQualityOfServiceLevel.AtLeastOnce),
            Filter("vending/+/dispense/started", MqttQualityOfServiceLevel.AtLeastOnce),
            Filter("vending/+/dispense/result", MqttQualityOfServiceLevel.AtLeastOnce)
        });
        await client.StartAsync(options);
        _log.LogInformation("MQTT: đang kết nối tới {Host}:{Port}", host, port);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        await client.StopAsync();
    }

    // ---------- IDeviceNotifier ----------
    public async Task PublishTraysAsync(int machineId)
    {
        if (_client is null) return;   // MQTT bị tắt

        TraysMessage? message;
        using (var scope = _scopeFactory.CreateScope())
        {
            var device = scope.ServiceProvider.GetRequiredService<DeviceService>();
            message = await device.BuildTraysMessageAsync(machineId);
        }
        if (message is null) return;

        await EnqueueAsync($"vending/{message.MachineCode}/trays", message, retain: true);
    }

    public Task PublishOrderStatusAsync(string machineCode, OrderStatusMessage message) =>
        EnqueueAsync($"vending/{machineCode}/order/status", message, retain: false);

    public Task PublishDispenseCommandAsync(string machineCode, DispenseCommandMessage message) =>
        EnqueueAsync($"vending/{machineCode}/dispense/command", message, retain: false);

    private Task EnqueueAsync(string topic, object payload, bool retain)
    {
        var client = _client;
        if (client is null) return Task.CompletedTask;

        var builder = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), JsonDefaults.Options))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
        if (retain) builder = builder.WithRetainFlag();

        return client.EnqueueAsync(builder.Build());
    }

    // ---------- nhận ----------
    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _log.LogInformation("Đã kết nối broker MQTT");
        try
        {
            IReadOnlyList<int> ids;
            using (var scope = _scopeFactory.CreateScope())
            {
                ids = await scope.ServiceProvider.GetRequiredService<DeviceService>().GetAllMachineIdsAsync();
            }
            foreach (var id in ids) await PublishTraysAsync(id);
            _log.LogInformation("Đã publish trays cho {Count} máy", ids.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Lỗi khi publish trays sau khi kết nối");
        }
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var parts = topic.Split('/');
            if (parts.Length < 3 || parts[0] != "vending") return;

            var machineCode = parts[1];
            var action = string.Join('/', parts.Skip(2));
            var payload = e.ApplicationMessage.ConvertPayloadToString();

            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            switch (action)
            {
                case "heartbeat":
                    await sp.GetRequiredService<DeviceService>().RecordHeartbeatAsync(machineCode);
                    break;

                case "status":
                    // LWT có dạng {"online": false}; tin khác thì bỏ qua
                    if (IsOfflineNotice(payload))
                        await sp.GetRequiredService<DeviceService>().MarkDisconnectedAsync(machineCode);
                    break;

                case "order/create":
                    if (Parse<CreateOrderMessage>(payload, topic) is { } create)
                        await sp.GetRequiredService<OrderService>().CreateOrderAsync(machineCode, create);
                    break;

                case "payment/callback":
                    if (Parse<PaymentCallbackMessage>(payload, topic) is { } callback)
                        await sp.GetRequiredService<OrderService>().PaymentCallbackAsync(machineCode, callback);
                    break;

                case "dispense/started":
                    if (Parse<DispenseStartedMessage>(payload, topic) is { } started)
                        await sp.GetRequiredService<OrderService>().DispenseStartedAsync(machineCode, started);
                    break;

                case "dispense/result":
                    if (Parse<DispenseResultMessage>(payload, topic) is { } result)
                        await sp.GetRequiredService<OrderService>().DispenseResultAsync(machineCode, result);
                    break;

                default:
                    _log.LogDebug("Chưa xử lý topic {Topic}", topic);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Không để một message hỏng làm sập Server (File 02, mục 3)
            _log.LogError(ex, "Lỗi khi xử lý message MQTT");
        }
    }

    private T? Parse<T>(string? payload, string topic) where T : class
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(payload))
                return JsonSerializer.Deserialize<T>(payload, JsonDefaults.Options);
        }
        catch (JsonException) { }
        _log.LogWarning("Payload sai định dạng ở topic {Topic}", topic);
        return null;
    }

    private static bool IsOfflineNotice(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("online", out var online)
                   && online.ValueKind == JsonValueKind.False;
        }
        catch (JsonException) { return false; }
    }

    private static MQTTnet.Packets.MqttTopicFilter Filter(string topic, MqttQualityOfServiceLevel qos) =>
        new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(qos).Build();
}