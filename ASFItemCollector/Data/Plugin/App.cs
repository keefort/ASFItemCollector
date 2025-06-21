using System.Text.Json.Serialization;

namespace ASFItemCollector.Data.Plugin;

[method: JsonConstructor]
public sealed class App(uint id, string name, IReadOnlyCollection<uint> itemDefIds)
{
	[JsonInclude]
	[JsonPropertyName("ID")]
	public uint ID { get; private set; } = id;

	[JsonInclude]
	[JsonPropertyName("Name")]
	public string? Name { get; private set; } = name;

	[JsonInclude]
	[JsonPropertyName("ItemDefIds")]
	public IReadOnlyCollection<uint> ItemDefIds { get; private set; } = itemDefIds ?? throw new ArgumentNullException(nameof(itemDefIds));
}