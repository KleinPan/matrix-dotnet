﻿using Refit;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Immutable;

namespace matrix_dotnet.Client;

using StateDict = ImmutableDictionary<StateKey, Api.EventContent>;

/// <summary>
/// The main client class for interacting with Matrix. Most operations
/// require you to login first, see <see cref="MatrixClient.PasswordLogin"/>
/// and <see cref="MatrixClient.TokenLogin"/>
/// </summary>
public class MatrixClient {
	/// <summary> The base URL of the homeserver </summary>
	public Uri Homeserver { get; private set; }
	/// <summary> The underlying API class used to make API calls directly </summary>
	public Api.IMatrixApi ApiClient { get; private set; }

	public string? AccessToken { get; private set; }
	public string? RefreshToken { get; private set; }

	/// <summary> <c>user_id</c> of the currently logged-in user. </summary>
	public string? UserId { get; private set; }
	/// <summary> This client's <c>device_id</c> either generated by the backend
	/// or supplied. Subsequent logins from the same client and device should reuse it.
	/// <seealso href="https://spec.matrix.org/v1.11/client-server-api/#relationship-between-access-tokens-and-devices"/> </summary>
	public string? DeviceId { get; private set; }
	/// <summary> The time when the access token expires. If it is
	/// expired and a <c>refresh_token</c> is available, reauth will
	/// happen automatically, otherwise logging in manually will be required. </summary>
	public DateTime? ExpiresAt { get; private set; }
	/// <summary> Is true when the client is logged in, but its access token is expired </summary>
	public bool Expired {
		get => AccessToken is not null && ExpiresAt is not null && ExpiresAt < DateTime.Now;
	}

	public ILogger? Logger;

	/// <summary>Indicates whether this client is currently logged in.
	/// This status is kept even if the access token is expired, as automatic
	/// relogin is usually possible. </summary>
	public bool LoggedIn {
		get => AccessToken is not null;
	}

	public StateDict? PresenceState { get; private set; }

	public Dictionary<Api.RoomID, StateDict> InvitiedState { get; private set; } = new();
	public Dictionary<Api.RoomID, StateDict> KnockState { get; private set; } = new();
	public Dictionary<Api.RoomID, JoinedRoom> JoinedRooms { get; private set; } = new();
	public Dictionary<Api.RoomID, LeftRoom> LeftRooms { get; private set; } = new();

	public string? NextBatch { get; private set; }

	public Dictionary<Api.EventID, ITimelineEvent> EventsById { get; private set; } = new();

	private bool Syncing = false;
	private bool Filling = false;
	private object SyncLock = new();

	/// <summary> Used by the <see cref="ApiClient"/> to supply
	/// requests with an access token. </summary>
	private async Task<string> GetAccessToken(HttpRequestMessage request, CancellationToken ct) {
		if (!LoggedIn) throw new LoginRequiredException();
		if (Expired) await Refresh();
		return AccessToken!;
	}

	private RefitSettings RefitSettings;

	/// <summary> Generates a transaction id for all requests that need it. </summary>
	private string GenerateTransactionId() {
		return Guid.NewGuid().ToString();
	}

	/// <summary> Use this constructor with <see cref="LoginData"/>
	/// previously generated by the <see cref="ToLoginData"/> method.
	/// </summary>
	public MatrixClient(LoginData loginData, ILogger? logger = null) : this(loginData.Homeserver, logger) {
		Homeserver = loginData.Homeserver;
		AccessToken = loginData.AccessToken;
		RefreshToken = loginData.RefreshToken;
		UserId = loginData.UserId;
		DeviceId = loginData.DeviceId;
		ExpiresAt = loginData.ExpiresAt;
	}

	public MatrixClient(string homeserver, ILogger? logger = null) : this(new Uri(homeserver), logger) { }

