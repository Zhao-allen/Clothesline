using System.IO.Compression;

// Packs the published application into payload.zip, which the installer embeds
// as a resource and extracts to whatever folder the user picks.
//
// Run:  dotnet run -- <sourceDir> <outputZip>

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: MakePayload <sourceDir> <outputZip>");
    return 1;
}

var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);

if (!Directory.Exists(source))
{
    Console.Error.WriteLine($"source not found: {source}");
    return 1;
}

if (File.Exists(output)) File.Delete(output);

// Optimal keeps the payload as small as it will get without losing time on
// files that are already compressed, which every DLL and PNG in a .NET app is.
using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
{
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
        zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
        Console.WriteLine($"  + {relative}");
    }
}

var size = new FileInfo(output).Length;
Console.WriteLine($"payload: {output} ({size / 1024 / 1024} MB)");
return 0;
