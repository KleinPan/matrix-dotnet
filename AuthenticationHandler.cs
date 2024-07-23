using System.Net.Http.Headers;

namespace matrix_dotnet;

// <summary>
// This is taken directly from Refit's source, because the class is private
// See <see href="https://github.com/reactiveui/refit/issues/1761">
// </summary>
class AuthenticatedHttpClientHandler : DelegatingHandler {
	readonly Func<HttpRequestMessage, CancellationToken, Task<string>> getToken;

	public AuthenticatedHttpClientHandler(Func<HttpRequestMessage, CancellationToken, Task<string>> getToken, HttpMessageHandler? innerHandler = null) : base(innerHandler ?? new HttpClientHandler()) {
		this.getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		// See if the request has an authorize header
		var auth = request.Headers.Authorization;
		if (auth != null) {
			var token = await getToken(request, cancellationToken).ConfigureAwait(false);
			request.Headers.Authorization = new AuthenticationHeaderValue(auth.Scheme, token);
		}

		return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}
}
