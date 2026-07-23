using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var nodes = new List<Node>();
        for (int i = 0; i < 10000; i++)
        {
            nodes.Add(new Node { Id = $"Node_{i}" });
        }

        var network = new Network { Nodes = nodes };

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            var options = network.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }
        sw.Stop();
        Console.WriteLine($"LINQ property eval: {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        var cached = network.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < 1000; i++)
        {
            var options = cached;
        }
        sw.Stop();
        Console.WriteLine($"Cached property eval: {sw.ElapsedMilliseconds} ms");
    }
}

public class Node
{
    public string Id { get; set; }
}

public class Network
{
    public List<Node> Nodes { get; set; }
}
