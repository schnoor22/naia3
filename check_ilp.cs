using System;
using System.IO;
using System.Linq;

var files = Directory.GetFiles(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
    "ilp_batch_*.txt");

if (files.Length == 0)
{
    Console.WriteLine("No ILP batch files found");
    return;
}

var file = files.OrderByDescending(f => new FileInfo(f).LastWriteTime).First();
Console.WriteLine($"File: {file}");
Console.WriteLine($"Size: {new FileInfo(file).Length} bytes");

var lines = File.ReadAllLines(file);
Console.WriteLine($"Total lines: {lines.Length}");

for (int i = 0; i < Math.Min(5, lines.Length); i++)
{
    var parts = lines[i].Split(' ');
    Console.WriteLine($"Line {i}: table={parts[0]}, fields={parts[1]}, timestamp={parts[2]}, timestamp_len={parts[2].Length}");
    
    // Check timestamp validity
    if (long.TryParse(parts[2], out var ts))
    {
        Console.WriteLine($"  Timestamp in nanoseconds: {ts}");
        Console.WriteLine($"  Timestamp in milliseconds (div 1M): {ts / 1_000_000}");
    }
    else
    {
        Console.WriteLine($"  ERROR: Could not parse timestamp as long!");
    }
}
