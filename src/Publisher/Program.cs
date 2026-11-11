using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MachineTelemetry.Edge;
using MQTTnet;
using MQTTnet.Protocol;

var simulate = args is ["simulate"];
var sensor = args is ["sensor", _] or ["sensor", _, "--once"];
if (!simulate && !sensor)
{
    Console.Error.WriteLine("Usage: Publisher simulate | sensor <w1_slave path> [--once]");
    return 2;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
var cancellation = stop.Token;
var machineId = Environment.GetEnvironmentVariable("MACHINE_ID") ?? "pump-01";
var host = Environment.GetEnvironmentVariable("MQTT_HOST") ?? "127.0.0.1";
var tls = Environment.GetEnvironmentVariable("MQTT_TLS") == "1";
var portSetting = Environment.GetEnvironmentVariable("MQTT_PORT") ?? (tls ? "8883" : "1883");
var caFile = Environment.GetEnvironmentVariable("MQTT_CA_FILE");
var certFile = Environment.GetEnvironmentVariable("MQTT_CLIENT_CERT");
var keyFile = Environment.GetEnvironmentVariable("MQTT_CLIENT_KEY");
if (machineId.Length is < 1 or > 32 || machineId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
    !int.TryParse(portSetting, out var port) || port is < 1 or > 65535 ||
    (!tls && host is not ("127.0.0.1" or "localhost" or "::1")) ||
    (certFile is null) != (keyFile is null) || (!tls && (caFile is not null || certFile is not null)))
{
    Console.Error.WriteLine("Check MACHINE_ID, MQTT host and port, and the TLS certificate settings. Plain MQTT is for loopback.");
    return 2;
}

try
{
    using var ca = caFile is null ? null : X509CertificateLoader.LoadCertificateFromFile(caFile);
    using var certificate = certFile is null ? null : X509Certificate2.CreateFromPemFile(certFile, keyFile);
    var options = new MqttClientOptionsBuilder().WithTcpServer(host, port)
        .WithClientId($"edge-{machineId}-{Guid.NewGuid():N}");
    if (tls)
    {
        options.WithTlsOptions(builder =>
        {
            builder.UseTls();
            if (ca is not null)
            {
                builder.WithCertificateValidationHandler(context =>
                {
                    if (context.Certificate is null ||
                        (context.SslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
                    {
                        return false;
                    }
                    using var server = X509CertificateLoader.LoadCertificate(context.Certificate.GetRawCertData());
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(ca);
                    chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    if (context.Chain is { } peerChain)
                    {
                        foreach (var element in peerChain.ChainElements.Cast<X509ChainElement>().Skip(1))
                        {
                            chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                        }
                    }
                    return chain.Build(server);
                });
            }
            if (certificate is not null)
            {
                builder.WithClientCertificates(new[] { certificate });
            }
        });
    }

    using var client = new MqttClientFactory().CreateMqttClient();
    var connection = options.Build();
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    async Task PublishAsync(double temperature)
    {
        var reading = new Reading(Guid.NewGuid(), machineId, DateTimeOffset.UtcNow, temperature);
        var payload = JsonSerializer.Serialize(reading, json);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"machines/{machineId}/telemetry")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(connection, timeout.Token);
                }
                var result = await client.PublishAsync(message, timeout.Token);
                if (!result.IsSuccess)
                {
                    throw new IOException("The broker rejected the reading.");
                }
                Console.WriteLine(payload);
                return;
            }
            catch (Exception) when (attempt < 3 && !cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
            }
        }
    }

    if (simulate)
    {
        foreach (var temperature in new[] { 62, 75, 86, 81 })
        {
            await PublishAsync(temperature);
        }
    }
    else
    {
        var once = args.Length == 3;
        do
        {
            try
            {
                var temperature = await SensorFile.ReadAsync(args[1], cancellation);
                await PublishAsync(temperature);
            }
            catch (Exception error) when (!once && error is IOException or InvalidDataException)
            {
                Console.Error.WriteLine("Reading unavailable. Retrying in five seconds.");
            }
            if (!once)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            }
        } while (!once);
    }
    await client.DisconnectAsync();
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Publisher stopped ({error.GetType().Name}). Check the sensor file or MQTT connection.");
    return 1;
}

record Reading(Guid Id, string MachineId, DateTimeOffset RecordedAt, double TemperatureC);
