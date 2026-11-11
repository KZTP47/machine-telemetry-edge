using MachineTelemetry.Edge;

var directory = Directory.CreateTempSubdirectory("temperature-check-");
var file = Path.Combine(directory.FullName, "w1_slave");
try
{
    await Check("a1 01 4b 46 7f ff 0c 10 72 : crc=72 YES\na1 01 4b 46 7f ff 0c 10 72 t=86000\n", 86);
    await Check("a1 01 4b 46 7f ff 0c 10 72 : crc=72 YES\na1 01 4b 46 7f ff 0c 10 72 t=-5125\n", -5.125);
    await Check("a1 01 4b 46 7f ff 0c 10 72 : crc=72 NO\na1 01 4b 46 7f ff 0c 10 72 t=86000\n", null);
    await Check("a1 01 4b 46 7f ff 0c 10 72 : crc=72 YES\na1 01 4b 46 7f ff 0c 10 72 t=nan\n", null);
    await Check("a1 01 4b 46 7f ff 0c 10 72 : crc=72 YES\na1 01 4b 46 7f ff 0c 10 72 t=126000\n", null);
    await Check("", null);
    Console.WriteLine("PASS: sensor file accepts valid readings and rejects invalid samples.");
}
finally
{
    directory.Delete(recursive: true);
}

async Task Check(string text, double? expected)
{
    await File.WriteAllTextAsync(file, text);
    double? actual;
    try
    {
        actual = await SensorFile.ReadAsync(file, CancellationToken.None);
    }
    catch (InvalidDataException)
    {
        actual = null;
    }
    if (actual != expected)
    {
        throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }
}
