using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.Particles.Diagnostics;
using ValveResourceFormat.ResourceTypes;

// Runs one particle effect as a simulation with no graphics device and writes what it did, so two
// runs — ours against ours, or ours against another implementation's — can be compared as state
// rather than as pixels. See README.md next to this file.

const string Usage = """
        ParticleTrace --vpk <package.vpk> --file <particles/....vpcf_c> [options]

          --vpk PATH          Package to read the effect and its children from. Required.
          --file PATH         Effect inside the package, e.g. particles/water_fx/waterfall_anubis.vpcf_c. Required.
          --out PATH          Where the trace goes. Default: stdout.
          --receipt PATH      Write a JSON receipt (inputs, hashes, warnings) here.
          --seed N            Random table offset for the root system. Default 0.
          --seed-stride N     Distance between consecutive systems' seeds. Default 61.
          --steps N           How many steps to run. Default 120.
          --dt SECONDS        Fixed step length. Default 0.0166666666666667 (1/60).
          --detail NAME       low | medium | high | ultra. Default ultra.
          --cp-source NAME    none | runtime. Default runtime.
          --cp N=X,Y,Z[,OX,OY,OZ]   Place a control point. Repeatable, applied after --cp-source.
          --no-state          Do not record per-particle state, only the function attribution layer.
          --no-functions      Do not record the function attribution layer, only state.
          --state-stride N    Record state every N steps. Default 1.
        """;

try
{
    return Run(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    return 2;
}

static int Run(string[] args)
{
    var arguments = ParseArguments(args);

    var vpkPath = Required(arguments, "vpk");
    var filePath = Required(arguments, "file");
    var outPath = Single(arguments, "out");
    var receiptPath = Single(arguments, "receipt");

    var options = new ParticleSimulationOptions
    {
        Seed = ParseInt(arguments, "seed", 0),
        SeedStride = ParseInt(arguments, "seed-stride", 61),
        Steps = ParseInt(arguments, "steps", 120),
        TimeStep = ParseFloat(arguments, "dt", 1f / 60f),
        DetailLevel = ParseDetail(Single(arguments, "detail")),
        ControlPointSource = ParseControlPointSource(Single(arguments, "cp-source")),
        ControlPoints = ParseControlPoints(arguments),
        RecordState = !arguments.ContainsKey("no-state"),
        RecordFunctions = !arguments.ContainsKey("no-functions"),
        StateStride = ParseInt(arguments, "state-stride", 1),
    };

    var warnings = new WarningCollector();

    using var package = new Package();
    package.Read(vpkPath);

    using var fileLoader = new GameFileLoader(package, vpkPath);
    using var rendererContext = new RendererContext(fileLoader, warnings);

    // The loader appends the compiled suffix itself, so take the name either way round: a package
    // listing gives the _c form and an asset reference gives the plain one.
    var assetName = filePath.EndsWith("_c", StringComparison.Ordinal) ? filePath[..^2] : filePath;

    using var resource = fileLoader.LoadFileCompiled(assetName)
        ?? throw new ArgumentException($"'{assetName}' is not in '{vpkPath}' or its search paths");

    if (resource.DataBlock is not ParticleSystem particleSystem)
    {
        throw new ArgumentException($"'{filePath}' is a {resource.ResourceType}, not a particle system");
    }

    ParticleSimulationResult result;

    if (outPath == null)
    {
        result = ParticleSimulation.Run(particleSystem, rendererContext, options, Console.Out);
    }
    else
    {
        // A LF-only writer, so the trace hashes the same on any host.
        using var output = new StreamWriter(outPath, append: false, new UTF8Encoding(false))
        {
            NewLine = "\n",
        };

        result = ParticleSimulation.Run(particleSystem, rendererContext, options, output);
    }

    foreach (var warning in warnings.Messages)
    {
        Console.Error.WriteLine($"warning: {warning}");
    }

    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{result.Systems.Count} systems, {result.Steps} steps, {result.Records} records"));

    if (receiptPath != null)
    {
        WriteReceipt(receiptPath, vpkPath, filePath, outPath, options, result, warnings);
    }

    return 0;
}

static void WriteReceipt(
    string receiptPath,
    string vpkPath,
    string filePath,
    string? outPath,
    ParticleSimulationOptions options,
    ParticleSimulationResult result,
    WarningCollector warnings)
{
    var receipt = new StringBuilder(1024);

    receipt.Append("{\n");
    receipt.Append("  \"tool\": \"ParticleTrace\",\n");
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"vpk\": {Json(vpkPath)},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"file\": {Json(filePath)},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"trace\": {Json(outPath ?? "<stdout>")},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"traceSha256\": {Json(outPath == null ? "" : Sha256(outPath))},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"seed\": {options.Seed},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"seedStride\": {options.SeedStride},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"steps\": {options.Steps},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"timeStep\": {options.TimeStep.ToString("R", CultureInfo.InvariantCulture)},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"detailLevel\": {Json(options.DetailLevel.ToString())},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"controlPointSource\": {Json(options.ControlPointSource.ToString())},\n"));
    receipt.Append(string.Create(CultureInfo.InvariantCulture, $"  \"records\": {result.Records},\n"));
    receipt.Append("  \"systems\": [\n");

    for (var i = 0; i < result.Systems.Count; i++)
    {
        var parts = result.Systems[i].Split('\t');
        receipt.Append(string.Create(CultureInfo.InvariantCulture,
            $"    {{\"path\": {Json(parts[0])}, \"name\": {Json(parts[1])}, \"seed\": {parts[2]}}}"));
        receipt.Append(i == result.Systems.Count - 1 ? "\n" : ",\n");
    }

    receipt.Append("  ],\n");
    receipt.Append("  \"warnings\": [\n");

    for (var i = 0; i < warnings.Messages.Count; i++)
    {
        receipt.Append("    ").Append(Json(warnings.Messages[i]));
        receipt.Append(i == warnings.Messages.Count - 1 ? "\n" : ",\n");
    }

    receipt.Append("  ]\n}\n");

    File.WriteAllText(receiptPath, receipt.ToString());
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}

