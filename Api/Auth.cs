namespace matrix_dotnet.Api;

[JsonNonFirstPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonNonFirstDerivedType(typeof(UserIdentifier), typeDiscriminator: "m.id.user")]
[JsonNonFirstDerivedType(typeof(PhoneIdentifier), typeDiscriminator: "m.id.phone")]
[JsonNonFirstDerivedType(typeof(ThirdpartyIdentifier), typeDiscriminator: "m.id.thirdparty")]
public abstract record Identifier(string type);
public record UserIdentifier(string user) : Identifier("m.id.user");
public record PhoneIdentifier(string country, string phone) : Identifier("m.id.phone");
public record ThirdpartyIdentifier(string medium, string address) : Identifier("m.id.thirdparty");

/// <summary>The return value of the <see cref="IMatrixApi.Login"/> function.</summary>
public record LoginResponse(
	string access_token,
	string device_id,
	int? expires_in_ms,
	// string? HomeServer, // DEPRECATED
	string? refresh_token,
	// object WellKnown, // NOT IMPLEMENTED
	string user_id
);

/// <summary><see cref="IMatrixApi.Login"/></summary>
[JsonNonFirstPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonNonFirstDerivedType(typeof(PasswordLoginRequest), typeDiscriminator: "m.login.password")]
[JsonNonFirstDerivedType(typeof(TokenLoginRequest), typeDiscriminator: "m.login.token")]
public abstract record LoginRequest(
	string type,
	string? initial_device_display_name = null,
	string? device_id = null,
	bool refresh_token = true
);

/// <summary><see cref="IMatrixApi.Login"/></summary>
public record PasswordLoginRequest(
		Identifier identifier,
		string password,
		string? initial_device_display_name = null,
		string? device_id = null,
		bool refresh_token = true
) : LoginRequest("m.login.password", initial_device_display_name, device_id, refresh_token);

/// <summary><see cref="IMatrixApi.Login"/></summary>
public record TokenLoginRequest(
		string token,
		string? initial_device_display_name = null,
		string? device_id = null,
		bool refresh_token = true
) : LoginRequest("m.login.token", initial_device_display_name, device_id, refresh_token);


