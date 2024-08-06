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

	private async Task<string> GetAccessToken(HttpRequestMessage request, CancellationToken ct) {
		if (!LoggedIn) throw new LoginRequiredException();
		if (Expired) await Refresh();
		return AccessToken!;
	}

	private RefitSettings RefitSettings;

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

	private string GenerateTransactionId() {
		return Guid.NewGuid().ToString();
	}

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
					new JsonStringEnumConverter()
				}
			})
		};

		ApiClient = RestService.For<Api.IMatrixApi>(client, RefitSettings);
	}

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

	/// <summary> Login using a username and a password </summary>
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

	/// <summary> Refresh the access token using a refresh token </summary>
	public async Task Refresh() {
		if (RefreshToken is null) throw new LoginRequiredException();
		var response = await ApiClient.Refresh(new Api.RefreshRequest(RefreshToken));

		AccessToken = response.access_token;
		RefreshToken = response.refresh_token;
		UpdateExpiresAt(response.expires_in_ms);
	}

	/// <summary> Get joined rooms. <see href="https://spec.matrix.org/v1.11/client-server-api/#get_matrixclientv3joined_rooms"/> </summary>
	public async Task<string[]> GetJoinedRooms() {
		var response = await Retry.RetryAsync(async () => await ApiClient.GetJoinedRooms());

		return response.joined_rooms;
	}

	/// <summary> Send a <c>m.room.message</c> event to a room. </summary>
	/// <returns> The <c>event_id</c> of the sent message </returns>
	public async Task<string> SendMessage<TMessage>(string roomId, TMessage message) where TMessage : Api.Message {
		var response = await Retry.RetryAsync(async () => await ApiClient.SendEvent(roomId, "m.room.message", GenerateTransactionId(), message));

		return response.event_id;
	}

	/// <summary> Send a basic <c>m.text</c> message to a room. </summary>
	/// <returns> The <c>event_id</c> of the sent message </returns>
	public async Task<string> SendTextMessage(string roomId, string body) => await SendMessage(roomId, new Api.TextMessage(body));

	public static EventWithState[] Resolve(IEnumerable<Api.Event> events, StateDict? stateDict = null, bool rewind = false) {
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

			list.Add(new EventWithState(
				ev,
				stateDict
			));
		}
		return list.ToArray();
	}

	public StateDict? PresenceState { get; private set; }

	public Dictionary<string, StateDict> InvitiedState { get; private set; } = new();
	public Dictionary<string, StateDict> KnockState { get; private set; } = new();
	public Dictionary<string, JoinedRoom> JoinedRooms { get; private set; } = new();
	public Dictionary<string, LeftRoom> LeftRooms { get; private set; } = new();

	public string? NextBatch { get; private set; }

	public Dictionary<string, ITimelineEvent> EventsById { get; private set; } = new();

	internal void Deduplicate(TimelineEvent e) {
		if (e.Value.Event.event_id is null) return;
		if (EventsById.TryGetValue(e.Value.Event.event_id, out var conflict)) {
			((TimelineEvent)conflict).RemoveSelf();
		}
		EventsById[e.Value.Event.event_id] = e;
	}

	private async Task SyncUnsafe(int timeout) {
		var response = await Retry.RetryAsync(async () => await ApiClient.Sync(timeout: timeout, since: NextBatch));

		string? original_batch = NextBatch;
		NextBatch = response.next_batch;

		if (response.presence is not null)
			PresenceState = Resolve(response.presence.events, PresenceState).Last().State;

		if (response.rooms is not null) {
			if (response.rooms.invite is not null)
				foreach (var invitedRoom in response.rooms.invite) {
					StateDict? state = null;
					InvitiedState.TryGetValue(invitedRoom.Key, out state);
					StateDict? resolvedState = Resolve(invitedRoom.Value.invite_state.events, state).LastOrDefault()?.State;
					if (resolvedState is not null)
						InvitiedState[invitedRoom.Key] = resolvedState;
				}
			if (response.rooms.knock is not null)
				foreach (var knockedRoom in response.rooms.knock) {
					StateDict? state = null;
					KnockState.TryGetValue(knockedRoom.Key, out state);
					StateDict? resolvedState = Resolve(knockedRoom.Value.knock_state.events, state).LastOrDefault()?.State;
					if (resolvedState is not null)
						KnockState[knockedRoom.Key] = resolvedState;
				}
			if (response.rooms.join is not null)
				foreach (var joinedRoom in response.rooms.join) {
					string id = joinedRoom.Key;
					var roomResponse = joinedRoom.Value;
					JoinedRoom? room = null;
					JoinedRooms.TryGetValue(id, out room);

					StateDict? account_data = Resolve(roomResponse.account_data.events, room?.account_data).LastOrDefault()?.State;
					StateDict? ephemeral = Resolve(roomResponse.ephemeral.events, room?.ephemeral).LastOrDefault()?.State;
					StateDict? state = Resolve(roomResponse.state.events, room?.state).LastOrDefault()?.State;
					Api.RoomSummary summary = roomResponse.summary;
					Timeline timeline = room?.timeline ?? new Timeline(this, id);
					timeline.Sync(roomResponse.timeline, state, roomResponse.timeline.prev_batch != original_batch);
					state = timeline.Last?.Value.State;
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
					string id = leftRoom.Key;
					var roomResponse = leftRoom.Value;
					LeftRoom? room = null;
					LeftRooms.TryGetValue(id, out room);

					StateDict? account_data = Resolve(roomResponse.account_data.events, room?.account_data).LastOrDefault()?.State;
					StateDict? state = Resolve(roomResponse.state.events, room?.state).LastOrDefault()?.State;
					Timeline timeline = room?.timeline ?? new Timeline(this, id);
					timeline.Sync(roomResponse.timeline, state, roomResponse.timeline.prev_batch != original_batch);
					state = timeline.Last?.Value.State;

					LeftRooms[id] = new LeftRoom(
						account_data ?? StateDict.Empty,
						state ?? StateDict.Empty,
						timeline
					);
				}
		}

	}

	private bool Syncing = false;
	private object SyncLock = new();

	public async Task Sync(int timeout = 0) {
		lock (SyncLock) {
			if (Syncing) {
				// Another sync is happening, return after it is done.
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
