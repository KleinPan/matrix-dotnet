using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// Heavily modified from https://github.com/dotnet/runtime/issues/72604#issuecomment-1932302266

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class JsonNonFirstPolymorphicAttribute() : Attribute {
	public string? TypeDiscriminatorPropertyName;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public class JsonNonFirstDerivedTypeAttribute(Type derivedType, string typeDiscriminator) : Attribute {
	public Type DerivedType = derivedType;
	public string TypeDiscriminator = typeDiscriminator;
}

public sealed class PolymorphicJsonConverterFactory : JsonConverterFactory {
	private Dictionary<Type, Dictionary<string, Type>> _additionalTypeDicts = new();

	public override bool CanConvert(Type typeToConvert) {
		return typeToConvert.IsAbstract && typeToConvert.GetCustomAttribute<JsonNonFirstPolymorphicAttribute>() is not null;
	}

	public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
		return (JsonConverter?)Activator.CreateInstance(typeof(PolymorphicJsonConverter<>).MakeGenericType(typeToConvert), options, _additionalTypeDicts.TryGetValue(typeToConvert, out var typeDict) ? typeDict : null);
	}

	public void AddDerivedType(Type baseType, Type derivedType, string discriminator) {
		if (!_additionalTypeDicts.TryGetValue(baseType, out var baseTypeDict)) {
			_additionalTypeDicts[baseType] = baseTypeDict = new();
		}
		baseTypeDict[discriminator] = derivedType;
	}
}

public sealed class PolymorphicJsonConverter<T> : JsonConverter<T> {
	private readonly string _discriminatorPropName;
	private readonly Dictionary<string, Type> _discriminatorToSubtype = [];

	public PolymorphicJsonConverter(JsonSerializerOptions options, Dictionary<string, Type>? additionalDerivedTypes = null) {
		_discriminatorPropName =
			typeof(T).GetCustomAttribute<JsonNonFirstPolymorphicAttribute>()?.TypeDiscriminatorPropertyName
			?? options.PropertyNamingPolicy?.ConvertName("$type")
			?? "$type";
		if (additionalDerivedTypes is not null) _discriminatorToSubtype = additionalDerivedTypes;
		foreach (var subtype in typeof(T).GetCustomAttributes<JsonNonFirstDerivedTypeAttribute>()) {
			if (subtype.TypeDiscriminator is not string discriminator) throw new NotSupportedException("Type discriminator must be string");
			_discriminatorToSubtype.Add(discriminator, subtype.DerivedType);
		}
	}

	public override bool CanConvert(Type typeToConvert) => typeof(T) == typeToConvert;

	public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		var reader2 = reader;
		using var doc = JsonDocument.ParseValue(ref reader2);

		var root = doc.RootElement;
		var typeProperty = root.GetProperty(_discriminatorPropName);

		if (typeProperty.GetString() is not string typeName) {
			throw new JsonException(
				$"Could not find string property {_discriminatorPropName} " +
				$"when trying to deserialize {typeof(T).Name}");
		}

		if (!_discriminatorToSubtype.TryGetValue(typeName, out var type)) {
			type = typeToConvert;
		}

		return (T)JsonSerializer.Deserialize(ref reader, type, options)!;
	}

	public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) {
		var type = value!.GetType();
		JsonSerializer.Serialize(writer, value, type, options);
	}
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
public class JsonPropertyPolymorphicAttribute(Type baseType) : Attribute {
	public string? TypeDiscriminatorPropertyName;
	public Type BaseType = baseType;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public class JsonPropertyDerivedTypeAttribute(Type derivedType, string typeDiscriminator) : Attribute {
	public Type DerivedType = derivedType;
	public string TypeDiscriminator = typeDiscriminator;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public class JsonPropertyTargetPropertyAttribute() : Attribute { }

public sealed class PolymorphicPropertyJsonConverterFactory : JsonConverterFactory {
	private Dictionary<Type, Dictionary<string, Type>> _additionalTypeDicts = new();

	public override bool CanConvert(Type typeToConvert) {
		return typeToConvert.GetCustomAttribute<JsonPropertyPolymorphicAttribute>() is not null;
	}

	public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
		return (JsonConverter?)Activator.CreateInstance(typeof(PolymorphicPropertyJsonConverter<>).MakeGenericType(typeToConvert), options, _additionalTypeDicts.TryGetValue(typeToConvert, out var typeDict) ? typeDict : null);
	}

	public void AddDerivedType(Type baseType, Type derivedType, string discriminator) {
		if (!_additionalTypeDicts.TryGetValue(baseType, out var baseTypeDict)) {
			_additionalTypeDicts[baseType] = baseTypeDict = new();
		}
		baseTypeDict[discriminator] = derivedType;
	}
}

public sealed class PolymorphicPropertyJsonConverter<T> : JsonConverter<T> {
	private readonly string _discriminatorPropName;
	private readonly Dictionary<string, Type> _discriminatorToSubtype = [];
	private readonly Type _baseType;

