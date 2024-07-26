namespace matrix_dotnet;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Refit;

/// <summary>
/// The IMatrixApi interface represents API endpoints directly and its implementation gets generated by Refit.
/// </summary>
public interface IMatrixApi {
	public record ErrorResponse(string errcode, string error, bool? soft_logout = null);

	[JsonNonFirstPolymorphic(TypeDiscriminatorPropertyName = "type")]
	[JsonNonFirstDerivedType(typeof(UserIdentifier), typeDiscriminator: "m.id.user")]
	[JsonNonFirstDerivedType(typeof(PhoneIdentifier), typeDiscriminator: "m.id.phone")]
	[JsonNonFirstDerivedType(typeof(ThirdpartyIdentifier), typeDiscriminator: "m.id.thirdparty")]
	public abstract record Identifier();
	public record UserIdentifier(string user) : Identifier();
	public record PhoneIdentifier(string country, string phone) : Identifier();
	public record ThirdpartyIdentifier(string medium, string address) : Identifier();

	/// <summary>The return value of the <see cref="Login"/> function.</summary>
	public record LoginResponse(
		string access_token,
		string device_id,
		int? expires_in_ms,
		// string? HomeServer, // DEPRECATED
		string? refresh_token,
		// object WellKnown, // NOT IMPLEMENTED
		string user_id
	);

	/// <summary><see cref="Login"/></summary>
	[JsonNonFirstPolymorphic(TypeDiscriminatorPropertyName = "type")]
	[JsonNonFirstDerivedType(typeof(PasswordLoginRequest), typeDiscriminator: "m.login.password")]
	[JsonNonFirstDerivedType(typeof(TokenLoginRequest), typeDiscriminator: "m.login.token")]
	public abstract record LoginRequest(
		string? initial_device_display_name = null,
		string? device_id = null,
		bool refresh_token = true
	);

	/// <summary><see cref="Login"/></summary>
	public record PasswordLoginRequest(
			Identifier identifier,
			string password,
			string? initial_device_display_name = null,
			string? device_id = null,
			bool refresh_token = true
	) : LoginRequest(initial_device_display_name, device_id, refresh_token);

	/// <summary><see cref="Login"/></summary>
	public record TokenLoginRequest(
			string token,
			string? initial_device_display_name = null,
			string? device_id = null,
			bool refresh_token = true
	) : LoginRequest(initial_device_display_name, device_id, refresh_token);

	/// <summary> Perform login to receive an access and an optional refresh token.
	/// <see href="https://spec.matrix.org/v1.11/client-server-api/#post_matrixclientv3login"/>
	/// </summary>
	[Post("/login")]
	public Task<LoginResponse> Login(LoginRequest request);

	/// <summary><see cref="Refresh"/></summary>
	public record RefreshRequest(string refresh_token);
	/// <summary><see cref="Refresh"/></summary>
	public record RefreshResponse(string access_token, int? expires_in_ms, string? refresh_token);

	/// <summary> Use a refresh token to reaquire a new access token </summary>
	[Post("/refresh")]
	public Task<RefreshResponse> Refresh(RefreshRequest request);

	/// <summary><see cref="GetJoinedRooms"/></summary>
	public record JoinedRoomsResponse(string[] joined_rooms);

	/// <summary> Get a list of IDs of currently joined rooms. </summary>
	[Get("/joined_rooms")]
	[Headers("Authorization: Bearer")]
	public Task<JoinedRoomsResponse> GetJoinedRooms();

	/// <summary>Represents a room event content</summary>
	public abstract record EventContent() {
		public static EventContent FromJSON(string type, JsonObject obj) {
			EventContent? ec = type switch {
				"m.room.message" => JsonSerializer.Deserialize<Message>(obj, new JsonSerializerOptions{Converters = {new PolymorphicJsonConverterFactory()}}),
				_ => new UnknownEvent()
			};
			if (ec is null) throw new JsonException(); // This should not happen
			return ec;
		}
	};

	public record UnknownEvent() : EventContent();

	/// <summary> Represents any <c>m.room.message</c> event. </summary>
	[JsonNonFirstPolymorphic(TypeDiscriminatorPropertyName = "msgtype")]
	[JsonNonFirstDerivedType(typeof(TextMessage), typeDiscriminator: "m.text")]
	public record Message(string body, string msgtype) : EventContent();
	/// <summary> Represents a basic <c>msgtype: m.text</c> message. </summary>
	public record TextMessage(string body) : Message(body, "m.text");

