﻿using Refit;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace matrix_dotnet;

/// <summary>
/// The main client class for interacting with Matrix. Most operations
/// require you to login first, see <see cref="MatrixClient.PasswordLogin"/>
/// and <see cref="MatrixClient.TokenLogin"/>
/// </summary>
public class MatrixClient {
	public record LoginData(
		Uri Homeserver,
		string? AccessToken,
		string? RefreshToken,
		string? UserId,
		string? DeviceId,
		DateTime? ExpiresAt
	);
	/// <summary> The base URL of the homeserver </summary>
	public Uri Homeserver { get; private set; }
	/// <summary> The underlying API class used to make API calls directly </summary>
	public IMatrixApi Api { get; private set; }

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

		var errorResponse = JsonSerializer.Deserialize<IMatrixApi.ErrorResponse>(await response.Content.ReadAsStringAsync());
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

		var apiUrlB = new UriBuilder(homeserver);
		apiUrlB.Path = "/_matrix/client/v3";

		HttpClient client;
		if (Logger is not null) {
			client = new HttpClient(new HttpLoggingHandler(Logger, new AuthenticatedHttpClientHandler(GetAccessToken)));
		} else {
			client = new HttpClient(new AuthenticatedHttpClientHandler(GetAccessToken));
		}
		client.BaseAddress = apiUrlB.Uri;

		RefitSettings = new RefitSettings {
			ExceptionFactory = ExceptionFactory,
			AuthorizationHeaderValueGetter = GetAccessToken,
			// ContentSerializer = new NewtonsoftJsonContentSerializer()
			ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions {
				// The ObjectToInferredTypesConverter is broken and cannot be used. See https://github.com/reactiveui/refit/issues/1763
				PropertyNameCaseInsensitive = true,
				PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
				Converters = {
					new PolymorphicJsonConverterFactory()
				}
			})
		};

		Api = RestService.For<IMatrixApi>(client, RefitSettings);
	}

	public LoginData ToLoginData() {
		return new LoginData(Homeserver, AccessToken, RefreshToken, UserId, DeviceId, ExpiresAt);
	}

	private void UpdateExpiresAt(int? expiresInMs) {
		if (expiresInMs is null) ExpiresAt = null;
		else ExpiresAt = DateTime.Now.AddMilliseconds((double)expiresInMs);
	}

	private async Task Login(IMatrixApi.LoginRequest request) {
		var response = await Api.Login(request);

		AccessToken = response.access_token;
		RefreshToken = response.refresh_token;
		UserId = response.user_id;
		DeviceId = response.device_id;
		UpdateExpiresAt(response.expires_in_ms);
	}

	/// <summary> Login using a username and a password </summary>
	/// <param name="deviceId">see <see cref="DeviceId"/>.</param>
	public async Task PasswordLogin(string username, string password, string? initialDeviceDisplayName = null, string? deviceId = null)
		=> await PasswordLogin(new IMatrixApi.UserIdentifier(username), password, initialDeviceDisplayName, deviceId);

	/// <summary> Login using an <see cref="IMatrixApi.Identifier"/> and a password </summary>
	/// <param name="deviceId">See <see cref="DeviceId"/>.</param>
	public async Task PasswordLogin(IMatrixApi.Identifier identifier, string password, string? initialDeviceDisplayName = null, string? deviceId = null) {
		await Login(new IMatrixApi.PasswordLoginRequest(identifier, password, initialDeviceDisplayName, deviceId));
	}

	/// <summary> Login using a token </summary>
	/// <param name="deviceId">See <see cref="DeviceId"/>.</param>
	public async Task TokenLogin(string token, string? initialDeviceDisplayName = null, string? deviceId = null) {
		await Login(new IMatrixApi.TokenLoginRequest(token, initialDeviceDisplayName, deviceId));
	}

	/// <summary> Refresh the access token using a refresh token </summary>
	public async Task Refresh() {
		if (RefreshToken is null) throw new LoginRequiredException();
		var response = await Api.Refresh(new IMatrixApi.RefreshRequest(RefreshToken));

		AccessToken = response.access_token;
		RefreshToken = response.refresh_token;
		UpdateExpiresAt(response.expires_in_ms);
	}

	/// <summary> Get joined rooms. <see href="https://spec.matrix.org/v1.11/client-server-api/#get_matrixclientv3joined_rooms"/> </summary>
	public async Task<string[]> GetJoinedRooms() {
		var response = await Retry.RetryAsync(async () => await Api.GetJoinedRooms());

		return response.joined_rooms;
	}

	/// <summary> Send a <c>m.room.message</c> event to a room. </summary>
	/// <returns> The <c>event_id</c> of the sent message </returns>
	public async Task<string> SendMessage<TMessage>(string roomId, TMessage message) where TMessage : IMatrixApi.Message {
		var response = await Retry.RetryAsync(async () => await Api.SendEvent(roomId, "m.room.message", GenerateTransactionId(), message));

		return response.event_id;
	}

	/// <summary> Send a basic <c>m.text</c> message to a room. </summary>
	/// <returns> The <c>event_id</c> of the sent message </returns>
	public async Task<string> SendTextMessage(string roomId, string body) => await SendMessage(roomId, new IMatrixApi.TextMessage(body));

}
