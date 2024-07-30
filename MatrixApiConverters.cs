using System.Text.Json;
using System.Text.Json.Serialization;

namespace matrix_dotnet;

public class MXCConverter : JsonConverter<IMatrixApi.MXC> {
	public override IMatrixApi.MXC Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		string? s = reader.GetString();
		if (s is null) throw new JsonException("Could not convert to MXC: isn't string");
		return new IMatrixApi.MXC(s);
	}

	public override void Write(Utf8JsonWriter writer, IMatrixApi.MXC value, JsonSerializerOptions options) {
		writer.WriteStringValue(value.ToString());
	}
}