	/// <summary><see cref="SendEvent"/></summary>
	public record SendEventResponse(string event_id);

	/// <summary>Send a raw event to a room. Can be of any type.</summary>
	/// <returns> The <c>event_id</c> of the sent event </returns>
	/// <param name="body">See <see cref="EventContent"/></param>
	[Put("/rooms/{roomId}/send/{eventType}/{txnId}")]
	[Headers("Authorization: Bearer")]
	public Task<SendEventResponse> SendEvent<TEvent>(string roomId, string eventType, string txnId, TEvent body) where TEvent : EventContent;

	public record Event {
		[JsonIgnore]
		public EventContent content {get;}
		[JsonPropertyName("content")]
		public JsonObject _content {init; private get;}
		public string type {get;}
		
		public Event(JsonObject _content, string type) {
			this.content = EventContent.FromJSON(type, _content);
			this._content = _content;
			this.type = type;
		}
	}

	public record StrippedStateEvent(
		JsonObject _content,
		string sender,
		string state_key,
		string type
	) : Event(_content, type);

	public record ClientEventWithoutRoomID(
		JsonObject _content,
		string event_id,
		long origin_server_ts,
		string sender,
		string? state_key,
		UnsignedData? unsigned,
		string type
	) : Event(_content, type);

	public record UnsignedData(
		int? age,
		string? membership,
		JsonObject? prev_content,
		ClientEventWithoutRoomID? redacted_because,
		string? transaction_id
	);

	public record RoomSummary(
		[property: JsonPropertyName("m.heroes")] // Why??
		string[] heroes,
		[property: JsonPropertyName("m.invited_member_count")]
		int invited_member_count,
		[property: JsonPropertyName("m.joined_member_count")]
		int joined_member_count
	);

	public record UnreadNotificationCounts(
		int highlight_count,
		int notification_count
	);

	public record InvitedRoom(InviteState invite_state);
	public record JoinedRoom(
		AccountData account_data,
		Ephemeral ephemeral,
		State state,
		RoomSummary summary,
		Timeline timeline,
		UnreadNotificationCounts unread_notifications,
		Dictionary<string, UnreadNotificationCounts> unread_thread_notifications
	);
	public record KnockedRoom();
	public record LeftRoom();

	public record Rooms(
		Dictionary<string, InvitedRoom> invite,
		Dictionary<string, JoinedRoom> join,
		Dictionary<string, KnockedRoom> knock,
		Dictionary<string, LeftRoom> leave
	);

	public abstract record EventList<TEvent>(TEvent[] events) where TEvent : Event;
	public record AccountData(Event[] events) : EventList<Event>(events);
	public record Presence(Event[] events) : EventList<Event>(events);
	public record ToDevice(Event[] events) : EventList<Event>(events);
	public record Ephemeral(Event[] events) : EventList<Event>(events);
	public record InviteState(StrippedStateEvent[] events) : EventList<StrippedStateEvent>(events);
	public record State(ClientEventWithoutRoomID[] events) : EventList<ClientEventWithoutRoomID>(events);
	public record Timeline(
		ClientEventWithoutRoomID[] events,
		bool limited,
		string prev_batch
	) : EventList<ClientEventWithoutRoomID>(events);
	

	public record SyncResponse(
		AccountData account_data,
		// DeviceLists device_lists, // NOT IMPLEMENTED: E2EE
		// Dictionary<string, integer> device_one_time_keys_count, // NOT IMPLEMENTED: E2EE
		string next_batch,
		Presence? presence,
		Rooms? rooms,
		ToDevice? to_device
	);

	public enum SetPresence { offline, online, unavailable }

	/// <summary> Sync events. See <see href="https://spec.matrix.org/v1.11/client-server-api/#syncing"/></summary>
	[Get("/sync")]
	[Headers("Authorization: Bearer")]
	public Task<SyncResponse> Sync(
		string? filter = null,
		string full_state = "false", // TODO: false.ToString() == "False" and matrix doesn't like that
		SetPresence set_presence = SetPresence.offline,
		string? since = null,
		int timeout = 0
	);


}

