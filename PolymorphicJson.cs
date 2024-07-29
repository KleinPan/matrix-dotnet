using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// Heavily modified from https://github.com/dotnet/runtime/issues/72604#issuecomment-1932302266

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public abstract class JsonAbstractPolymorphicAttribute() : Attribute {
	public string? TypeDiscriminatorPropertyName;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public abstract class JsonAbstractDerivedTypeAttribute(Type derivedType, string typeDiscriminator) : Attribute {
	public Type DerivedType = derivedType;
	public string TypeDiscriminator = typeDiscriminator;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class JsonNonFirstPolymorphicAttribute : JsonAbstractPolymorphicAttribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public class JsonNonFirstDerivedTypeAttribute(Type derivedType, string typeDiscriminator): JsonAbstractDerivedTypeAttribute(derivedType, typeDiscriminator);

public abstract class PolymorphicJsonConverterFactory<TAttr> : JsonConverterFactory where TAttr : JsonAbstractPolymorphicAttribute {
	private Dictionary<Type, Dictionary<string, Type>> _additionalTypeDicts = new();
	private Type TConverter;
	
	public PolymorphicJsonConverterFactory(Type tConverter) : base() {
		TConverter = tConverter;
	}

	public override bool CanConvert(Type typeToConvert) {
		return typeToConvert.GetCustomAttribute<TAttr>() is not null;
	}

	public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
		return (JsonConverter?)Activator.CreateInstance(TConverter.MakeGenericType(typeToConvert), options, _additionalTypeDicts.TryGetValue(typeToConvert, out var typeDict) ? typeDict : null);
	}

	public void AddDerivedType(Type baseType, Type derivedType, string discriminator) {
		if (!_additionalTypeDicts.TryGetValue(baseType, out var baseTypeDict)) {
			_additionalTypeDicts[baseType] = baseTypeDict = new();
		}
		baseTypeDict[discriminator] = derivedType;
	}
}

public class PolymorphicNonFirstJsonConverterFactory() : PolymorphicJsonConverterFactory<JsonNonFirstPolymorphicAttribute>(typeof(PolymorphicNonFirstJsonConverter<>));
public class PolymorphicPropertyJsonConverterFactory() : PolymorphicJsonConverterFactory<JsonPropertyPolymorphicAttribute>(typeof(PolymorphicPropertyJsonConverter<>));

public class PolymorphicNonFirstJsonConverter<T>(JsonSerializerOptions options, Dictionary<string, Type>? additionalDerivedTypes = null)
	: PolymorphicJsonConverter<T, JsonNonFirstPolymorphicAttribute, JsonNonFirstDerivedTypeAttribute>(options, additionalDerivedTypes);

public class PolymorphicJsonConverter<T, TAttr, TDerAttr> : JsonConverter<T> where TAttr : JsonAbstractPolymorphicAttribute where TDerAttr : JsonAbstractDerivedTypeAttribute {
	protected readonly string _discriminatorPropName;
	protected readonly Dictionary<string, Type> _discriminatorToSubtype = [];

	public PolymorphicJsonConverter(JsonSerializerOptions options, Dictionary<string, Type>? additionalDerivedTypes = null) {
		var attr = typeof(T).GetCustomAttribute<TAttr>();
		if (attr is null) throw new InvalidOperationException("Converter tasked with converting unconvertible type");
		_discriminatorPropName =
			attr.TypeDiscriminatorPropertyName
			?? options.PropertyNamingPolicy?.ConvertName("$type")
			?? "$type";
		if (additionalDerivedTypes is not null) _discriminatorToSubtype = additionalDerivedTypes;
		foreach (var subtype in typeof(T).GetCustomAttributes<TDerAttr>()) {
			if (subtype.TypeDiscriminator is not string discriminator) throw new NotSupportedException("Type discriminator must be string");
			_discriminatorToSubtype.Add(discriminator, subtype.DerivedType);
		}
	}

	public override bool CanConvert(Type typeToConvert) => typeof(T) == typeToConvert;

	protected virtual object Deserialize(JsonElement root, Type typeToConvert, Type chosenType, JsonSerializerOptions options) {
		return JsonSerializer.Deserialize(root, chosenType, options)!;
	}

	protected virtual Type GetDefaultType(Type typeToConvert) {
		return typeToConvert;
	}

	public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		using var doc = JsonDocument.ParseValue(ref reader);

		var root = doc.RootElement;
		var typeProperty = root.GetProperty(_discriminatorPropName);

		if (typeProperty.GetString() is not string typeName) {
			throw new JsonException(
				$"Could not find string property {_discriminatorPropName} " +
				$"when trying to deserialize {typeof(T).Name}");
		}

		if (!_discriminatorToSubtype.TryGetValue(typeName, out var type)) {
			type = GetDefaultType(typeToConvert);
		}

		return (T)Deserialize(root, typeToConvert, type, options);
	}

	public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) {
		var type = value!.GetType();
		JsonSerializer.Serialize(writer, value, type, options);
	}
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
public class JsonPropertyPolymorphicAttribute(Type baseType) : JsonAbstractPolymorphicAttribute {
	public Type BaseType = baseType;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = true)]
public class JsonPropertyDerivedTypeAttribute(Type derivedType, string typeDiscriminator) : JsonAbstractDerivedTypeAttribute(derivedType, typeDiscriminator);

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public class JsonPropertyTargetPropertyAttribute() : Attribute { }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public class JsonPropertyRecursiveAttribute() : Attribute { }


public sealed class PolymorphicPropertyJsonConverter<T> : PolymorphicJsonConverter<T, JsonPropertyPolymorphicAttribute, JsonPropertyDerivedTypeAttribute> {
	private readonly Type _baseType;

	public PolymorphicPropertyJsonConverter(JsonSerializerOptions options, Dictionary<string, Type>? additionalDerivedTypes = null) : base(options, additionalDerivedTypes) {
		var attr = typeof(T).GetCustomAttribute<JsonPropertyPolymorphicAttribute>();
		if (attr is null) throw new InvalidOperationException("Converter tasked with converting unconvertible type");
		_baseType = attr.BaseType;
	}

	protected override Type GetDefaultType(Type typeToConvert) {
			return _baseType;
		}

	protected override object Deserialize(JsonElement root, Type typeToConvert, Type chosenType, JsonSerializerOptions options) {
		ConstructorInfo[] constructors = typeToConvert.GetConstructors();
		if (constructors.Count() != 1) throw new MissingMethodException("Only single constructor types are supported");
		ConstructorInfo constructor = constructors[0];

		ParameterInfo[] parameters = constructor.GetParameters();

		List<object?> args = [];
		foreach (var param in parameters) {
			if (param.Name is null) throw new InvalidOperationException("Nameless parameters not supported");
			string jsonName = options.PropertyNamingPolicy?.ConvertName(param.Name) ?? param.Name;
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
			} else if (param.GetCustomAttribute<JsonPropertyRecursiveAttribute>() is not null) {
				args.Add(Deserialize(jsonEl.Value, param.ParameterType, chosenType, options));
			} else if (param.GetCustomAttribute<JsonPropertyTargetPropertyAttribute>() is not null) {
				args.Add(JsonSerializer.Deserialize(jsonEl.Value, chosenType, options));
			} else {
				args.Add(JsonSerializer.Deserialize(jsonEl.Value, param.ParameterType, options));
			}

		}
		return constructor.Invoke(args.ToArray());
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
