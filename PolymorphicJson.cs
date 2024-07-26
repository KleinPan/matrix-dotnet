using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// MODIFIED FROM https://github.com/dotnet/runtime/issues/72604#issuecomment-1932302266

/// <summary>
/// Same as <see cref="JsonPolymorphicAttribute"/> but used for the hack below. Necessary because using the built-in
/// attribute will lead to NotSupportedExceptions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class JsonNonFirstPolymorphicAttribute() : Attribute {
	public string? TypeDiscriminatorPropertyName;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public class JsonNonFirstDerivedTypeAttribute(Type derivedType, string typeDiscriminator) : Attribute {
	public Type DerivedType = derivedType;
	public string TypeDiscriminator = typeDiscriminator;
}

/// <summary>
/// Same as <see cref="JsonNonFirstDerivedType"/> but uses type discriminator from parent to serialize child.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public class JsonDerivedChildAttribute(Type subtype, string discriminator, Type baseType) : Attribute {
	public Type Subtype { get; set; } = subtype;
	public Type BaseType { get; set; } = baseType;
	public string Discriminator { get; set; } = discriminator;
}


public sealed class PolymorphicJsonConverterFactory : JsonConverterFactory {
	public override bool CanConvert(Type typeToConvert) {
		return typeToConvert.GetCustomAttribute<JsonNonFirstPolymorphicAttribute>() is not null;
	}

	public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
		return (JsonConverter?)Activator.CreateInstance(typeof(PolymorphicJsonConverter<>).MakeGenericType(typeToConvert), options);
	}
}

/// <summary>
/// A temporary hack to support deserializing JSON payloads that use polymorphism but don't specify $type as the first field.
/// Modified from https://github.com/dotnet/runtime/issues/72604#issuecomment-1440708052.
/// </summary>
public sealed class PolymorphicJsonConverter<T> : JsonConverter<T> {
	private readonly string _discriminatorPropName;
	private readonly Dictionary<string, Type> _discriminatorToSubtype = [];
	private readonly Dictionary<Type, string> _subtypeToDiscriminator = [];

	public PolymorphicJsonConverter(JsonSerializerOptions options) {
		_discriminatorPropName =
			typeof(T).GetCustomAttribute<JsonNonFirstPolymorphicAttribute>()?.TypeDiscriminatorPropertyName
			?? options.PropertyNamingPolicy?.ConvertName("$type")
			?? "$type";
		foreach (var subtype in typeof(T).GetCustomAttributes<JsonNonFirstDerivedTypeAttribute>()) {
			if (subtype.TypeDiscriminator is not string discriminator) throw new NotSupportedException("Type discriminator must be string");
			_discriminatorToSubtype.Add(discriminator, subtype.DerivedType);
			_subtypeToDiscriminator.Add(subtype.DerivedType, discriminator);
		}
	}

	public override bool CanConvert(Type typeToConvert) => typeof(T) == typeToConvert || _subtypeToDiscriminator[typeToConvert] is not null;

	public override T Read(ref Utf8JsonReader reader, Type objectType, JsonSerializerOptions options) {
		var reader2 = reader;
		using var doc = JsonDocument.ParseValue(ref reader2);

		var root = doc.RootElement;
		var typeField = root.GetProperty(_discriminatorPropName);

		if (typeField.GetString() is not string typeName) {
			throw new JsonException(
				$"Could not find string property {_discriminatorPropName} " +
				$"when trying to deserialize {typeof(T).Name}");
		}

		if (!_discriminatorToSubtype.TryGetValue(typeName, out var type)) {
			throw new JsonException($"Unknown type: {typeName}");
		}

		return (T)JsonSerializer.Deserialize(ref reader, type, options)!;
	}

	public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) {
		var type = value!.GetType();
		JsonSerializer.Serialize(writer, value, type, options);
	}
}
