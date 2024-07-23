using Refit;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace matrix_dotnet;

public class MatrixClient {
	public Uri Homeserver { get; private set; }
	public IMatrixApi Api { get; private set; }

	private string? AccessToken;
	private string? RefreshToken;

	public string? UserId { get; private set; }
	public string? DeviceId { get; private set; }
	public DateTime? ExpiresAt { get; private set; }

	public ILogger? Logger;

	public bool LoggedIn {
		get => AccessToken is not null;
	}

	public bool Expired {
		get => AccessToken is not null && ExpiresAt is not null && ExpiresAt < DateTime.Now;
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
			AuthorizationHeaderValueGetter = GetAccessToken
		};

		Api = RestService.For<IMatrixApi>(client, RefitSettings);
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

	public async Task PasswordLogin(string username, string password, string? initialDeviceDisplayName = null, string? deviceId = null)
		=> await PasswordLogin(new IMatrixApi.UserIdentifier(username), password, initialDeviceDisplayName, deviceId);

	public async Task PasswordLogin(IMatrixApi.Identifier identifier, string password, string? initialDeviceDisplayName = null, string? deviceId = null) {
		await Login(new IMatrixApi.PasswordLoginRequest(identifier, password, initialDeviceDisplayName, deviceId));
	}

	public async Task TokenLogin(string username, string token, string? initialDeviceDisplayName = null, string? deviceId = null)
		=> await TokenLogin(new IMatrixApi.UserIdentifier(username), token, initialDeviceDisplayName, deviceId);

	public async Task TokenLogin(IMatrixApi.Identifier identifier, string token, string? initialDeviceDisplayName = null, string? deviceId = null) {
		await Login(new IMatrixApi.TokenLoginRequest(identifier, token, initialDeviceDisplayName, deviceId));
	}

	public async Task Refresh() {
		if (RefreshToken is null) throw new LoginRequiredException();
		var response = await Api.Refresh(new IMatrixApi.RefreshRequest(RefreshToken));

		AccessToken = response.access_token;
		RefreshToken = response.refresh_token;
		UpdateExpiresAt(response.expires_in_ms);
	}


	public async Task<string[]> GetJoinedRooms() {
		var response = await Retry.RetryAsync(async () => await Api.GetJoinedRooms());

		return response.joined_rooms;
	}

	public abstract record Message(string body, string msgtype);
	public record TextMessage(string body) : Message(body: body, msgtype: "m.text");

	public async Task<string> SendMessage<TMessage>(string roomId, TMessage message) where TMessage : Message {
		var response = await Retry.RetryAsync(async () => await Api.SendEvent(roomId, "m.room.message", GenerateTransactionId(), message));

		return response.event_id;
	}
	public async Task<string> SendTextMessage(string roomId, string body) => await SendMessage(roomId, new TextMessage(body));

}