static string Json(string value)
{
    var quoted = new StringBuilder(value.Length + 2);
    quoted.Append('"');

    foreach (var c in value)
    {
        switch (c)
        {
            case '"': quoted.Append("\\\""); break;
            case '\\': quoted.Append("\\\\"); break;
            case '\n': quoted.Append("\\n"); break;
            case '\r': quoted.Append("\\r"); break;
            case '\t': quoted.Append("\\t"); break;
            default:
                if (c < ' ')
                {
                    quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    quoted.Append(c);
                }

                break;
        }
    }

    quoted.Append('"');

    return quoted.ToString();
}

static Dictionary<string, List<string>> ParseArguments(string[] args)
{
    var parsed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    string? pending = null;

    foreach (var arg in args)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            pending = arg[2..];

            if (!parsed.ContainsKey(pending))
            {
                parsed[pending] = [];
            }

            continue;
        }

        if (pending == null)
        {
            throw new ArgumentException($"Unexpected value '{arg}' before any option");
        }

        parsed[pending].Add(arg);
    }

    return parsed;
}

static string Required(Dictionary<string, List<string>> arguments, string name)
    => Single(arguments, name) ?? throw new ArgumentException($"--{name} is required");

static string? Single(Dictionary<string, List<string>> arguments, string name)
{
    if (!arguments.TryGetValue(name, out var values) || values.Count == 0)
    {
        return null;
    }

    if (values.Count > 1)
    {
        throw new ArgumentException($"--{name} was given more than once");
    }

    return values[0];
}

static int ParseInt(Dictionary<string, List<string>> arguments, string name, int fallback)
{
    var value = Single(arguments, name);

    if (value == null)
    {
        return fallback;
    }

    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : throw new ArgumentException($"--{name} '{value}' is not a whole number");
}

static float ParseFloat(Dictionary<string, List<string>> arguments, string name, float fallback)
{
    var value = Single(arguments, name);

    if (value == null)
    {
        return fallback;
    }

    return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : throw new ArgumentException($"--{name} '{value}' is not a number");
}

static ParticleDetailLevel ParseDetail(string? value) => value switch
{
    null or "ultra" => ParticleDetailLevel.PARTICLEDETAIL_ULTRA,
    "high" => ParticleDetailLevel.PARTICLEDETAIL_HIGH,
    "medium" => ParticleDetailLevel.PARTICLEDETAIL_MEDIUM,
    "low" => ParticleDetailLevel.PARTICLEDETAIL_LOW,
    _ => throw new ArgumentException($"--detail '{value}' is not low, medium, high or ultra"),
};

static ParticleControlPointSource ParseControlPointSource(string? value) => value switch
{
    null or "runtime" => ParticleControlPointSource.Runtime,
    "none" => ParticleControlPointSource.None,
    _ => throw new ArgumentException($"--cp-source '{value}' is not none or runtime"),
};

static List<ParticleControlPointPlacement> ParseControlPoints(Dictionary<string, List<string>> arguments)
{
    var placements = new List<ParticleControlPointPlacement>();

    if (!arguments.TryGetValue("cp", out var values))
    {
        return placements;
    }

    foreach (var value in values)
    {
        var split = value.Split('=');

        if (split.Length != 2)
        {
            throw new ArgumentException($"--cp '{value}' is not N=X,Y,Z");
        }

        var components = split[1].Split(',');

        if (components.Length is not (3 or 6))
        {
            throw new ArgumentException($"--cp '{value}' needs 3 or 6 components");
        }

        var numbers = new float[components.Length];

        for (var i = 0; i < components.Length; i++)
        {
            if (!float.TryParse(components[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                throw new ArgumentException($"--cp '{value}' has a non-numeric component '{components[i]}'");
            }
        }

        if (!int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            throw new ArgumentException($"--cp '{value}' has a non-numeric control point number");
        }

        placements.Add(new ParticleControlPointPlacement(
            index,
            new System.Numerics.Vector3(numbers[0], numbers[1], numbers[2]),
            components.Length == 6
                ? new System.Numerics.Vector3(numbers[3], numbers[4], numbers[5])
                : System.Numerics.Vector3.Zero));
    }

    return placements;
}

/// <summary>
/// Collects the renderer's warnings instead of printing them as they happen, so that an unsupported
/// class the run met lands in the receipt beside the trace it affected.
/// </summary>
internal sealed class WarningCollector : ILogger
{
    private readonly List<string> messages = [];

    public IReadOnlyList<string> Messages => messages;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        messages.Add(formatter(state, exception));
    }
}