	public MatrixClient(Uri homeserver, ILogger? logger = null) {
		Homeserver = homeserver;
		Logger = logger;

		HttpClient client;
		if (Logger is not null) {
			client = new HttpClient(new HttpLoggingHandler(Logger, new AuthenticatedHttpClientHandler(GetAccessToken)));
		} else {
			client = new HttpClient(new AuthenticatedHttpClientHandler(GetAccessToken));
		}
		client.BaseAddress = Homeserver;

		RefitSettings = new RefitSettings {
			ExceptionFactory = ExceptionFactory,
			AuthorizationHeaderValueGetter = GetAccessToken,
			// ContentSerializer = new NewtonsoftJsonContentSerializer()
			ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions {
				// The ObjectToInferredTypesConverter is broken and cannot be used. See https://github.com/reactiveui/refit/issues/1763
				PropertyNameCaseInsensitive = true,
				PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
				Converters = {
					new PolymorphicNonFirstJsonConverterFactory(),
					new PolymorphicPropertyJsonConverterFactory(),
					new MXCConverter(), // It is a bummer that C# doesn't have an interface for loading structs from strings
					new EventIDConverter(),
					new RoomIDConverter(),
					new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
				},
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
			})
		};

		ApiClient = RestService.For<Api.IMatrixApi>(client, RefitSettings);
	}

	/// <summary> Returns a serializable data structure with session data
	/// that can be used to reconstruct the client later. </summary>
	public LoginData ToLoginData() {
		return new LoginData(Homeserver, AccessToken, RefreshToken, UserId, DeviceId, ExpiresAt);
	}

	private void UpdateExpiresAt(int? expiresInMs) {
		if (expiresInMs is null) ExpiresAt = null;
		else ExpiresAt = DateTime.Now.AddMilliseconds((double)expiresInMs);
	}

	private async Task Login(Api.LoginRequest request) {
		var response = await ApiClient.Login(request);

		AccessToken = response.access_token;
		RefreshToken = response.refresh_token;
		UserId = response.user_id;
		DeviceId = response.device_id;
		UpdateExpiresAt(response.expires_in_ms);
	}

	/// <summary> Login using a username and a password.
	/// This API is rate limited however, so for repeated initializations,
	/// as in command line utilities, use the <see cref="ToLoginData"> to
	/// cache login data between runs. </summary>
	/// <param name="deviceId">see <see cref="DeviceId"/>.</param>
	public async Task PasswordLogin(string username, string password, string? initialDeviceDisplayName = null, string? deviceId = null)
		=> await PasswordLogin(new Api.UserIdentifier(username), password, initialDeviceDisplayName, deviceId);

	/// <summary> Login using an <see cref="Api.Identifier"/> and a password </summary>
	/// <param name="deviceId">See <see cref="DeviceId"/>.</param>
	public async Task PasswordLogin(Api.Identifier identifier, string password, string? initialDeviceDisplayName = null, string? deviceId = null) {
		await Login(new Api.PasswordLoginRequest(identifier, password, initialDeviceDisplayName, deviceId));
	}

	/// <summary> Login using a token </summary>
	/// <param name="deviceId">See <see cref="DeviceId"/>.</param>
	public async Task TokenLogin(string token, string? initialDeviceDisplayName = null, string? deviceId = null) {
		await Login(new Api.TokenLoginRequest(token, initialDeviceDisplayName, deviceId));
	}

	/// <summary> Refresh the access token using a refresh token.
	/// This is done automatically in case of expiration, but can
	/// be done manually.</summary>
	public async Task Refresh() {
		if (RefreshToken is null) throw new LoginRequiredException();
		var response = await ApiClient.Refresh(new Api.RefreshRequest(RefreshToken));

		AccessToken = response.access_token;
		RefreshToken = response.refresh_token;
		UpdateExpiresAt(response.expires_in_ms);
	}

	/// <summary> Get joined rooms. <see href="https://spec.matrix.org/v1.11/client-server-api/#get_matrixclientv3joined_rooms"/> </summary>
	[Obsolete("Getting joined rooms this way is deprecated. Use the Sync method and then find joined rooms in the JoinedRooms property.")]
	public async Task<Api.RoomID[]> GetJoinedRooms() {
		var response = await Retry.RetryAsync(async () => await ApiClient.GetJoinedRooms());

		return response.joined_rooms;
	}

	/// <summary> Send any arbitrary event to a room. </summary>
	/// <returns> The <c>event_id</c> of the sent event </returns>
	public async Task<Api.EventID> SendEvent<TEvent>(Api.RoomID roomId, string type, TEvent ev) where TEvent: Api.EventContent {
		var txnId = GenerateTransactionId();
		var response = await Retry.RetryAsync(async () => await ApiClient.SendEvent(roomId, type, txnId, ev));

		return response.event_id;
	}

	/// <summary> Send a <c>m.room.message</c> event to a room. </summary>
	/// <returns> The <c>event_id</c> of the sent message </returns>
	public async Task<Api.EventID> SendMessage<TMessage>(Api.RoomID roomId, TMessage message) where TMessage : Api.Message => await SendEvent(roomId, "m.room.message", message);

	/// <summary> Send a basic <c>m.text</c> message to a room. </summary>
	/// <returns> The <c>event_id</c> of the sent message </returns>
	public async Task<Api.EventID> SendTextMessage(Api.RoomID roomId, string body) => await SendMessage(roomId, new Api.TextMessage(body));

	/// <summary> Resolves state using the provided events and a
	/// starting state dicitonary. Each event gets the relevant state
	/// dictionary attached to it. </summary>
	/// <returns> A tuple with the resolved events and the state dictionary
	/// representing the state at the end of the event list </returns>
	public static (EventWithState[] events, StateDict state) Resolve(IEnumerable<Api.Event> events, StateDict? stateDict = null, bool rewind = false) {
		if (stateDict is null) stateDict = StateDict.Empty;
		List<EventWithState> list = new();
		foreach (var ev in events) {
			if (ev.IsState) {
				var key = new StateKey(ev.type, ev.state_key!);
				if (rewind) {
					if (ev is not Api.ClientEvent clientEvent) throw new InvalidOperationException("Cannot backwards resolve with stripped state");
					if (clientEvent.unsigned is null || clientEvent.unsigned.prev_content is null) stateDict = stateDict.Remove(key);
					else stateDict = stateDict.SetItem(key, clientEvent.unsigned.prev_content);
				} else {
					stateDict = stateDict.SetItem(key, ev.content!);
				}
			}

			if (ev is Api.ClientEvent clev)
				list.Add(new EventWithState(
					clev,
					stateDict
				));
		}
		return (list.ToArray(), stateDict);
	}

	/// <summary> Removes any previous mentions of an event and adds it
	/// to the <see cref="EventsById"/> dictionary. </summary>
	internal void Deduplicate(TimelineEvent e) {
		if (e.Value.Event.event_id is null) return;
		if (EventsById.TryGetValue(e.Value.Event.event_id.Value, out var conflict)) {
			((TimelineEvent)conflict).RemoveSelf();
		}
		EventsById[e.Value.Event.event_id.Value] = e;
	}

	private async Task SyncUnsafe(int timeout) {
		var response = await Retry.RetryAsync(async () => await ApiClient.Sync(timeout: timeout, since: NextBatch));

		string? original_batch = NextBatch;
		NextBatch = response.next_batch;

		if (response.presence is not null)
			PresenceState = Resolve(response.presence.events, PresenceState).state;

		if (response.rooms is not null) {
			if (response.rooms.invite is not null)
				foreach (var invitedRoom in response.rooms.invite) {
					StateDict? state = null;
					InvitiedState.TryGetValue(invitedRoom.Key, out state);
					StateDict? resolvedState = Resolve(invitedRoom.Value.invite_state.events, state).state;
					if (resolvedState is not null)
						InvitiedState[invitedRoom.Key] = resolvedState;
				}
			if (response.rooms.knock is not null)
				foreach (var knockedRoom in response.rooms.knock) {
					StateDict? state = null;
					KnockState.TryGetValue(knockedRoom.Key, out state);
					StateDict? resolvedState = Resolve(knockedRoom.Value.knock_state.events, state).state;
					if (resolvedState is not null)
						KnockState[knockedRoom.Key] = resolvedState;
				}
			if (response.rooms.join is not null)
				foreach (var joinedRoom in response.rooms.join) {
					Api.RoomID id = joinedRoom.Key;
					var roomResponse = joinedRoom.Value;
					JoinedRoom? room = null;
					JoinedRooms.TryGetValue(id, out room);

					StateDict? account_data = Resolve(roomResponse.account_data.events, room?.account_data).state;
					StateDict? ephemeral = Resolve(roomResponse.ephemeral.events, room?.ephemeral).state;
					StateDict? state = Resolve(roomResponse.state.events, room?.state).state;
					Api.RoomSummary summary = roomResponse.summary;
					Timeline timeline = room?.timeline ?? new Timeline(this, id);
					timeline.Sync(roomResponse.timeline, state, roomResponse.timeline.prev_batch, original_batch);
					state = timeline.Last?.Value.State ?? state;
					Api.UnreadNotificationCounts unread_notifications = roomResponse.unread_notifications;
					Dictionary<string, Api.UnreadNotificationCounts> unread_thread_notifications = room?.unread_thread_notifications ?? new();
					if (roomResponse.unread_thread_notifications is not null)
						foreach (var kv in roomResponse.unread_thread_notifications) unread_thread_notifications[kv.Key] = kv.Value;

					JoinedRooms[id] = new JoinedRoom(
						account_data ?? StateDict.Empty,
						ephemeral ?? StateDict.Empty,
						state ?? StateDict.Empty,
						summary,
						timeline,
						unread_notifications,
						unread_thread_notifications
					);
				}
			if (response.rooms.leave is not null)
				foreach (var leftRoom in response.rooms.leave) {
					Api.RoomID id = leftRoom.Key;
					var roomResponse = leftRoom.Value;
					LeftRoom? room = null;
					LeftRooms.TryGetValue(id, out room);

					StateDict? account_data = Resolve(roomResponse.account_data.events, room?.account_data).state;
					StateDict? state = Resolve(roomResponse.state.events, room?.state).state;
					Timeline timeline = room?.timeline ?? new Timeline(this, id);
					timeline.Sync(roomResponse.timeline, state, roomResponse.timeline.prev_batch, original_batch);
					state = timeline.Last?.Value.State;

					LeftRooms[id] = new LeftRoom(
						account_data ?? StateDict.Empty,
						state ?? StateDict.Empty,
						timeline
					);
				}
		}

	}

	/// <summary> Perform a synchronisation operation with the API.
	/// This updates all the internal data structures with the latest
	/// information from the API. </summary>
	/// <param name="timeout"> In milliseconds, when a timeout parameter
	/// is supplied, the server will wait for <paramref name="timeout"/>
	/// milliseconds before replying, returning early if an event arrives.
	/// </param>
	public async Task Sync(int timeout = 0) {
		lock (SyncLock) {
			while (Filling) Monitor.Wait(SyncLock);
			if (Syncing) {
				// Another sync is happening, we don't have to sync again right after it.
				while (Syncing) Monitor.Wait(SyncLock);
				return;
			} else {
				Syncing = true;
			}
		}

		await SyncUnsafe(timeout);

		lock (SyncLock) {
			Syncing = false;
		}

	}

	internal void FillLock() {
		lock (SyncLock) {
			while (Filling || Syncing) Monitor.Wait(SyncLock);
			Filling = true;
		}
	}

	internal void FillUnlock() {
		lock (SyncLock) {
			if (!Filling) throw new InvalidOperationException("Not locked");
			Filling = false;
		}
	}

	/// <summary> Redact an event by ID. </summary>
	public async Task<Api.EventID> Redact(Api.RoomID roomId, Api.EventID eventId, string? reason = null) {
		var txnId = GenerateTransactionId();
		var response = await Retry.RetryAsync(async () => await ApiClient.Redact(eventId, roomId, txnId, new Api.RedactRequest(reason)));

		return response.event_id;

	}

	internal void RedactEvent(Api.ClientEvent redactionEvent) {
		if (redactionEvent.content is not Api.Redaction redaction) throw new ArgumentException("Argument is not a redaction event");
		if (EventsById.TryGetValue(redaction.redacts, out var point)) {
			var orig_event = point.Value;
			((TimelineEvent)EventsById[redaction.redacts]).Value = new EventWithState(
				orig_event.Event with { content = null, unsigned = (orig_event.Event.unsigned ?? new Api.UnsignedData()) with { redacted_because = redactionEvent } },
				orig_event.State
			);
		}
	}
	

	/// <summary> Create a new room. See <see href="https://spec.matrix.org/latest/client-server-api/#post_matrixclientv3createroom"/>
	/// for explanation of the many parameters. </summary>
	public async Task<Api.RoomID> CreateRoom(
		string? type = null,
		string? room_version = null,
		bool? federate = null,
		string[]? invite = null,
		bool? is_direct = null,
		string? name = null,
		Api.RoomCreationStateEvent[]? initial_state = null,
		Api.PowerLevels? power_level_content_override = null,
		Api.RoomPreset? preset = null,
		string? room_alias_name = null,
		string? topic = null,
		Api.RoomVisibility? visibility = null,
		Api.EventID? predecessor_event_id = null,
		Api.RoomID? predecessor_room_id = null
	) {
		var response = await Retry.RetryAsync(async () => await ApiClient.CreateRoom(new Api.RoomCreationRequest(
			creation_content: new Api.RoomCreation(
				creator: null,
				predecessor: predecessor_room_id is not null && predecessor_event_id is not null ? new Api.PreviousRoom(predecessor_event_id.Value, predecessor_room_id.Value) : null,
				type: type,
				room_version: room_version,
				federate: federate
			),
			initial_state: initial_state,
			invite: invite,
			is_direct: is_direct,
			name: name,
			power_level_content_override: power_level_content_override,
			preset: preset,
			room_alias_name: room_alias_name,
			room_version: room_version,
			topic: topic,
			visibility: visibility
		)));

		return response.room_id;

	}

	/// <summary> Invite user to a room </summary>
	public async Task InviteUser(Api.RoomID roomId, string userId, string? reason = null) {
		await Retry.RetryAsync(async () => await ApiClient.Invite(roomId, new Api.InviteRequest(userId, reason)));
	}

	/// <summary> Join a room </summary>
	public async Task<Api.RoomID> JoinRoom(Api.RoomID roomId, string? reason = null, string[]? server_name = null)
		=> await JoinRoom(roomId.ToString(), reason, server_name);

	/// <summary> Join a room </summary>
	public async Task<Api.RoomID> JoinRoom(string roomIdOrAlias, string? reason = null, string[]? server_name = null) {
		var response = await Retry.RetryAsync(async () => await ApiClient.Join(roomIdOrAlias, new Api.JoinRequest(reason), server_name));

		InvitiedState.Remove(response.room_id);
		return response.room_id;
	}

	/// <summary> Leave a room </summary>
	public async Task LeaveRoom(Api.RoomID roomId, string? reason = null) {
		await Retry.RetryAsync(async () => await ApiClient.Leave(roomId, new Api.LeaveRequest(reason)));
	}

	/// <summary> Exception factory used by the <see cref="ApiClient"/>. </summary>
	private async Task<Exception?> ExceptionFactory(HttpResponseMessage response) {
		if (response.IsSuccessStatusCode) return null;

		var errorResponse = JsonSerializer.Deserialize<Api.ErrorResponse>(await response.Content.ReadAsStringAsync());
		if (errorResponse is not null) {
			if (errorResponse.errcode == "M_UNKNOWN_TOKEN") {
				if (errorResponse.soft_logout is not null && errorResponse.soft_logout!.Value) {
					await Refresh();
					return new Retry.RetryException();
				} else {
					AccessToken = null;
					RefreshToken = null;
					ExpiresAt = null;
					return new LoginRequiredException();
				}
			}

			return new MatrixApiError(errorResponse.errcode, errorResponse.error, response, null);
		}

		return await ApiException.Create(
			response.RequestMessage!,
			response.RequestMessage!.Method,
			response,
			RefitSettings
		);
	}

}

/// <summary> Can be serialized to JSON for persistence of login information between program runs. </summary>
public record struct LoginData(
	Uri Homeserver,
	string? AccessToken,
	string? RefreshToken,
	string? UserId,
	string? DeviceId,
	DateTime? ExpiresAt
);
