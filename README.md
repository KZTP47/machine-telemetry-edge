# Machine Telemetry Edge

This program sends temperature readings to [Machine Telemetry Lab](https://github.com/KZTP47/machine-telemetry-lab). It can send four sample readings or read a DS18B20 temperature sensor connected to a Linux device.

```text
Sample readings or Linux sensor file -> Edge publisher -> EMQX -> .NET services
```

## For juniors and interns

The [illustrated beginner's guide (PDF)](docs/machine-telemetry-beginners-guide.pdf) explains MQTT and shows what happens after this program sends a reading.

## Try it without hardware

Start the backend with Docker Compose. From this repository, run with the .NET 10 SDK installed.

```sh
dotnet run --project src/Publisher -- simulate
```

The program prints each reading as JSON after EMQX accepts it. Open the backend's readings and alerts APIs to see where it went.

## Use a sensor

Enable Linux 1-Wire support and find the sensor's `w1_slave` file under `/sys/bus/w1/devices/`. The file includes a CRC check and a temperature in thousandths of a degree. The publisher checks the CRC and converts the value to Celsius.

Use the backend's TLS listener for a sensor on another machine. Set `MQTT_HOST` to the broker's certificate hostname, `MQTT_TLS=1`, and `MQTT_CA_FILE`, `MQTT_CLIENT_CERT` and `MQTT_CLIENT_KEY` to your PEM files. The TLS port defaults to 8883. Set `MACHINE_ID` to identify this sensor. Keep the certificates and keys outside the repo.

```sh
dotnet run --project src/Publisher -- sensor /sys/bus/w1/devices/28-EXAMPLE/w1_slave --once
```

Omit `--once` to take a reading every five seconds. The publisher uses the backend's [telemetry v1 format](https://github.com/KZTP47/machine-telemetry-lab#telemetry-v1). Sensor input is tested with files that mimic Linux output.

```sh
dotnet run --project tests/PublisherChecks
```
