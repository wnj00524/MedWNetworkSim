import sys

def main():
    filepath = sys.argv[1]
    search_str = """    private static Dictionary<string, double> CloneDictionary(IDictionary<string, double> source)
    {
        var clone = new Dictionary<string, double>(source.Count, Comparer);
        foreach (var pair in source) clone[pair.Key] = pair.Value;
        return clone;
    }"""

    replace_str = """    private static Dictionary<string, double> CloneDictionary(IDictionary<string, double> source)
    {
        return new Dictionary<string, double>(source, Comparer);
    }"""

    with open(filepath, 'r') as f:
        content = f.read()

    if search_str in content:
        content = content.replace(search_str, replace_str)
        with open(filepath, 'w') as f:
            f.write(content)
        print("Success TemporalNetworkSimulationEngine.cs 1087")
    else:
        print("Could not find string 1087")

if __name__ == "__main__":
    main()
