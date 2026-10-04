using System.Text.Json;
using System.IO;

namespace Poster.Core;

public class Workspace
{
    public Dictionary<string, string> Variables { get; set; } = new();
    public string Name { get; set; } = "My Workspace";
    public Guid Id { get; set; }

    public HttpClient Client { get; set; }

    public List<PosterReqRes> Requests { get; set; } = [];

    public Workspace()
    {
        Id = Guid.CreateVersion7();
        Client = new HttpClient();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize<Workspace>(this);
        File.WriteAllText($"Workspace-{Id}.json", json);
    }

    public string[] ListLocal()
    {
        string[] files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.json");
        return files;
    }

    public void Load(string file_path)
    {
        if (!File.Exists(file_path))
            return;

        var json = File.ReadAllText(file_path, System.Text.Encoding.UTF8);

        // file is either corrupt or empty or wrong
        if (json.Length <= 10)
            return;

        var w = JsonSerializer.Deserialize<Workspace>(json);

        if (w != null)
            load(w);

        
    }

    private void load(Workspace w)
    {
        Id = w.Id;
        Name = w.Name;
        Variables = w.Variables;
        Requests = w.Requests;
    }
}
