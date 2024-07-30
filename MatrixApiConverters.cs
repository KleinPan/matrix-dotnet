using System.Text.Json;
using System.Text.Json.Serialization;

namespace matrix_dotnet;

public class MXCConverter : JsonConverter<IMatrixApi.MXC> {
	public override IMatrixApi.MXC Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		string? s = reader.GetString();
		if (s is null) throw new JsonException("Could not convert to MXC: isn't string");
		if (!s.StartsWith("mxc://")) throw new JsonException("Could not convert to MXC: doesn't start with mxc://");
		s = s.Substring(6);
		string[] parts = s.Split("/");
		if (parts.Count() != 2) throw new JsonException("Could not convert to MXC: invalid url format");
		return new IMatrixApi.MXC { server_name = parts[0], media_id = parts[1] };
	}

	public override void Write(Utf8JsonWriter writer, IMatrixApi.MXC value, JsonSerializerOptions options) {
		throw new NotImplementedException();
	}
}
