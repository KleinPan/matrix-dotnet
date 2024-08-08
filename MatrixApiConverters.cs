using System.Text.Json;
using System.Text.Json.Serialization;
using matrix_dotnet.Api;

namespace matrix_dotnet;

public class MXCConverter : JsonConverter<Api.MXC> {
	public override Api.MXC Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		string? s = reader.GetString();
		if (s is null) throw new JsonException("Could not convert to MXC: isn't string");
		return new Api.MXC(s);
	}

	public override void Write(Utf8JsonWriter writer, Api.MXC value, JsonSerializerOptions options) {
		writer.WriteStringValue(value.ToString());
	}
}

public class EventIDConverter : JsonConverter<Api.EventID> {
	public override EventID Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		string? s = reader.GetString();
		if (s is null) throw new JsonException("Could not convert to MXC: isn't string");
		return new Api.EventID(s);
	}

	public override void Write(Utf8JsonWriter writer, EventID value, JsonSerializerOptions options) {
		writer.WriteStringValue(value.ToString());
	}

    public override EventID ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Read(ref reader, typeToConvert, options);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, EventID value, JsonSerializerOptions options) => Write(writer, value, options);
}

public class RoomIDConverter : JsonConverter<Api.RoomID> {
	public override RoomID Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		string? s = reader.GetString();
		if (s is null) throw new JsonException("Could not convert to MXC: isn't string");
		return new Api.RoomID(s);
	}

	public override void Write(Utf8JsonWriter writer, RoomID value, JsonSerializerOptions options) {
		writer.WriteStringValue(value.ToString());
	}

    public override RoomID ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Read(ref reader, typeToConvert, options);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, RoomID value, JsonSerializerOptions options) => Write(writer, value, options);
}