	public PolymorphicPropertyJsonConverter(JsonSerializerOptions options, Dictionary<string, Type>? additionalDerivedTypes = null) {
		var attr = typeof(T).GetCustomAttribute<JsonPropertyPolymorphicAttribute>();
		if (attr is null) throw new InvalidOperationException("Converter tasked with converting unconvertible type");
		_discriminatorPropName =
			attr.TypeDiscriminatorPropertyName
			?? options.PropertyNamingPolicy?.ConvertName("$type")
			?? "$type";
		_baseType = attr.BaseType;
		if (additionalDerivedTypes is not null) _discriminatorToSubtype = additionalDerivedTypes;
		foreach (var subtype in typeof(T).GetCustomAttributes<JsonPropertyDerivedTypeAttribute>()) {
			if (subtype.TypeDiscriminator is not string discriminator) throw new NotSupportedException("Type discriminator must be string");
			_discriminatorToSubtype.Add(discriminator, subtype.DerivedType);
			Console.WriteLine($"Added {subtype.DerivedType} as {discriminator} to dict");
		}
	}

	public override bool CanConvert(Type typeToConvert) => typeof(T) == typeToConvert;

	public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		using var doc = JsonDocument.ParseValue(ref reader);

		var root = doc.RootElement;
		JsonElement typeProperty;
		try {
			typeProperty = root.GetProperty(_discriminatorPropName);
		} catch (KeyNotFoundException) {
			Console.WriteLine(root);
			Console.WriteLine(typeToConvert);
			throw;
		}

		if (typeProperty.GetString() is not string typeName) {
			throw new JsonException(
				$"Could not find string property {_discriminatorPropName} " +
				$"when trying to deserialize {typeof(T).Name}");
		}
		
		if (!_discriminatorToSubtype.TryGetValue(typeName, out var type)) {
			type = _baseType;
		}

		Console.WriteLine($"Converting {typeToConvert} with {typeName}");

		ConstructorInfo[] constructors = typeToConvert.GetConstructors();
		if (constructors.Count() != 1) throw new MissingMethodException("Only single constructor types are supported");
		ConstructorInfo constructor = constructors[0];

		ParameterInfo[] parameters = constructor.GetParameters();

		if (parameters.Count() == 0) {
			T result = Activator.CreateInstance<T>();

			foreach (var prop in typeToConvert.GetProperties()) {
				string jsonName = options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
				Console.WriteLine($"Converting prop {prop.Name}");
				JsonElement jsonEl = root.GetProperty(jsonName);
				if (prop.GetCustomAttribute<JsonPropertyTargetPropertyAttribute>() is not null) {
					prop.SetValue(result, JsonSerializer.Deserialize(jsonEl, type, options));
				} else {
					prop.SetValue(result, JsonSerializer.Deserialize(jsonEl, prop.GetType(), options));
				}
			}

			return result;
		} else {
			List<object?> args = [];
			foreach (var param in parameters) {
				if (param.Name is null) throw new InvalidOperationException("Nameless parameters not supported");
				string jsonName = options.PropertyNamingPolicy?.ConvertName(param.Name) ?? param.Name;
				Console.WriteLine($"Converting param {param.Name} of type {param.ParameterType} which is Nullable: {param.IsNullable()}");
				JsonElement? jsonEl = null;
				try {
					jsonEl = root.GetProperty(jsonName);
				} catch (KeyNotFoundException) { }
				if (jsonEl is null) {
					if (param.IsNullable()) {
						args.Add(null);
					} else {
						throw new KeyNotFoundException();
					}
				} else if (param.GetCustomAttribute<JsonPropertyTargetPropertyAttribute>() is not null) {
					Console.WriteLine("Doing the thing, Zhu-li!");
					args.Add(JsonSerializer.Deserialize(jsonEl.Value, type, options));
					Console.WriteLine($"{args.Last()}, {args.Last().GetType()}, {type}");
				} else {
					args.Add(JsonSerializer.Deserialize(jsonEl.Value, param.ParameterType, options));
				}

			}
			Console.WriteLine($"{constructor}, {args.Count}");
			return (T)constructor.Invoke(args.ToArray());
		}
	}

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) {
		throw new NotImplementedException();
	}
}

// From https://www.reddit.com/r/dotnet/comments/18caun7/is_it_impossible_to_determine_if_a_string_is/
public static class NullabilityCheckerExtensions {
	public static bool IsNullable(this ParameterInfo parameter) {
		NullabilityInfoContext nullabilityInfoContext = new NullabilityInfoContext();
		var info = nullabilityInfoContext.Create(parameter);
		if (info.WriteState == NullabilityState.Nullable || info.ReadState == NullabilityState.Nullable) {
			return true;
		}

		return false;
	}
}
