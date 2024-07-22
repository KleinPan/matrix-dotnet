namespace matrix_dotnet;

using Refit;

public interface IMatrixApi {

	public abstract record Identifier(string Type, string? User = null, string? Country = null, string? Phone = null, string? Medium = null, string? Address = null);
	public record UserIdentifier(string user) : Identifier("m.id.user", User: user);
	public record PhoneIdentifier(string country, string phone) : Identifier("m.id.phone", Country: country, Phone: phone);
	public record ThirdpartyIdentifier(string medium, string address) : Identifier("m.id.thirdparty", Medium: medium, Address: address);

	public record LoginResponse(
		string access_token,
		string device_id,
		int? expires_in_ms,
		// string? HomeServer, // DEPRECATED
		string? refresh_token,
		// object WellKnown, // NOT IMPLEMENTED
		string user_id
	);

	public abstract record LoginRequest(
		string type,
		Identifier identifier,
		string? password,
		string? token,
		string? initial_device_display_name = null,
		string? device_id = null,
		bool refresh_token = true
	);

	public record PasswordLoginRequest(
			Identifier identifier,
			string password,
			string? initial_device_display_name = null,
			string? device_id = null,
			bool refresh_token = true
	) : LoginRequest("m.login.password", identifier, password, null, initial_device_display_name, device_id, refresh_token);

	public record TokenLoginRequest(
			Identifier identifier,
			string token,
			string? initial_device_display_name = null,
			string? device_id = null,
			bool refresh_token = true
	) : LoginRequest("m.login.token", identifier, null, token, initial_device_display_name, device_id, refresh_token);

	[Post("/login")]
	public Task<LoginResponse> Login(LoginRequest request);


	public record RefreshRequest(string refresh_token);
	public record RefreshResponse(string access_token, int? expires_in_ms, string? refresh_token);

	[Post("/refresh")]
	public Task<RefreshResponse> Refresh(RefreshRequest request);

	public record JoinedRoomsResponse(string[] joined_rooms);

	[Get("/joined_rooms")]
	[Headers("Authorization: Bearer")]
	public Task<JoinedRoomsResponse> GetJoinedRooms();

	public record SendEventResponse(string event_id);

	[Put("/rooms/{roomId}/send/{eventType}/{txnId}")]
	[Headers("Authorization: Bearer")]
	public Task<SendEventResponse> SendEvent<T>(string roomId, string eventType, string txnId, T body);
}
