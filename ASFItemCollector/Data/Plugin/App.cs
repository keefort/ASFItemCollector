using System.Text.Json.Serialization;

namespace ASFItemCollector.Data.Plugin;

[method: JsonConstructor]
public sealed class App(uint appId, string name, IReadOnlyCollection<uint> items)
{
	[JsonInclude]
	[JsonPropertyName("AppId")]
	public uint AppId { get; private set; } = appId;

	[JsonInclude]
	[JsonPropertyName("Name")]
	public string? Name { get; private set; } = name;

	[JsonInclude]
	[JsonPropertyName("Items")]
	public IReadOnlyCollection<uint> Items { get; private set; } = items ?? throw new ArgumentNullException(nameof(items));
}